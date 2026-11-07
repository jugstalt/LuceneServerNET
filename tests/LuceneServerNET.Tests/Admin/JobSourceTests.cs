#nullable enable
using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Admin.Services.Jobs;
using LuceneServerNET.Admin.Services.Repository;
using LuceneServerNET.Core.Models.Mapping;
using System.Text.Json;

namespace LuceneServerNET.Tests.Admin;

public class JobSourceTests
{
    private static readonly List<IDictionary<string, object?>> Sample =
    [
        new Dictionary<string, object?> { ["OBJECTID"] = 1L, ["PLZ"] = 8010L, ["ORT"] = "Graz", ["STR"] = "Leonhardgürtel", ["HNR"] = "10", ["X"] = -67000.0, ["Y"] = 215000.0 },
        new Dictionary<string, object?> { ["OBJECTID"] = 2L, ["PLZ"] = 8020L, ["ORT"] = "Graz", ["STR"] = "Annenstraße", ["HNR"] = "1", ["X"] = -68000.0, ["Y"] = 214000.0 },
    ];

    private static ImportJobSource WebgisSource(string category = "Adresse")
    {
        var source = new ImportJobSource { Name = category, ConnectionId = 1, Sql = "SELECT * FROM adressen", Preset = WebgisPreset.Name };
        source.Mapping = MappingInference.Infer(Sample);
        source.Mapping.GeoFields[0].Epsg = 31256;
        WebgisPreset.Apply(source.Mapping, "OBJECTID");
        source.Mapping.Fields.First(f => f.Name == "suggested_text").Template = "{PLZ} {ORT} {STR} {HNR}";
        source.Mapping.Fields.First(f => f.Name == "subtext").Template = "Adresse in {ORT}";
        source.Metadata.Category = category;
        return source;
    }

    [Fact]
    public void Webgis_Apply()
    {
        var source = WebgisSource();
        var imported = source.Mapping.Fields.Where(f => !f.Skip).Select(f => f.Name).ToList();

        Assert.Equal(["id", "suggested_text", "subtext", "category", "thumbnail_url"], imported);
        Assert.Equal("{@metaid}.{OBJECTID}", source.Mapping.Fields[0].Template);
        Assert.Equal(("geo", "bbox"), (source.Mapping.GeoFields.Single().FieldName, source.Mapping.GeoFields.Single().BBoxFieldName));
        Assert.Null(WebgisPreset.Validate(source));

        var mapping = new ImportJobMapping { Sources = [source], EncodeCharacters = "./" }.ToIndexMapping();
        Assert.Equal(["suggested_text", "subtext", "category"], mapping.PrimaryFields);
        Assert.False(mapping.GetField("thumbnail_url").Index);
        Assert.Equal(FieldTypes.GeoType, mapping.GetField("geo").FieldType);
        Assert.Equal(['.', '/'], mapping.PrimaryFieldsEncodeCharacters);

        Assert.True(ImportService.TryConvert(Sample[0], source.Mapping, out var document, out var error, source.Variables), error);
        Assert.Equal($"{source.MetaId}.1", document!["id"]);
        Assert.Equal("8010 Graz Leonhardgürtel 10", document["suggested_text"]);
        Assert.Equal("Adresse", document["category"]);
        Assert.True(document.ContainsKey("geo"));
        Assert.False(document.ContainsKey("PLZ"));
    }

    [Fact]
    public void Webgis_Validate()
    {
        Assert.Contains("category", WebgisPreset.Validate(WebgisSource().With(s => s.Metadata.Category = null)));
        Assert.Contains("suggested_text", WebgisPreset.Validate(WebgisSource().With(s => s.Mapping.Fields[1].Template = "")));
        Assert.Contains("@metaid", WebgisPreset.Validate(WebgisSource().With(s => s.Mapping.Fields[0].Template = "{OBJECTID}")));
    }

