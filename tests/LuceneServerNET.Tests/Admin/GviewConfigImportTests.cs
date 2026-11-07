#nullable enable
using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Admin.Services.Jobs;

namespace LuceneServerNET.Tests.Admin;

public class GviewConfigImportTests
{
    private const string Config = """
        {
          "lucene_server_connection": {
            "url": "https://lucene.example.test/lucene-allgemein",
            "default_index": "adressen",
            "delete_index": true,
            "encode_characters": "./",
            "phonetic_algorithm": "ColognePhonetics"
          },
          "datasets": [
            {
              "dataset_guid": "f7394b37-1397-4914-b1d0-5a03b11d2949",
              "connection_string": "server=...",
              "feature_classes": [
                {
                  "name": "DU_TOP.TOP_P_ADRESSEN",
                  "srs": 31256,
                  "geometry": true,
                  "category": "Adresse",
                  "filter": "SUCHINDEX = 1",
                  "index_proto": {
                    "suggested_text": "{PLZ} {ORT} {STR} {HNR}",
                    "subtext": "Adresse in {ORT} ({ORTSTEIL})",
                    "thumbnail_url": "https://example.test/addresses-40-b.png"
                  },
                  "metadata": {
                    "sample": "8010 Graz Leonhardgürtel 10",
                    "description": "Adressen für die Steiermark<br/>Suchfelder: PLZ, Zustellort, Straße, Hausnummer",
                    "service": "basis@ccgis_default",
                    "query": "adressen"
                  },
                  "replace": [ { "from": "gürtel", "to": "g." } ]
                },
                {
                  "name": "DU_TOP.STRASSEN",
                  "category": "Straße",
                  "index_proto": { "suggested_text": "{STRNAME}" }
                }
              ]
            }
          ]
        }
        """;

    private static readonly List<IDictionary<string, object?>> Adressen =
    [
        new Dictionary<string, object?> { ["OBJECTID"] = 7L, ["PLZ"] = 8010L, ["ORT"] = "Graz", ["STR"] = "Leonhardgürtel", ["HNR"] = "10", ["ORTSTEIL"] = null, ["SHAPE"] = "POINT(-67000 215000)" },
    ];

    [Fact]
    public async Task BuildJob()
    {
        var config = GviewConfigImport.Parse(Config);
        var sources = GviewConfigImport.FeatureClasses(config)
            .Select(fc => new GviewImportSource { FeatureClass = fc, Sql = GviewConfigImport.ProposeSql(fc) }).ToList();
        Assert.Equal("SELECT * FROM DU_TOP.TOP_P_ADRESSEN WHERE SUCHINDEX = 1", sources[0].Sql);
        Assert.Equal("SELECT * FROM DU_TOP.STRASSEN", sources[1].Sql);

        var (job, settings, warnings) = await GviewConfigImport.BuildJobAsync(config, sources, 5, "prod", "adressen",
            sql => sql.Contains("ADRESSEN")
                ? Task.FromResult<IReadOnlyList<IDictionary<string, object?>>>(Adressen)
                : throw new InvalidOperationException("table not found"));

        Assert.Equal(("adressen", ImportJobModes.ReplaceAlias, "", "prod"), (job.Target, job.Mode, job.Cron, job.Instance));
        Assert.Equal(("./", LuceneServerNET.Core.Phonetics.Algorithm.ColognePhonetics), (settings.EncodeCharacters, settings.Phonetics));
        Assert.Null(ImportJobRunner.Validate(job));

        var adressen = settings.Sources[0];
        Assert.Equal(("Adresse", 5L, WebgisPreset.Name), (adressen.Name, adressen.ConnectionId, adressen.Preset));
        Assert.Equal("basis@ccgis_default", adressen.Metadata.Service);
        Assert.Equal(31256, adressen.Mapping.GeoFields.Single().Epsg);
        Assert.Equal("{@metaid}.{OBJECTID}", adressen.Mapping.Fields.First(f => f.Name == "id").Template);
        Assert.Null(WebgisPreset.Validate(adressen));

        Assert.True(ImportService.TryConvert(Adressen[0], adressen.Mapping, out var document, out var error, adressen.Variables), error);
        Assert.Equal("8010 Graz Leonhardg. 10", document!["suggested_text"]);
        Assert.Equal("Adresse in Graz", document["subtext"]);
        Assert.Equal("Adresse", document["category"]);
        Assert.Equal($"{adressen.MetaId}.7", document["id"]);
        Assert.Equal("https://example.test/addresses-40-b.png", document["thumbnail_url"]);

        // second source: query failed => no mapping, warning
        Assert.False(settings.Sources[1].HasMapping);
        Assert.Contains(warnings, w => w.Contains("Straße") && w.Contains("table not found"));
    }

    [Theory]
    [InlineData("no json")]
    [InlineData("""{ "datasets": [] }""")]
    public void Parse_Invalid(string json) => Assert.Throws<FormatException>(() => GviewConfigImport.Parse(json));
}
