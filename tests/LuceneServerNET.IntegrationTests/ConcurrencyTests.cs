using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LuceneServerNET.IntegrationTests;

/// <summary>
/// Small load test: parallel indexing, refreshing and searching on several indices.
/// Before SearcherManager, a refresh disposed the reader while searches were still using it
/// (AlreadyClosedException => HTTP 500).
/// </summary>
public class ConcurrencyTests : IClassFixture<LuceneServerFactory>
{
    private readonly HttpClient _client;

    public ConcurrencyTests(LuceneServerFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<JsonElement> Send(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        var json = JsonDocument.Parse(body).RootElement;
        Assert.True(json.GetProperty("success").GetBoolean(), body);
        return json;
    }

    [Fact]
    public async Task ParallelIndexRefreshAndSearch_NeverFails()
    {
        var indices = Enumerable.Range(0, 3).Select(i => "load" + Guid.NewGuid().ToString("N").Substring(0, 8)).ToArray();
        const int batches = 30, batchSize = 20;

        foreach (var id in indices)
        {
            await Send(await _client.GetAsync($"/lucene/createindex/{id}"));
            await Send(await _client.PostAsJsonAsync($"/lucene/map/{id}", new
            {
                fields = new object[] { new { name = "title", fieldType = "text" }, new { name = "n", fieldType = "string" } },
                primaryFields = new[] { "title" }
            }));
        }

        using var cts = new CancellationTokenSource();

        var writers = indices.Select(id => Task.Run(async () =>
        {
            for (int b = 0; b < batches; b++)
            {
                var docs = Enumerable.Range(0, batchSize)
                    .Select(i => new Dictionary<string, object> { ["title"] = $"doc lorem {b} {i}", ["n"] = $"{b}_{i}" })
                    .ToArray();
                await Send(await _client.PostAsJsonAsync($"/lucene/index/{id}", docs));
                await Send(await _client.GetAsync($"/lucene/refresh/{id}"));
            }
        })).ToArray();

        var searchers = Enumerable.Range(0, 8).Select(s => Task.Run(async () =>
        {
            int count = 0;
            while (!cts.IsCancellationRequested)
            {
                var id = indices[count++ % indices.Length];
                await Send(await _client.GetAsync($"/lucene/search/{id}?q=lorem&size=200"));
                await Send(await _client.GetAsync($"/lucene/group/{id}?groupField=n"));
            }
            return count;
        })).ToArray();

        await Task.WhenAll(writers);
        cts.Cancel();
        var searchCounts = await Task.WhenAll(searchers);

        Assert.All(searchCounts, c => Assert.True(c > 0));

        // after the final refresh every document is visible
        foreach (var id in indices)
        {
            var result = await Send(await _client.GetAsync($"/lucene/search/{id}?q=lorem&size=10000"));
            Assert.Equal(batches * batchSize, result.GetProperty("hits").GetArrayLength());
        }
    }
}
