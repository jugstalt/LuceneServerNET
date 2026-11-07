using LuceneServerNET.Admin.Services.Import;
using System.Text;

namespace LuceneServerNET.Tests.Admin;

public class DocumentReaderTests
{
    private static async Task<List<IDictionary<string, object>>> Read(string content, SourceFormat format)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var rows = new List<IDictionary<string, object>>();
        await foreach (var row in DocumentReaders.ReadAsync(stream, format))
        {
            rows.Add(row);
        }
        return rows;
    }

    [Theory]
    [InlineData("name,pop\nGraz,300000\n\"Wien, Stadt\",2000000\n")]
    [InlineData("name;pop\nGraz;300000\n\"Wien, Stadt\";2000000\n")]
    [InlineData("name\tpop\nGraz\t300000\nWien, Stadt\t2000000\n")]
    public async Task Csv_DetectsDelimiter(string csv)
    {
        var rows = await Read(csv, SourceFormat.Csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Wien, Stadt", rows[1]["name"]);
        Assert.Equal("300000", rows[0]["pop"]);
    }

    [Fact]
    public async Task Csv_EmptyValuesAreNull()
    {
        var rows = await Read("a,b\n1,\n", SourceFormat.Csv);

        Assert.Null(rows[0]["b"]);
    }

    [Fact]
    public async Task Json_Array()
    {
        var rows = await Read("""[{"name":"Graz","pop":300000,"area":127.6,"capital":false,"tags":["a"]},{"name":null}]""", SourceFormat.Json);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Graz", rows[0]["name"]);
        Assert.Equal(300000L, rows[0]["pop"]);
        Assert.Equal(127.6, rows[0]["area"]);
        Assert.Equal(false, rows[0]["capital"]);
        Assert.Equal("""["a"]""", rows[0]["tags"]);
        Assert.Null(rows[1]["name"]);
    }

    [Fact]
    public async Task Json_ObjectWithArrayProperty()
    {
        var rows = await Read("""{"count":2,"items":[{"a":1},{"a":2}]}""", SourceFormat.Json);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Json_Ndjson()
    {
        var rows = await Read("{\"a\":1}\n\n{\"a\":2}\n", SourceFormat.Json);

        Assert.Equal([1L, 2L], rows.Select(r => (long)r["a"]));
    }

    [Theory]
    [InlineData("data.json", SourceFormat.Json)]
    [InlineData("data.NDJSON", SourceFormat.Json)]
    [InlineData("data.csv", SourceFormat.Csv)]
    [InlineData("data.txt", SourceFormat.Csv)]
    public void DetectFormat(string fileName, SourceFormat expected)
    {
        Assert.Equal(expected, DocumentReaders.DetectFormat(fileName));
    }
}
