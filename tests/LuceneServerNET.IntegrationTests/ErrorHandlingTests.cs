using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LuceneServerNET.IntegrationTests;

/// <summary>
/// v2 returns errors with HTTP status codes, v1 keeps HTTP 200 + { success: false }.
/// </summary>
public class ErrorHandlingTests : IClassFixture<LuceneServerFactory>
{
    private readonly HttpClient _client;

    public ErrorHandlingTests(LuceneServerFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string NewIndexName() => "idx" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private async Task<string> CreateIndex()
    {
        var id = NewIndexName();
        var response = await _client.PutAsync($"/api/v2/indices/{id}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.False(String.IsNullOrEmpty(body.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task V2_UnknownIndex_Returns404()
        => await AssertError(await _client.PostAsync($"/api/v2/indices/{NewIndexName()}/refresh", null), HttpStatusCode.NotFound);

    [Fact]
    public async Task V2_CreateExistingIndex_Returns409()
    {
        var id = await CreateIndex();
        await AssertError(await _client.PutAsync($"/api/v2/indices/{id}", null), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task V2_InvalidQuery_Returns400()
    {
        var id = await CreateIndex();
        await AssertError(await _client.GetAsync($"/api/v2/indices/{id}/search?q=title:("), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task V2_InvalidFilter_Returns400()
    {
        var id = await CreateIndex();
        await AssertError(await _client.GetAsync($"/api/v2/indices/{id}/search?q=*:*&filter=bbox(1,2"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task V2_SizeAboveMaxResultWindow_Returns400()
    {
        var id = await CreateIndex();
        await AssertError(await _client.GetAsync($"/api/v2/indices/{id}/search?q=*:*&size=100000"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task V1_InvalidQuery_StillReturns200WithErrorBody()
    {
        var id = await CreateIndex();
        await AssertError(await _client.GetAsync($"/lucene/search/{id}?q=title:("), HttpStatusCode.OK);
    }
}
