using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LuceneServerNET.IntegrationTests;

public class LuceneApiTests : IClassFixture<LuceneServerFactory>
{
    private readonly HttpClient _client;

    public LuceneApiTests(LuceneServerFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string NewIndexName() => "idx" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private async Task<JsonElement> GetJson(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> PostJson(string url, object body)
    {
        var response = await _client.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static bool Success(JsonElement result) => result.GetProperty("success").GetBoolean();

    private async Task<string> CreateIndexWithDocuments()
    {
        var id = NewIndexName();

        Assert.True(Success(await GetJson($"/lucene/createindex/{id}")));
        Assert.True(Success(await PostJson($"/lucene/map/{id}", new
        {
            fields = new object[]
            {
                new { name = "title", fieldType = "text" },
                new { name = "content", fieldType = "text" },
                new { name = "category", fieldType = "string" },
                new { name = "geo", fieldType = "geo" },
            },
            primaryFields = new[] { "title", "content" }
        })));
        Assert.True(Success(await PostJson($"/lucene/index/{id}", new object[]
        {
            new Dictionary<string, object> { ["title"] = "Graz", ["content"] = "Hauptstadt der Steiermark", ["category"] = "city",
                                             ["geo"] = "{\"Type\":\"GeoPoint\",\"Longidute\":15.44,\"Latitude\":47.07}" },
            new Dictionary<string, object> { ["title"] = "Wien", ["content"] = "Hauptstadt von Österreich", ["category"] = "city",
                                             ["geo"] = "{\"Type\":\"GeoPoint\",\"Longidute\":16.37,\"Latitude\":48.21}" },
            new Dictionary<string, object> { ["title"] = "Mur", ["content"] = "Fluss durch Graz", ["category"] = "river" },
        })));
        Assert.True(Success(await GetJson($"/lucene/refresh/{id}")));

        return id;
    }

    private static string[] Titles(JsonElement searchResult)
        => searchResult.GetProperty("hits").EnumerateArray()
                       .Select(h => h.GetProperty("title").GetString())
                       .OrderBy(t => t)
                       .ToArray();

    [Fact]
    public async Task Info_ReturnsOk()
    {
        var response = await _client.GetAsync("/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateIndex_ThenExists_ThenRemove()
    {
        var id = NewIndexName();

        Assert.False(Success(await GetJson($"/lucene/indexexists/{id}")));
        Assert.True(Success(await GetJson($"/lucene/createindex/{id}")));
        Assert.True(Success(await GetJson($"/lucene/indexexists/{id}")));
        Assert.True(Success(await GetJson($"/lucene/removeindex/{id}")));
        Assert.False(Success(await GetJson($"/lucene/indexexists/{id}")));
    }

    [Fact]
    public async Task Mapping_ReturnsMappedFields()
    {
        var id = await CreateIndexWithDocuments();

        var result = await GetJson($"/lucene/mapping/{id}");
        var names = result.GetProperty("mapping").GetProperty("fields").EnumerateArray()
                          .Select(f => f.GetProperty("name").GetString());

        Assert.Contains("title", names);
        Assert.Contains("geo", names);
    }

    [Fact]
    public async Task Search_FindsDocumentsByTerm()
    {
        var id = await CreateIndexWithDocuments();

        var result = await GetJson($"/lucene/search/{id}?q=hauptstadt");

        Assert.True(Success(result));
        Assert.Equal(new[] { "Graz", "Wien" }, Titles(result));
    }

    [Fact]
    public async Task Search_WithOutFields_ReturnsOnlyRequestedFields()
    {
        var id = await CreateIndexWithDocuments();

        var result = await GetJson($"/lucene/search/{id}?q=wien&outFields=title");
        var hit = Assert.Single(result.GetProperty("hits").EnumerateArray());

        Assert.Equal("Wien", hit.GetProperty("title").GetString());
        Assert.False(hit.TryGetProperty("content", out _));
    }

    [Fact]
    public async Task Search_WithOutFieldMethods_AppliesMethods()
    {
        var id = await CreateIndexWithDocuments();

        var outFields = Uri.EscapeDataString("title;content.CHARS(4).AS(\"short\");");
        var result = await GetJson($"/lucene/search/{id}?q=wien&outFields={outFields}");
        var hit = Assert.Single(result.GetProperty("hits").EnumerateArray());

        Assert.Equal("Haup...", hit.GetProperty("short").GetString());
    }

    [Fact]
    public async Task Search_WithBBoxFilter_RestrictsHits()
    {
        var id = await CreateIndexWithDocuments();

        var result = await GetJson($"/lucene/search/{id}?q=hauptstadt&filter=bbox(geo:15,46.5,16,47.5)");

        Assert.Equal(new[] { "Graz" }, Titles(result));
    }

    [Fact]
    public async Task Search_WithDistanceFilter_RestrictsHits()
    {
        var id = await CreateIndexWithDocuments();

        var result = await GetJson($"/lucene/search/{id}?q=hauptstadt&filter=dist_km(geo:16.37,48.21,10)");

        Assert.Equal(new[] { "Wien" }, Titles(result));
    }

    [Fact]
    public async Task Remove_DeletesDocumentsByTerm()
    {
        var id = await CreateIndexWithDocuments();

        Assert.True(Success(await GetJson($"/lucene/remove/{id}?term=river&termField=category")));
        await GetJson($"/lucene/refresh/{id}");

        var result = await GetJson($"/lucene/search/{id}?q=graz");
        Assert.Equal(new[] { "Graz" }, Titles(result));
    }

    [Fact]
    public async Task Metadata_CanBeStoredAndRead()
    {
        var id = NewIndexName();
        await GetJson($"/lucene/createindex/{id}");

        Assert.True(Success(await PostJson($"/lucene/addmeta/{id}?name=info", new { metadata = "hello" })));

        var meta = await GetJson($"/lucene/getmeta/{id}?name=info");
        Assert.Equal("hello", meta.GetProperty("metadata").GetString());

        var names = await GetJson($"/lucene/getmetanames/{id}");
        Assert.Contains("info", names.GetProperty("result").EnumerateArray().Select(n => n.GetString()));
    }

    [Fact]
    public async Task Search_UnknownIndex_ReturnsUnsuccessfulResult()
    {
        var result = await GetJson($"/lucene/search/{NewIndexName()}?q=x");

        Assert.False(Success(result));
    }

    [Fact]
    public async Task Search_InvalidFilter_ReturnsUnsuccessfulResult()
    {
        var id = await CreateIndexWithDocuments();

        var result = await GetJson($"/lucene/search/{id}?q=graz&filter=bbox(geo:1,2)");

        Assert.False(Success(result));
    }
}