    [Fact]
    public void ProposeIdColumn()
    {
        var inferred = MappingInference.Infer(Sample);
        Assert.Equal("OBJECTID", WebgisPreset.ProposeIdColumn(inferred, Sample[0].Keys));
        Assert.Equal("PLZ", WebgisPreset.ProposeIdColumn(inferred, ["PLZ", "ORT"]));    // first integer column
    }

    [Fact]
    public void Sources_UnionAndConflicts()
    {
        var settings = new ImportJobMapping { Sources = [WebgisSource("Adresse"), WebgisSource("Straße")] };
        Assert.NotEqual(settings.Sources[0].MetaId, settings.Sources[1].MetaId);
        Assert.Null(ImportJobRunner.ValidateSources(settings));
        Assert.Equal(6, settings.ToIndexMapping().Fields.Count);   // id, suggested_text, subtext, category, thumbnail_url, geo (points: no bbox)

        settings.Sources[1].Mapping.Fields.First(f => f.Name == "subtext").FieldType = FieldTypes.StringType;
        Assert.Contains("subtext", ImportJobRunner.ValidateSources(settings));

        settings.Sources[1] = WebgisSource();
        settings.Sources[1].MetaId = settings.Sources[0].MetaId;
        Assert.Contains("same meta id", ImportJobRunner.ValidateSources(settings));

        settings.Sources[1].MetaId = "Not-A-Guid";
        Assert.Contains("32 hex", ImportJobRunner.ValidateSources(settings));

        Assert.Equal("Add a source", ImportJobRunner.ValidateSources(new ImportJobMapping()));
        Assert.Contains("SELECT", ImportJobRunner.ValidateSources(new ImportJobMapping { Sources = [WebgisSource().With(s => s.Sql = "DELETE FROM orte")] }));
    }

    [Fact]
    public void Metadata_Json()
    {
        var metadata = new JobSourceMetadata
        {
            Category = "Adresse", Sample = "8010 Graz Leonhardgürtel 10", Description = "Adressen<br/>Suchfelder", Service = "basis@ccgis", Query = "adressen",
            Extra = [new TextReplacement { From = "icon", To = "a.png" }]
        };
        var json = metadata.ToJson("abc");

        Assert.StartsWith("{\"id\":\"abc\",\"category\":\"Adresse\"", json);
        Assert.Contains("Leonhardgürtel", json);       // not escaped
        Assert.Contains("<br/>", json);
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("adressen", values["query"]);
        Assert.Equal("a.png", values["icon"]);

        Assert.True(new JobSourceMetadata().IsEmpty);
        Assert.False(metadata.IsEmpty);
    }

    [Fact]
    public void Legacy_Job_BecomesOneSource()
    {
        var legacy = new ImportJobMapping { Mapping = MappingInference.Infer(Sample), Phonetics = LuceneServerNET.Core.Phonetics.Algorithm.ColognePhonetics };
        legacy.Mapping.EncodeCharacters = "./";
        var job = new ImportJobRecord { Name = "orte", ConnectionId = 3, Sql = "SELECT * FROM orte", MappingJson = legacy.ToJson() };

        var settings = ImportJobMapping.ForJob(job);

        var source = Assert.Single(settings.Sources);
        Assert.Equal((3L, "SELECT * FROM orte", "orte"), (source.ConnectionId, source.Sql, source.Name));
        Assert.True(ImportJobSource.IsMetaId(source.MetaId));
        Assert.Equal("./", settings.EncodeCharacters);
        Assert.Equal(LuceneServerNET.Core.Phonetics.Algorithm.ColognePhonetics, settings.Phonetics);
        Assert.Empty(settings.Mapping.Fields);

        // roundtrip keeps the meta id
        var again = ImportJobMapping.ForJob(new ImportJobRecord { MappingJson = settings.ToJson() });
        Assert.Equal(source.MetaId, again.Sources.Single().MetaId);
    }
}

internal static class JobSourceTestExtensions
{
    public static ImportJobSource With(this ImportJobSource source, Action<ImportJobSource> change)
    {
        change(source);
        return source;
    }
}
