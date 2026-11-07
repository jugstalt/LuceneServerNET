using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Mapping;
using System.Globalization;
using System.Text.Json;

namespace LuceneServerNET.IntegrationTests;

/// <summary>
/// Numbers are written and returned culture invariant, also on a server with a german culture
/// (before: stored doubles came back as "47,0707" and the string "47.0707" was indexed as 470707).
/// </summary>
public class NumericCultureTests : IClassFixture<LuceneServerFactory>
{
    private readonly LuceneServerV2Client _client;

    public NumericCultureTests(LuceneServerFactory factory)
    {
        var http = factory.CreateClient();
        _client = new LuceneServerV2Client(http.BaseAddress!.ToString(), httpClient: http);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Numbers_AreCultureInvariant(bool indexed)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("de-AT");
        try
        {
            var id = "num" + Guid.NewGuid().ToString("N").Substring(0, 8);
            await _client.CreateIndexAsync(id);
            await _client.PutMappingAsync(id, new IndexMapping
            {
                Fields = new List<FieldMapping>
                {
                    new("title"),
                    new("lat", FieldTypes.DoubleType) { Index = indexed },
                    new("area", FieldTypes.SingleType) { Index = indexed },
                    new("pop", FieldTypes.Int32Type) { Index = indexed }
                },
                PrimaryFields = new List<string> { "title" }
            });

            // numbers as JSON numbers and as (invariant) strings
            await _client.AddDocumentsAsync(id, new List<IDictionary<string, object>>
            {
                new Dictionary<string, object> { ["title"] = "number", ["lat"] = 47.0707, ["area"] = 127.5f, ["pop"] = 300000 },
                new Dictionary<string, object> { ["title"] = "text", ["lat"] = "47.0707", ["area"] = "127.5", ["pop"] = "300000" },
            });
            await _client.RefreshIndexAsync(id);

            var hits = (await _client.SearchAsync(id, new SearchRequest { Size = 10 })).Hits.ToList();

            Assert.Equal(2, hits.Count);
            foreach (var hit in hits)
            {
                Assert.Equal("47.0707", ((JsonElement)hit["lat"]).GetString());
                Assert.Equal("127.5", ((JsonElement)hit["area"]).GetString());
                Assert.Equal("300000", ((JsonElement)hit["pop"]).GetString());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.DefaultThreadCurrentCulture = null;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dates_AreUtc_IndependentOfServerTimeZone(bool indexed)
    {
        var id = "dat" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await _client.CreateIndexAsync(id);
        await _client.PutMappingAsync(id, new IndexMapping
        {
            Fields = new List<FieldMapping> { new("title"), new("date", FieldTypes.DateTimeType) { Index = indexed } },
            PrimaryFields = new List<string> { "title" }
        });

        await _client.AddDocumentsAsync(id, new List<IDictionary<string, object>>
        {
            new Dictionary<string, object> { ["title"] = "a", ["date"] = "1128-01-01" },                  // no time zone => UTC
            new Dictionary<string, object> { ["title"] = "b", ["date"] = "2024-06-01T12:00:00+02:00" },   // offset => converted
        });
        await _client.RefreshIndexAsync(id);

        var hits = (await _client.SearchAsync(id, new SearchRequest { Size = 10, SortField = "title" })).Hits.ToList();

        string Date(string title) => ((JsonElement)hits.Single(h => ((JsonElement)h["title"]).GetString() == title)["date"]).GetString();
        Assert.Equal("1128-01-01T00:00:00Z", Date("a"));
        Assert.Equal("2024-06-01T10:00:00Z", Date("b"));
    }
}
