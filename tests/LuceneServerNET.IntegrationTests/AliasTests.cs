using LuceneServerNET.Client;
using LuceneServerNET.Client.Exceptions;
using LuceneServerNET.Core.Models.Mapping;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LuceneServerNET.IntegrationTests;

public class AliasTests : IClassFixture<LuceneServerFactory>
{
    private readonly HttpClient _http;
    private readonly LuceneServerV2Client _client;

    public AliasTests(LuceneServerFactory factory)
    {
        _http = factory.CreateClient();
        _client = new LuceneServerV2Client(_http.BaseAddress!.ToString(), httpClient: _http);
    }

    private static string NewName(string prefix) => prefix + Guid.NewGuid().ToString("N").Substring(0, 8);

    private async Task<string> CreateIndex(string title)
    {
        var id = NewName("ali");
        await _client.CreateIndexAsync(id);
        await _client.PutMappingAsync(id, new IndexMapping { Fields = new List<FieldMapping> { new("title") }, PrimaryFields = new List<string> { "title" } });
        await _client.AddDocumentsAsync(id, [new Dictionary<string, object> { ["title"] = title }]);
        await _client.RefreshIndexAsync(id);
        return id;
    }

    private async Task<string> SearchTitle(string indexOrAlias)
        => ((JsonElement)(await _client.SearchAsync(indexOrAlias, new SearchRequest())).Hits.Single()["title"]).GetString();

    [Fact]
    public async Task Alias_IsResolved_InV1AndV2_AndCanBeSwitched()
    {
        var first = await CreateIndex("first");
        var second = await CreateIndex("second");
        var alias = NewName("alias");

        await _client.SetAliasAsync(alias, first);
        Assert.Equal(first, (await _client.GetAliasesAsync())[alias]);
        Assert.Equal("first", await SearchTitle(alias));
        Assert.True(await _client.IndexExistsAsync(alias));
        Assert.Equal(first, (await _client.GetIndexStatsAsync(alias)).Name);

        // v1
        var v1 = JsonDocument.Parse(await _http.GetStringAsync($"/lucene/search/{alias}?q=")).RootElement;
        Assert.Equal("first", v1.GetProperty("hits")[0].GetProperty("title").GetString());

        // switch
        await _client.SetAliasAsync(alias, second);
        Assert.Equal("second", await SearchTitle(alias));

        // export resolves the alias too
        var exported = new List<IDictionary<string, object>>();
        await foreach (var d in _client.ExportDocumentsAsync(alias)) exported.Add(d);
        Assert.Equal("second", ((JsonElement)Assert.Single(exported)["title"]).GetString());
    }

    [Fact]
    public async Task Alias_Write_GoesToTheIndex()
    {
        var index = await CreateIndex("a");
        var alias = NewName("alias");
        await _client.SetAliasAsync(alias, index);

        await _client.AddDocumentsAsync(alias, [new Dictionary<string, object> { ["title"] = "b" }]);
        await _client.RefreshIndexAsync(alias);

        Assert.Equal(2, (await _client.GetIndexStatsAsync(index)).DocumentCount);
    }

    [Fact]
    public async Task Rules_Conflicts()
    {
        var index = await CreateIndex("a");
        var other = await CreateIndex("b");
        var alias = NewName("alias");
        await _client.SetAliasAsync(alias, index);

        // an index with the alias name can not be created
        var create = await _http.PutAsync($"/api/v2/indices/{alias}", null);
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);

        // an alias can not have the name of an index
        var sameName = await _http.PutAsJsonAsync($"/api/v2/aliases/{other}", new { index });
        Assert.Equal(HttpStatusCode.Conflict, sameName.StatusCode);

        // an index used by an alias can not be removed (v1 and v2)
        Assert.Equal(HttpStatusCode.Conflict, (await _http.DeleteAsync($"/api/v2/indices/{index}")).StatusCode);
        var v1 = JsonDocument.Parse(await (await _http.DeleteAsync($"/lucene/removeindex/{index}")).Content.ReadAsStringAsync()).RootElement;
        Assert.False(v1.GetProperty("success").GetBoolean());

        // removing via the alias name does not remove the index behind it
        Assert.Equal(HttpStatusCode.Conflict, (await _http.DeleteAsync($"/api/v2/indices/{alias}")).StatusCode);
        Assert.True(await _client.IndexExistsAsync(index));

        // alias to a missing index / to an alias
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PutAsJsonAsync($"/api/v2/aliases/{NewName("x")}", new { index = "missing123" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PutAsJsonAsync($"/api/v2/aliases/{NewName("x")}", new { index = alias })).StatusCode);

        // after removing the alias the index can be removed
        await _client.RemoveAliasAsync(alias);
        await _client.RemoveIndexAsync(index);
        Assert.False(await _client.IndexExistsAsync(index));
    }

    [Fact]
    public async Task RemoveAlias_Missing_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync($"/api/v2/aliases/{NewName("x")}")).StatusCode);
        await Assert.ThrowsAsync<LuceneServerClientException>(() => _client.RemoveAliasAsync(NewName("x")));
    }

    [Fact]
    public async Task IndexList_DoesNotContainAliases()
    {
        var index = await CreateIndex("a");
        var alias = NewName("alias");
        await _client.SetAliasAsync(alias, index);

        var indices = (await _client.GetIndicesAsync()).ToList();
        Assert.Contains(index, indices);
        Assert.DoesNotContain(alias, indices);
    }
}
