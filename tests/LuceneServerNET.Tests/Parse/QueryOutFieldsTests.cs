using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Parse;

namespace LuceneServerNET.Tests.Parse;

public class QueryOutFieldsTests
{
    private static object Apply(QueryOutFields outFields, string field, object value, out string name)
    {
        name = field;
        return outFields[field].Invoke(value, ref name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_UsesWildcardAndAllowsAnyField(string text)
    {
        var outFields = new QueryOutFields(text);

        Assert.Equal("*", Assert.Single(outFields.Fields).Name);
        Assert.Empty(outFields.Names);
        Assert.True(outFields.UseField("anything"));
    }

    [Fact]
    public void SimpleList_IsSplitByComma()
    {
        var outFields = new QueryOutFields("title, content ,id");

        Assert.Equal(new[] { "title", "content", "id" }, outFields.Names);
        Assert.True(outFields.UseField("title"));
        Assert.False(outFields.UseField("other"));
    }

    [Fact]
    public void Statements_WithoutTrailingSeparator_AreParsed()
        => Assert.Equal(new[] { "title", "content" }, new QueryOutFields("title;content.CHARS(5)").Names);

    [Fact]
    public void Chars_TruncatesValue()
        => Assert.Equal("Hello...", Apply(new QueryOutFields("content.CHARS(5);"), "content", "Hello World", out _));

    [Fact]
    public void Chars_ShortValue_IsUnchanged()
        => Assert.Equal("Hi", Apply(new QueryOutFields("content.CHARS(5);"), "content", "Hi", out _));

    [Fact]
    public void Words_TruncatesToWordCount()
        => Assert.Equal("one two...", Apply(new QueryOutFields("content.WORDS(2);"), "content", "one two three four", out _));

    [Fact]
    public void As_RenamesField()
    {
        var result = Apply(new QueryOutFields("content.AS(\"alias\");"), "content", "value", out var name);

        Assert.Equal("value", result);
        Assert.Equal("alias", name);
    }

    [Fact]
    public void Chained_MethodsAreAppliedInOrder()
    {
        var result = Apply(new QueryOutFields("content.INCL(\"batman robin joker\").AS(\"incl\");"), "content", "Batman and Robin", out var name);

        Assert.Equal("batman robin", result);
        Assert.Equal("incl", name);
    }

    [Fact]
    public void RegexReplace_ReplacesPattern_AndMarksShortenedValue()
        => Assert.Equal("a-b...", Apply(new QueryOutFields("content.REGEX_REPLACE(\"\\s+\",\"-\");"), "content", "a   b", out _));

    [Fact]
    public void Indexer_UnknownField_FallsBackToWildcard()
    {
        var outFields = new QueryOutFields("*;content.CHARS(2);");

        Assert.Equal("*", outFields["other"].Name);
        Assert.Equal("content", outFields["content"].Name);
    }

    [Fact]
    public void UnknownMethod_Throws()
        => Assert.ThrowsAny<Exception>(() => new QueryOutFields("content.UNKNOWN(1);"));

    [Fact]
    public void SelectOutFields_Wildcard_ReturnsStoredFieldsOnce()
    {
        var mapping = new IndexMapping();
        mapping.AddField(new FieldMapping("title"));
        mapping.AddField(new FieldMapping("content"));
        mapping.AddField(new FieldMapping("secret") { Store = false });

        var selected = new QueryOutFields("*;title.CHARS(3);").SelectOutFields(mapping).Select(f => f.Name);

        Assert.Equal(new[] { "content", "title" }, selected.OrderBy(n => n));
    }

    [Fact]
    public void SelectOutFields_Explicit_IgnoresUnstoredAndUnknownFields()
    {
        var mapping = new IndexMapping();
        mapping.AddField(new FieldMapping("title"));
        mapping.AddField(new FieldMapping("secret") { Store = false });

        var selected = new QueryOutFields("title,secret,unknown").SelectOutFields(mapping).Select(f => f.Name);

        Assert.Equal(new[] { "title" }, selected);
    }
}
