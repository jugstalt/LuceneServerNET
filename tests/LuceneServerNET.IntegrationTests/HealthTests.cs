using System.Net;

namespace LuceneServerNET.IntegrationTests;

public class HealthTests : IClassFixture<LuceneServerFactory>
{
    private readonly HttpClient _client;

    public HealthTests(LuceneServerFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Health_WritableRootPath_ReturnsHealthy(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}

public class HealthWithBasicAuthTests : IClassFixture<BasicAuthServerFactory>
{
    private readonly HttpClient _client;

    public HealthWithBasicAuthTests(BasicAuthServerFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Health_BasicAuthEnabled_IsAnonymous(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class UnwritableRootPathServerFactory : LuceneServerFactory
{
    // a file where the directory should be => the RootPath can't be created
    public string BlockingFile { get; } = Path.Combine(Path.GetTempPath(), $"luceneservernet-blocked-{Guid.NewGuid():N}");

    public UnwritableRootPathServerFactory()
    {
        File.WriteAllText(BlockingFile, "");
    }

    protected override IDictionary<string, string> ExtraSettings => new Dictionary<string, string>
    {
        ["LuceneServer:RootPath"] = Path.Combine(BlockingFile, "indices"),
    };

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(BlockingFile); } catch { }
    }
}

public class HealthUnwritableTests : IClassFixture<UnwritableRootPathServerFactory>
{
    private readonly HttpClient _client;

    public HealthUnwritableTests(UnwritableRootPathServerFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_UnwritableRootPath_ReturnsUnhealthy_AliveStaysHealthy()
    {
        var health = await _client.GetAsync("/health");
        var alive = await _client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.Equal("Unhealthy", await health.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }
}
