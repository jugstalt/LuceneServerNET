using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LuceneServerNET.IntegrationTests;

/// <summary>
/// Group values are returned exactly (decoded from the BytesRef slice via Utf8ToString()).
/// Lucene.NET currently hands out deep copies (offset 0, exact length), so decoding the whole
/// buffer happened to work too; this test guards the result if that ever changes.
/// </summary>
public class GroupingTests : IClassFixture<LuceneServerFactory>
{
    private readonly HttpClient _client;

    public GroupingTests(LuceneServerFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<JsonElement> Send(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        var json = JsonDocument.Parse(body).RootElement;
        Assert.True(json.GetProperty("success").GetBoolean(), body);
        return json;
    }

    [Fact]
    public async Task Group_ReturnsExactValues()
    {
        var id = "grp" + Guid.NewGuid().ToString("N").Substring(0, 8);

        await Send(await _client.PostAsync($"/lucene/createindex/{id}", null));
        await Send(await _client.PostAsJsonAsync($"/lucene/map/{id}", new
        {
            fields = new object[] { new { name = "title", fieldType = "text" }, new { name = "city", fieldType = "string" } },
            primaryFields = new[] { "title" }
        }));

        // values of different length
        var cities = new[] { "St. Johann im Pongau", "Graz", "Wien", "Graz", "Bad Aussee", "Linz" };
        var docs = cities.Select((c, i) => new Dictionary<string, object> { ["title"] = $"doc {i}", ["city"] = c }).ToArray();
        await Send(await _client.PostAsJsonAsync($"/lucene/index/{id}", docs));
        await Send(await _client.PostAsync($"/lucene/refresh/{id}", null));

        var json = await Send(await _client.GetAsync($"/lucene/group/{id}?groupField=city"));

        var groups = json.GetProperty("hits").EnumerateArray()
            .ToDictionary(g => g.GetProperty("value").GetString()!, g => g.GetProperty("_hits").GetInt32());

        Assert.Equal(
            cities.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count()).OrderBy(kv => kv.Key),
            groups.OrderBy(kv => kv.Key));
    }
}
