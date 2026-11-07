// LuceneServerNET stress test: performance (latency percentiles, throughput) and thread safety
// (errors, lost documents) under parallel indexing, refreshing, searching and grouping.
//
//   dotnet run -c Release --project tests/LuceneServerNET.StressTest -- [options]
//
//   --url <http://host:port>   test an external server (default: host the server in-process)
//   --duration <sec>           measured load phase (default 30)
//   --indices <n>              number of indices (default 4)
//   --seed <n>                 documents per index before the load phase (default 20000)
//   --searchers <n>            parallel search clients (default 16)
//   --writers <n>              parallel writer clients (default 2 per index => indices*2)
//   --batch <n>                documents per index request (default 50)
//   --refresh-every <n>        writer calls /refresh after every n batches (default 5, 0 = never)
//   --user <u> --pwd <p>       basic auth for an external server

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

// No top-level statements here: they would generate a second global "Program" class and
// WebApplicationFactory<Program> would host this tool instead of the server.
static class StressTest
{
static async Task<int> Main(string[] args)
{
var opt = Options.Parse(args);

LocalServerFactory factory = null;
HttpClient client;
if (opt.Url == null)
{
    factory = new LocalServerFactory();
    client = factory.CreateClient();
    Console.WriteLine($"In-process server, RootPath: {factory.RootPath}");
}
else
{
    client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 1024 }) { BaseAddress = new Uri(opt.Url) };
    Console.WriteLine($"External server: {opt.Url}");
}
if (opt.User != null)
{
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opt.User}:{opt.Pwd}")));
}
client.Timeout = TimeSpan.FromSeconds(60);

var stats = new Stats();
var words = new[] { "graz", "wien", "linz", "salzburg", "innsbruck", "klagenfurt", "bregenz", "eisenstadt", "st", "poelten",
                    "berg", "see", "fluss", "strasse", "platz", "gasse", "weg", "hof", "dorf", "stadt" };
var categories = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };

var indices = Enumerable.Range(0, opt.Indices).Select(_ => "stress" + Guid.NewGuid().ToString("N").Substring(0, 8)).ToArray();
var expected = new ConcurrentDictionary<string, long>(indices.ToDictionary(i => i, _ => 0L));
long docSeq = 0;

Dictionary<string, object>[] MakeDocs(int count, Random rnd) => Enumerable.Range(0, count).Select(_ =>
{
    var n = Interlocked.Increment(ref docSeq);
    return new Dictionary<string, object>
    {
        ["id"] = n.ToString(),
        ["title"] = $"{words[rnd.Next(words.Length)]} {words[rnd.Next(words.Length)]} {words[rnd.Next(words.Length)]}",
        ["content"] = string.Join(' ', Enumerable.Range(0, 12).Select(_ => words[rnd.Next(words.Length)])),
        ["category"] = categories[rnd.Next(categories.Length)],
        ["num"] = rnd.Next(100000),
    };
}).ToArray();

async Task<bool> Call(string op, Func<Task<HttpResponseMessage>> request, bool measure = true)
{
    var sw = Stopwatch.StartNew();
    try
    {
        using var response = await request();
        var body = await response.Content.ReadAsStringAsync();
        sw.Stop();

        bool ok = response.IsSuccessStatusCode;
        if (ok)
        {
            using var json = JsonDocument.Parse(body);
            ok = !json.RootElement.TryGetProperty("success", out var s) || s.GetBoolean();
        }
        if (!ok)
        {
            stats.Error(op, $"{(int)response.StatusCode}: {Truncate(body)}");
            return false;
        }
        if (measure)
        {
            stats.Record(op, sw.Elapsed.TotalMilliseconds);
        }
        return true;
    }
    catch (Exception ex)
    {
        stats.Error(op, $"{ex.GetType().Name}: {Truncate(ex.Message)}");
        return false;
    }
}

static string Truncate(string s) => s.Length > 200 ? s.Substring(0, 200) + "..." : s;

#region Setup

