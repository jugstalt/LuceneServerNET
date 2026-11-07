using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Core.Models.Mapping;

namespace LuceneServerNET.Tests.Admin;

public class MappingInferenceTests
{
    private static List<IDictionary<string, object>> Rows(params (string Column, object Value)[][] rows)
        => rows.Select(r => (IDictionary<string, object>)r.ToDictionary(c => c.Column, c => c.Value)).ToList();

    [Theory]
    [InlineData(new object[] { "1", "42", "-7" }, FieldTypes.Int32Type)]
    [InlineData(new object[] { "1.5", "42", "-7e3" }, FieldTypes.DoubleType)]
    [InlineData(new object[] { 1L, 2L }, FieldTypes.Int32Type)]
    [InlineData(new object[] { 1L, 2.5 }, FieldTypes.DoubleType)]
    [InlineData(new object[] { "2024-01-31", "2024-02-01T12:30:00Z" }, FieldTypes.DateTimeType)]
    [InlineData(new object[] { "AT", "DE", "CH-01" }, FieldTypes.StringType)]
    [InlineData(new object[] { "A long text with words", "Another sentence here" }, FieldTypes.TextType)]
    [InlineData(new object[] { "1", "x" }, FieldTypes.StringType)]
    public void InferType(object[] values, string expected)
    {
        Assert.Equal(expected, MappingInference.InferType(values));
    }

    [Fact]
    public void InferType_NoValues_IsText()
    {
        Assert.Equal(FieldTypes.TextType, MappingInference.InferType([]));
    }

    [Fact]
    public void Infer_ProposesPrimaryTextFields_AndGeo()
    {
        var sample = Rows(
            [("name", "Graz Hauptplatz"), ("code", "G1"), ("lat", "47.07"), ("lon", "15.43")],
            [("name", "Wien Stephansplatz"), ("code", "W1"), ("lat", "48.2"), ("lon", "16.37")]);

        var mapping = MappingInference.Infer(sample);

        var name = mapping.Fields.Single(f => f.Column == "name");
        Assert.Equal(FieldTypes.TextType, name.FieldType);
        Assert.True(name.Primary);
        Assert.False(mapping.Fields.Single(f => f.Column == "code").Primary);
        Assert.Equal(FieldTypes.DoubleType, mapping.Fields.Single(f => f.Column == "lat").FieldType);
        var geo = Assert.Single(mapping.GeoFields);
        Assert.Equal(("lon", "lat", "location", 4326), (geo.XColumn, geo.YColumn, geo.FieldName, geo.Epsg));
        Assert.False(geo.IsGeometry);
    }

    [Fact]
    public void Infer_WithoutTextFields_UsesFirstStringFieldAsPrimary()
    {
        var mapping = MappingInference.Infer(Rows([("id", "a1"), ("code", "x")]));

        Assert.Equal(["id"], mapping.Fields.Where(f => f.Primary).Select(f => f.Name));
    }

    [Theory]
    [InlineData("_id", "id")]
    [InlineData("first name", "first_name")]
    [InlineData("___", "field")]
    public void ToFieldName_RemovesReservedPrefixAndWhitespace(string column, string expected)
    {
        Assert.Equal(expected, MappingInference.ToFieldName(column));
    }

    [Fact]
    public void Infer_MakesFieldNamesUnique()
    {
        var mapping = MappingInference.Infer(Rows([("_id", "1"), ("id", "2")]));

        Assert.Equal(["id", "id2"], mapping.Fields.Select(f => f.Name));
    }

    [Fact]
    public void ToIndexMapping_SkipsColumns_AddsGeoField()
    {
        var inferred = new InferredMapping
        {
            Fields =
            [
                new InferredField { Column = "a", Name = "a", FieldType = FieldTypes.TextType, Primary = true },
                new InferredField { Column = "b", Name = "b", Skip = true },
            ],
            GeoFields = [GeoFieldMapping.ForColumns("lon", "lat", "location", 4326)]
        };

        var mapping = MappingInference.ToIndexMapping(inferred, LuceneServerNET.Core.Phonetics.Algorithm.ColognePhonetics);

        Assert.Equal(["a", "location"], mapping.Fields.Select(f => f.Name));
        Assert.Equal(FieldTypes.GeoType, mapping.GetField("location").FieldType);
        Assert.Equal(["a"], mapping.PrimaryFields);
        Assert.Equal(LuceneServerNET.Core.Phonetics.Algorithm.ColognePhonetics, mapping.PrimaryFieldsPhonetics);
        Assert.True(mapping.IsValid());
    }
}
