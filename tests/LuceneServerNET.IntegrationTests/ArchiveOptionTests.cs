using LuceneServerNET.Client;

namespace LuceneServerNET.IntegrationTests;

/// <summary>PUT api/v2/indices/{id}?archive=false: no archive although the server has an archive path.</summary>
public class ArchiveOptionTests : IClassFixture<ArchiveOptionTests.ArchiveFactory>
{
    public class ArchiveFactory : LuceneServerFactory
    {
        public string ArchivePath => Path.Combine(RootPath, "_archive");

        protected override IDictionary<string, string> ExtraSettings => new Dictionary<string, string>
        {
            ["LuceneServer:ArchivePath"] = ArchivePath
        };
    }

    private readonly ArchiveFactory _factory;
    private readonly LuceneServerV2Client _client;

    public ArchiveOptionTests(ArchiveFactory factory)
    {
        _factory = factory;
        var http = factory.CreateClient();
        _client = new LuceneServerV2Client(http.BaseAddress!.ToString(), httpClient: http);
    }

    private static List<IDictionary<string, object>> Documents(int count)
        => Enumerable.Range(0, count).Select(i => (IDictionary<string, object>)new Dictionary<string, object> { ["title"] = $"document {i}" }).ToList();

    private int ArchivedFiles(string index)
    {
        var directory = Directory.GetDirectories(_factory.ArchivePath).FirstOrDefault(d => Path.GetFileName(d) == index);
        return directory is null ? -1 : Directory.GetFiles(directory, "*.json").Length;
    }

    [Fact]
    public async Task CreateIndex_WithAndWithoutArchive()
    {
        var mapping = new Core.Models.Mapping.IndexMapping { Fields = [new Core.Models.Mapping.FieldMapping("title", Core.Models.Mapping.FieldTypes.TextType)], PrimaryFields = ["title"] };

        await _client.CreateIndexAsync("with_archive");
        await _client.PutMappingAsync("with_archive", mapping);
        await _client.AddDocumentsAsync("with_archive", Documents(5));

        await _client.CreateIndexAsync("no_archive", archive: false);
        await _client.PutMappingAsync("no_archive", mapping);
        await _client.AddDocumentsAsync("no_archive", Documents(5));
        await _client.RefreshIndexAsync("no_archive");

        Assert.Equal(5, ArchivedFiles("with_archive"));
        Assert.Equal(-1, ArchivedFiles("no_archive"));
        Assert.Equal(5, (await _client.GetIndexStatsAsync("no_archive")).DocumentCount);
    }
}