Console.WriteLine($"Creating {indices.Length} indices, seeding {opt.Seed} documents each ...");
var setupWatch = Stopwatch.StartNew();
foreach (var id in indices)
{
    await Call("setup", () => client.GetAsync($"/lucene/createindex/{id}"), false);
    await Call("setup", () => client.PostAsJsonAsync($"/lucene/map/{id}", new
    {
        fields = new object[]
        {
            new { name = "id", fieldType = "string" },
            new { name = "title", fieldType = "text" },
            new { name = "content", fieldType = "text", store = false },
            new { name = "category", fieldType = "string" },
            new { name = "num", fieldType = "int32" },
        },
        primaryFields = new[] { "title", "content" }
    }), false);
}
await Parallel.ForEachAsync(indices, async (id, _) =>
{
    var rnd = new Random(id.GetHashCode());
    for (int done = 0; done < opt.Seed; done += 1000)
    {
        var docs = MakeDocs(Math.Min(1000, opt.Seed - done), rnd);
        if (await Call("seed", () => client.PostAsJsonAsync($"/lucene/index/{id}", docs), false))
        {
            expected.AddOrUpdate(id, docs.Length, (_, v) => v + docs.Length);
        }
    }
    await Call("setup", () => client.GetAsync($"/lucene/refresh/{id}"), false);
});
Console.WriteLine($"Setup done in {setupWatch.Elapsed.TotalSeconds:0.0}s, errors: {stats.ErrorCount}");

#endregion

#region Warmup (first searches build caches for sort/group)

foreach (var id in indices)
{
    await Call("warmup", () => client.GetAsync($"/lucene/search/{id}?q=graz&size=20&sortField=num"), false);
    await Call("warmup", () => client.GetAsync($"/lucene/group/{id}?groupField=category"), false);
}

#endregion

#region Load phase

Console.WriteLine($"Load phase: {opt.Duration}s, {opt.Searchers} searchers, {opt.Writers} writers, batch {opt.Batch}, refresh every {opt.RefreshEvery} batches ...");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(opt.Duration));
var loadWatch = Stopwatch.StartNew();

var writerTasks = Enumerable.Range(0, opt.Writers).Select(w => Task.Run(async () =>
{
    var rnd = new Random(1000 + w);
    var id = indices[w % indices.Length];
    int batches = 0;
    while (!cts.IsCancellationRequested)
    {
        var docs = MakeDocs(opt.Batch, rnd);
        if (await Call("index", () => client.PostAsJsonAsync($"/lucene/index/{id}", docs)))
        {
            expected.AddOrUpdate(id, docs.Length, (_, v) => v + docs.Length);
        }
        if (opt.RefreshEvery > 0 && ++batches % opt.RefreshEvery == 0)
        {
            await Call("refresh", () => client.GetAsync($"/lucene/refresh/{id}"));
        }
    }
})).ToArray();

var searcherTasks = Enumerable.Range(0, opt.Searchers).Select(s => Task.Run(async () =>
{
    var rnd = new Random(2000 + s);
    while (!cts.IsCancellationRequested)
    {
        var id = indices[rnd.Next(indices.Length)];
        var w1 = words[rnd.Next(words.Length)];
        var w2 = words[rnd.Next(words.Length)];
        switch (rnd.Next(10))
        {
            case < 6:  // simple term search
                await Call("search", () => client.GetAsync($"/lucene/search/{id}?q={w1}&size=20"));
                break;
            case < 8:  // two terms + sort
                await Call("search+sort", () => client.GetAsync($"/lucene/search/{id}?q={w1}%20{w2}&size=50&sortField=num"));
                break;
            case 8:    // wildcard
                await Call("search wildcard", () => client.GetAsync($"/lucene/search/{id}?q={w1.Substring(0, 2)}*&size=20"));
                break;
            default:   // grouping
                await Call("group", () => client.GetAsync($"/lucene/group/{id}?groupField=category&q={w1}"));
                break;
        }
    }
})).ToArray();

await Task.WhenAll(writerTasks.Concat(searcherTasks));
loadWatch.Stop();

#endregion

#region Consistency check: every indexed document must be findable after a final refresh

Console.WriteLine();
Console.WriteLine("Consistency check ...");
bool consistent = true;
foreach (var id in indices)
{
    await Call("check", () => client.GetAsync($"/lucene/refresh/{id}"), false);
    var groups = await client.GetFromJsonAsync<JsonElement>($"/lucene/group/{id}?groupField=category");
    long found = groups.GetProperty("hits").EnumerateArray().Sum(g => g.GetProperty("_hits").GetInt64());
    var ok = found == expected[id];
    consistent &= ok;
    Console.WriteLine($"  {id}: expected {expected[id],8}, found {found,8}  {(ok ? "OK" : "MISMATCH")}");
}

#endregion

#region Report

