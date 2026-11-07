#nullable enable
using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Core.Models.Mapping;

namespace LuceneServerNET.Tests.Admin;

public class FieldTemplateTests
{
    private static readonly Dictionary<string, object?> Row = new()
    {
        ["PLZ"] = 8010L, ["ORT"] = "Graz", ["STR"] = "Leonhardgürtel", ["HNR"] = "10", ["ORTSTEIL"] = null,
        ["objectid"] = 4711L, ["flaeche"] = 12.5, ["datum"] = new DateTime(2026, 11, 7)
    };

    [Theory]
    [InlineData("{PLZ} {ORT} {STR} {HNR}", "8010 Graz Leonhardgürtel 10")]
    [InlineData("Adresse in {ORT} ({ORTSTEIL})", "Adresse in Graz")]          // empty brackets removed
    [InlineData("{plz}  {ort}", "8010 Graz")]                                  // case insensitive, whitespace collapsed
    [InlineData("{flaeche} m² am {datum}", "12.5 m² am 2026-11-07")]           // invariant numbers, ISO dates
    [InlineData("{UNKNOWN} {ORT}", "Graz")]
    [InlineData("Adresse", "Adresse")]                                         // constant
    public void Render(string template, string expected)
        => Assert.Equal(expected, FieldTemplate.Render(template, Row));

    [Fact]
    public void Variables_And_Replacements()
    {
        var variables = new Dictionary<string, string> { [FieldTemplate.MetaIdVariable] = "abc", [FieldTemplate.CategoryVariable] = "Adresse" };
        Assert.Equal("abc.4711", FieldTemplate.Render("{@metaid}.{objectid}", Row, variables));
        Assert.Equal("Adresse", FieldTemplate.Render("{@category}", Row, variables));
        Assert.Equal("", FieldTemplate.Render("{@other}", Row, variables));

        Assert.Equal("8010 Graz Leonhardg. 10",
            FieldTemplate.Render("{PLZ} {ORT} {STR} {HNR}", Row, null, [new TextReplacement { From = "gürtel", To = "g." }]));
    }

    [Fact]
    public void UnknownPlaceholders()
    {
        Assert.Equal(["X"], FieldTemplate.UnknownPlaceholders("{PLZ} {X} {@metaid}", Row.Keys));
        Assert.Equal(["@metaid"], FieldTemplate.UnknownPlaceholders("{@metaid}", Row.Keys, []));
    }

    [Fact]
    public void TryConvert_TemplateFields()
    {
        var mapping = new InferredMapping
        {
            Fields =
            [
                new InferredField { Column = "PLZ", Name = "plz", FieldType = FieldTypes.Int32Type },
                new InferredField { Name = "id", Template = "{@metaid}.{objectid}", FieldType = FieldTypes.StringType },
                new InferredField { Name = "suggested_text", Template = "{PLZ} {ORT} {STR} {HNR}" },
                new InferredField { Name = "subtext", Template = "{ORTSTEIL}" },                   // empty => not set
                new InferredField { Name = "nummer", Template = "{HNR}", FieldType = FieldTypes.Int32Type },
            ],
            Replacements = [new TextReplacement { From = "Leonhardgürtel", To = "Leonhardg." }]
        };

        Assert.True(ImportService.TryConvert(Row, mapping, out var document, out var error,
                                             new Dictionary<string, string> { ["@metaid"] = "m1" }), error);
        Assert.Equal("m1.4711", document!["id"]);
        Assert.Equal("8010 Graz Leonhardg. 10", document["suggested_text"]);
        Assert.False(document.ContainsKey("subtext"));
        Assert.Equal(10, document["nummer"]);
        Assert.Equal(8010, document["plz"]);

        mapping.Fields[4].Template = "{ORT}";
        Assert.False(ImportService.TryConvert(Row, mapping, out _, out error));
        Assert.Contains("Template of 'nummer'", error);
    }
}
