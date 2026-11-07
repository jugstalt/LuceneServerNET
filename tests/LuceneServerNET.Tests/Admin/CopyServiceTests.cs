using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Mapping;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LuceneServerNET.Tests.Admin;

public class CopyServiceTests
{
    /// <summary>In-memory fake of the v2 API: one index "src" with documents, records what is written to other indices.</summary>
    private class FakeServer : HttpMessageHandler
    {
        public HashSet<string> Indices { get; } = ["src"];
        public int SourceDocuments { get; set; } = 25;
        public List<string> Requests { get; } = [];
        public List<int> Batches { get; } = [];
        public Dictionary<string, string> WrittenMetadata { get; } = [];
        public string? WrittenMapping { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);   // api v2 indices {id} ...
            var id = parts.Length > 3 ? parts[3] : "";

            if (request.Method == HttpMethod.Get && parts.Length == 4)
                return Indices.Contains(id) ? Ok("""{"success":true}""") : Json(HttpStatusCode.NotFound, """{"success":false,"message":"not found"}""");
            if (request.Method == HttpMethod.Put && parts.Length == 4) { Indices.Add(id); return Ok("""{"success":true}"""); }
            if (path.EndsWith("/mapping") && request.Method == HttpMethod.Get)
                return Ok("""{"success":true,"mapping":{"fields":[{"name":"title","fieldType":"text","store":true,"index":true},{"name":"x","fieldType":"text","store":false,"index":true}],"primaryFields":["title"]}}""");
            if (path.EndsWith("/mapping") && request.Method == HttpMethod.Put) { WrittenMapping = await request.Content!.ReadAsStringAsync(cancellationToken); return Ok("""{"success":true}"""); }
            if (path.EndsWith("/meta") && request.Method == HttpMethod.Get)
                return Ok("""{"success":true,"metadata":{"source":"db","version":"3"}}""");
            if (parts.Length == 6 && parts[4] == "meta" && request.Method == HttpMethod.Put)
            {
                WrittenMetadata[parts[5]] = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.GetProperty("Metadata").GetString()!;
                return Ok("""{"success":true}""");
            }
            if (path.EndsWith("/documents/_export"))
            {
                var ndjson = String.Join("\n", Enumerable.Range(0, SourceDocuments).Select(i => $$"""{"title":"t{{i}}"}"""));
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson") };
                response.Headers.Add("X-Total-Count", SourceDocuments.ToString());
                return response;
            }
            if (path.EndsWith("/documents") && request.Method == HttpMethod.Post)
            {
                Batches.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.GetArrayLength());
                return Ok("""{"success":true}""");
            }
            return Ok("""{"success":true}""");
        }

        private static HttpResponseMessage Ok(string json) => Json(HttpStatusCode.OK, json);
        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static (LuceneServerV2Client Source, LuceneServerV2Client Target, FakeServer Server) Create(string targetUrl = "http://target")
    {
        var server = new FakeServer();
        var http = new HttpClient(server);
        return (new LuceneServerV2Client("http://source", httpClient: http), new LuceneServerV2Client(targetUrl, httpClient: http), server);
    }

    [Fact]
    public async Task Copy_CreatesTarget_CopiesMappingMetadataAndDocuments()
    {
        var (source, target, server) = Create();
        long? total = null;

        var report = await new CopyService().CopyAsync(source, "src", target, "dst", new CopyOptions { BatchSize = 10 }, onTotal: t => total = t);

        Assert.Null(report.FatalError);
        Assert.Equal(25, total);
        Assert.Equal(25, report.Read);
        Assert.Equal(25, report.Imported);
        Assert.Equal([10, 10, 5], server.Batches);
        Assert.Contains("PUT /api/v2/indices/dst", server.Requests);
        Assert.Contains("\"title\"", server.WrittenMapping);
        Assert.Equal("db", server.WrittenMetadata["source"]);
        Assert.Equal("POST /api/v2/indices/dst/refresh", server.Requests.Last());
    }

    [Fact]
    public async Task Copy_ExistingTarget_FailsWithoutReplace()
    {
        var (source, target, server) = Create();
        server.Indices.Add("dst");

        var report = await new CopyService().CopyAsync(source, "src", target, "dst", new CopyOptions());

        Assert.Contains("already exists", report.FatalError);
        Assert.Empty(server.Batches);
    }

    [Fact]
    public async Task Copy_ExistingTarget_ReplaceClearsDocumentsFirst()
    {
        var (source, target, server) = Create();
        server.Indices.Add("dst");

        var report = await new CopyService().CopyAsync(source, "src", target, "dst", new CopyOptions { ReplaceExisting = true, CopyMetadata = false });

        Assert.Null(report.FatalError);
        Assert.Contains("DELETE /api/v2/indices/dst/documents", server.Requests);
        Assert.DoesNotContain("PUT /api/v2/indices/dst", server.Requests);
        Assert.Empty(server.WrittenMetadata);
    }

    [Fact]
    public async Task Copy_SameIndex_IsRejected()
    {
        var (source, _, server) = Create();

        var report = await new CopyService().CopyAsync(source, "src", source, "src", new CopyOptions());

        Assert.Contains("same index", report.FatalError);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public void NotStoredFields()
    {
        var mapping = new IndexMapping { Fields = new List<FieldMapping> { new("a"), new("b") { Store = false }, new DocumentGuidField() } };
        Assert.Equal(["b"], CopyService.NotStoredFields(mapping));
    }
}