stats.Print(loadWatch.Elapsed);
Console.WriteLine();
Console.WriteLine(stats.ErrorCount == 0 && consistent ? "RESULT: PASSED" : "RESULT: FAILED");

#endregion

#region Cleanup

foreach (var id in indices)
{
    await Call("cleanup", () => client.GetAsync($"/lucene/removeindex/{id}"), false);
}
factory?.Dispose();

#endregion

return stats.ErrorCount == 0 && consistent ? 0 : 1;
}
}

#region Helpers

class Options
{
    public string Url, User, Pwd;
    public int Duration = 30, Indices = 4, Seed = 20000, Searchers = 16, Writers = -1, Batch = 50, RefreshEvery = 5;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--url": o.Url = args[++i]; break;
                case "--user": o.User = args[++i]; break;
                case "--pwd": o.Pwd = args[++i]; break;
                case "--duration": o.Duration = int.Parse(args[++i]); break;
                case "--indices": o.Indices = int.Parse(args[++i]); break;
                case "--seed": o.Seed = int.Parse(args[++i]); break;
                case "--searchers": o.Searchers = int.Parse(args[++i]); break;
                case "--writers": o.Writers = int.Parse(args[++i]); break;
                case "--batch": o.Batch = int.Parse(args[++i]); break;
                case "--refresh-every": o.RefreshEvery = int.Parse(args[++i]); break;
            }
        }
        if (o.Writers < 0)
        {
            o.Writers = o.Indices * 2;
        }
        return o;
    }
}

class Stats
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<double>> _latencies = new();
    private readonly ConcurrentDictionary<string, int> _errors = new();
    private readonly ConcurrentQueue<string> _errorSamples = new();
    private int _errorCount;

    public int ErrorCount => _errorCount;

    public void Record(string op, double ms) => _latencies.GetOrAdd(op, _ => new ConcurrentBag<double>()).Add(ms);

    public void Error(string op, string message)
    {
        Interlocked.Increment(ref _errorCount);
        _errors.AddOrUpdate(op, 1, (_, v) => v + 1);
        if (_errorSamples.Count < 10)
        {
            _errorSamples.Enqueue($"[{op}] {message}");
        }
    }

    public void Print(TimeSpan elapsed)
    {
        Console.WriteLine();
        Console.WriteLine($"Load phase: {elapsed.TotalSeconds:0.0}s");
        Console.WriteLine();
        Console.WriteLine($"{"operation",-17}{"count",9}{"req/s",9}{"errors",8}{"avg",9}{"p50",9}{"p95",9}{"p99",9}{"max",9}   (ms)");
        foreach (var op in _latencies.Keys.Union(_errors.Keys).OrderBy(k => k))
        {
            var values = _latencies.TryGetValue(op, out var bag) ? bag.ToArray() : Array.Empty<double>();
            Array.Sort(values);
            double P(double p) => values.Length == 0 ? 0 : values[Math.Min(values.Length - 1, (int)Math.Ceiling(p * values.Length) - 1)];
            Console.WriteLine($"{op,-17}{values.Length,9}{values.Length / elapsed.TotalSeconds,9:0}{_errors.GetValueOrDefault(op),8}" +
                              $"{(values.Length == 0 ? 0 : values.Average()),9:0.00}{P(0.50),9:0.00}{P(0.95),9:0.00}{P(0.99),9:0.00}{(values.Length == 0 ? 0 : values[^1]),9:0.00}");
        }
        if (!_errorSamples.IsEmpty)
        {
            Console.WriteLine();
            Console.WriteLine($"Errors: {_errorCount} (first samples)");
            foreach (var e in _errorSamples)
            {
                Console.WriteLine("  " + e);
            }
        }
    }
}

class LocalServerFactory : WebApplicationFactory<Program>
{
    public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "luceneservernet-stress", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseContentRoot(AppContext.BaseDirectory);  // no test-SDK content root manifest in a console app

        var settings = new Dictionary<string, string>
        {
            ["LuceneServer:RootPath"] = RootPath,
            ["LuceneServer:ArchivePath"] = "",
            ["LuceneServer:AutoRestoreOnStartup"] = "false",
            ["useSwagger"] = "false",
            ["Authorization:Type"] = "none",
            ["Logging:LogLevel:Default"] = "Warning",
        };
        foreach (var kv in settings)
        {
            builder.UseSetting(kv.Key, kv.Value);
        }
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch { /* best effort */ }
    }
}

#endregion
