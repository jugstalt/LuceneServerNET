using LuceneServerNET.Client;
using LuceneServerNET.Client.Exceptions;
using LuceneServerNET.Core.Models.Mapping;

namespace LuceneServerNET.IntegrationTests;

/// <summary>
/// v2 endpoints used by the admin UI (index list, stats, admin info, whoami) through <see cref="LuceneServerV2Client"/>.
/// </summary>
public class AdminApiTests : IClassFixture<LuceneServerFactory>
{
    private readonly LuceneServerV2Client _client;

    public AdminApiTests(LuceneServerFactory factory)
    {
        var http = factory.CreateClient();
        _client = new LuceneServerV2Client(http.BaseAddress!.ToString(), httpClient: http);
    }

    private static string NewId() => "adm" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private async Task<string> CreateIndexWithDocuments(int count)
    {
        var id = NewId();
        await _client.CreateIndexAsync(id);
        await _client.PutMappingAsync(id, new IndexMapping
        {
            Fields = new List<FieldMapping> { new("title"), new("city", FieldTypes.StringType), new("pop", FieldTypes.Int32Type) },
            PrimaryFields = new List<string> { "title" }
        });
        await _client.AddDocumentsAsync(id, Enumerable.Range(0, count)
            .Select(i => (IDictionary<string, object>)new Dictionary<string, object> { ["title"] = $"document {i}", ["city"] = i % 2 == 0 ? "graz" : "wien", ["pop"] = i })
            .ToList());
        await _client.RefreshIndexAsync(id);
        return id;
    }

    [Fact]
    public async Task ListIndices_ContainsCreatedIndex_WithoutMetaDirectories()
    {
        var id = NewId();
        await _client.CreateIndexAsync(id);

        var indices = (await _client.GetIndicesAsync()).ToList();

        Assert.Contains(id, indices);
        Assert.DoesNotContain(indices, i => i.StartsWith('.'));
    }

    [Fact]
    public async Task Stats_ReturnsDocumentCountAndSize()
    {
        var id = await CreateIndexWithDocuments(7);

        var stats = await _client.GetIndexStatsAsync(id);

        Assert.Equal(id, stats.Name);
        Assert.Equal(7, stats.DocumentCount);
        Assert.True(stats.SizeInBytes > 0);
        Assert.NotNull(stats.LastModifiedUtc);
    }

    [Fact]
    public async Task Stats_OfEmptyIndex_IsZero()
    {
        var id = NewId();
        await _client.CreateIndexAsync(id);

        var stats = await _client.GetIndexStatsAsync(id);

        Assert.Equal(0, stats.DocumentCount);
    }

    [Fact]
    public async Task Stats_OfMissingIndex_Throws()
    {
        await Assert.ThrowsAsync<LuceneServerClientException>(() => _client.GetIndexStatsAsync(NewId()));
    }

    [Fact]
    public async Task ServerInfo_And_WhoAmI()
    {
        var info = await _client.GetServerInfoAsync();
        Assert.False(string.IsNullOrEmpty(info.Version));
        Assert.True(info.UptimeSeconds >= 0);

        // authentication disabled => all roles
        var whoAmI = await _client.WhoAmIAsync();
        Assert.Equal(new[] { "read", "write", "admin" }, whoAmI.Roles);
    }

    [Fact]
    public async Task Search_Group_Metadata_RemoveDocuments()
    {
        var id = await CreateIndexWithDocuments(10);

        var result = await _client.SearchAsync(id, new SearchRequest { Query = "document", Size = 3 });
        Assert.Equal(10, result.TotalHits);
        Assert.Equal(3, result.Hits.Count());

        var groups = (await _client.GroupAsync(id, "city")).ToList();
        Assert.Equal(2, groups.Count);

        await _client.PutMetadataAsync(id, "source", "unit test");
        var metadata = await _client.GetMetadataAsync(id);
        Assert.Equal("unit test", metadata["source"]);

        // clear index (admin UI: "*:*")
        await _client.RemoveDocumentsAsync(id, "*:*");
        await _client.RefreshIndexAsync(id);
        Assert.Equal(0, (await _client.GetIndexStatsAsync(id)).DocumentCount);
    }

    [Fact]
    public async Task IndexExists_And_Remove()
    {
        var id = NewId();
        Assert.False(await _client.IndexExistsAsync(id));

        await _client.CreateIndexAsync(id);
        Assert.True(await _client.IndexExistsAsync(id));

        await _client.RemoveIndexAsync(id);
        Assert.False(await _client.IndexExistsAsync(id));
    }
}

/// <summary>Roles reported by whoami with basic authentication.</summary>
public class AdminApiAuthenticationTests : IClassFixture<AdminApiAuthenticationTests.ReadOnlyFactory>
{
    public class ReadOnlyFactory : LuceneServerFactory
    {
        protected override IDictionary<string, string> ExtraSettings => new Dictionary<string, string>
        {
            ["Authorization:Type"] = "basic",
            ["Authorization:Clients:0:Id"] = "reader",
            ["Authorization:Clients:0:Secret"] = "reader-secret",
            ["Authorization:Clients:0:Roles:0"] = "read",
        };
    }

    private readonly ReadOnlyFactory _factory;

    public AdminApiAuthenticationTests(ReadOnlyFactory factory) => _factory = factory;

    [Fact]
    public async Task ReadClient_GetsReadRole_AndNoAdminInfo()
    {
        var http = _factory.CreateClient();
        var client = new LuceneServerV2Client(http.BaseAddress!.ToString(), "reader", "reader-secret", http);

        var whoAmI = await client.WhoAmIAsync();
        Assert.Equal("reader", whoAmI.ClientId);
        Assert.Equal(new[] { "read" }, whoAmI.Roles);

        await Assert.ThrowsAsync<LuceneServerClientException>(() => client.GetServerInfoAsync());
    }

    [Fact]
    public async Task WrongSecret_Throws()
    {
        var http = _factory.CreateClient();
        var client = new LuceneServerV2Client(http.BaseAddress!.ToString(), "reader", "wrong", http);

        await Assert.ThrowsAsync<LuceneServerClientException>(() => client.WhoAmIAsync());
    }
}
