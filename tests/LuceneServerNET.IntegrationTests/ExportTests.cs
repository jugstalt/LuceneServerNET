using LuceneServerNET.Client;
using LuceneServerNET.Client.Exceptions;
using LuceneServerNET.Core.Models.Mapping;
using System.Net;
using System.Text.Json;

namespace LuceneServerNET.IntegrationTests;

/// <summary>GET api/v2/indices/{id}/documents/_export (NDJSON) and copying an index with it.</summary>
public class ExportTests : IClassFixture<LuceneServerFactory>
{
    private readonly HttpClient _http;
    private readonly LuceneServerV2Client _client;

    public ExportTests(LuceneServerFactory factory)
    {
        _http = factory.CreateClient();
        _client = new LuceneServerV2Client(_http.BaseAddress!.ToString(), httpClient: _http);
    }

    private async Task<string> CreateIndex(int count)
    {
        var id = "exp" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await _client.CreateIndexAsync(id);
        await _client.PutMappingAsync(id, new IndexMapping
        {
            Fields = new List<FieldMapping>
            {
                new("title"),
                new("city", FieldTypes.StringType),
                new("pop", FieldTypes.Int32Type),
                new("area", FieldTypes.DoubleType),
                new("founded", FieldTypes.DateTimeType),
                new("location", FieldTypes.GeoType),
                new("secret") { Store = false },
            },
            PrimaryFields = new List<string> { "title" }
        });
        await _client.AddDocumentsAsync(id, Enumerable.Range(0, count).Select(i => (IDictionary<string, object>)new Dictionary<string, object>
        {
            ["title"] = $"document {i}",
            ["city"] = $"c{i}",
            ["pop"] = i,
            ["area"] = i + 0.5,
            ["founded"] = "2024-01-31",
            ["location"] = "15.44 47.07",
            ["secret"] = "not stored",
        }).ToList());
        await _client.RefreshIndexAsync(id);
        return id;
    }

    private async Task<List<IDictionary<string, object>>> Export(string id, Action<long?> onTotal = null)
    {
        var documents = new List<IDictionary<string, object>>();
        await foreach (var document in _client.ExportDocumentsAsync(id, total => onTotal?.Invoke(total)))
        {
            documents.Add(document);
        }
        return documents;
    }

    private static string Value(IDictionary<string, object> document, string field) => ((JsonElement)document[field]).ToString();

    [Fact]
    public async Task Export_AllStoredFields_WithoutGuid_AndTotal()
    {
        var id = await CreateIndex(2500);   // more than one flush block
        long? total = null;

        var documents = await Export(id, t => total = t);

        Assert.Equal(2500, total);
        Assert.Equal(2500, documents.Count);
        var first = documents.Single(d => Value(d, "city") == "c1");
        Assert.Equal("document 1", Value(first, "title"));
        Assert.Equal("1", Value(first, "pop"));
        Assert.Equal("1.5", Value(first, "area"));
        Assert.Equal("2024-01-31T00:00:00Z", Value(first, "founded"));
        Assert.Equal("15.44 47.07", Value(first, "location"));
        Assert.False(first.ContainsKey("_guid"));
        Assert.False(first.ContainsKey("secret"));
    }

    [Fact]
    public async Task Export_SkipsDeletedDocuments()
    {
        var id = await CreateIndex(10);
        await _client.RemoveDocumentsAsync(id, "c3", "city");
        await _client.RefreshIndexAsync(id);

        var documents = await Export(id);

        Assert.Equal(9, documents.Count);
        Assert.DoesNotContain(documents, d => Value(d, "city") == "c3");
    }

    [Fact]
    public async Task Export_EmptyIndex()
    {
        var id = "exp" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await _client.CreateIndexAsync(id);
        await _client.PutMappingAsync(id, new IndexMapping { Fields = new List<FieldMapping> { new("title") }, PrimaryFields = new List<string> { "title" } });

        Assert.Empty(await Export(id));
    }

    [Fact]
    public async Task Export_MissingIndex_404()
    {
        var response = await _http.GetAsync("/api/v2/indices/doesnotexist123/documents/_export");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await Assert.ThrowsAsync<LuceneServerClientException>(() => Export("doesnotexist123"));
    }

    [Fact]
    public async Task ExportedDocuments_CanBeIndexedAgain()
    {
        var source = await CreateIndex(20);
        var target = source + "_copy";
        await _client.CreateIndexAsync(target);
        await _client.PutMappingAsync(target, await _client.GetMappingAsync(source));

        await _client.AddDocumentsAsync(target, await Export(source));
        await _client.RefreshIndexAsync(target);

        Assert.Equal(20, (await _client.GetIndexStatsAsync(target)).DocumentCount);
        var hit = Assert.Single((await _client.SearchAsync(target, new SearchRequest { Query = "city:c7" })).Hits);
        Assert.Equal("7", ((JsonElement)hit["pop"]).ToString());
        Assert.Equal("2024-01-31T00:00:00Z", ((JsonElement)hit["founded"]).ToString());

        // numeric and spatial fields work in the copy
        Assert.Equal(20, (await _client.SearchAsync(target, new SearchRequest { Filter = "dist_km(location:15.44,47.07,1)", Size = 0 })).TotalHits);
        var sorted = (await _client.SearchAsync(target, new SearchRequest { SortField = "pop", SortReverse = true, Size = 1 })).Hits.Single();
        Assert.Equal("19", ((JsonElement)sorted["pop"]).ToString());
    }
}
