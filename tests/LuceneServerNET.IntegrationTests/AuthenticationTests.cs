using System.Net;

namespace LuceneServerNET.IntegrationTests;

public class BasicAuthServerFactory : LuceneServerFactory
{
    protected override IDictionary<string, string> ExtraSettings => new Dictionary<string, string>
    {
        ["Authorization:Type"] = "basic",
    };
}

public class AuthenticationTests : IClassFixture<BasicAuthServerFactory>
{
    private readonly HttpClient _client;

    public AuthenticationTests(BasicAuthServerFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/lucene/search/test?q=x")]
    [InlineData("/lucene/createindex/test")]
    [InlineData("/info")]
    public async Task BasicAuthEnabled_WithoutCredentials_ReturnsUnauthorized(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
