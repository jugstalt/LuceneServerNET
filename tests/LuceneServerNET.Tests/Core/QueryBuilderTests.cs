using LuceneServerNET.Core;

namespace LuceneServerNET.Tests.Core;

public class QueryBuilderTests
{
    private readonly QueryBuilder _builder = new QueryBuilder();

    [Fact]
    public void ParseTerm_MultipleWords_CombinesExactAndWildcardQuery()
        => Assert.Equal("(+hello +world) OR (+hello* +world*)", _builder.ParseTerm("hello world"));

    [Fact]
    public void ParseTerm_ShortWords_AreNotRequired()
        => Assert.Equal("(ab) OR (ab*)", _builder.ParseTerm("ab"));

    [Fact]
    public void ParseTerm_ShortNumber_IsRequired()
        => Assert.Equal("(+12) OR (+12*)", _builder.ParseTerm("12"));

    [Fact]
    public void ParseTerm_HyphensAndMultipleSpaces_AreNormalized()
        => Assert.Equal("(+abc +def +ghi) OR (+abc* +def* +ghi*)", _builder.ParseTerm("  abc-def    ghi "));

    [Fact]
    public void ParseTerm_TrailingSlash_PreventsWildcard()
        => Assert.Equal("(+abc//) OR (+abc//)", _builder.ParseTerm("abc/"));

    [Fact]
    public void ParseTerm_Group_IsRequiredWithoutWildcard()
        => Assert.Equal("(+(a)) OR (+(a))", _builder.ParseTerm("(a)"));
}
