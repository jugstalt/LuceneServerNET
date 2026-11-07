using LuceneServerNET.Admin.Services.Auth;
using LuceneServerNET.Admin.Services.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuceneServerNET.Tests.Admin;

public class AdminRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lucene-admin-test-{Guid.NewGuid():N}.db");

    private SqliteAdminRepositoryService Create()
        => new($"Data Source={_path};Pooling=False", NullLogger<SqliteAdminRepositoryService>.Instance);

    [Fact]
    public void MigrationScripts_AreEmbeddedAndOrdered()
    {
        var scripts = SqliteAdminRepositoryService.MigrationScripts();

        Assert.NotEmpty(scripts);
        Assert.Equal(1, scripts[0].Version);
        Assert.Equal(scripts.OrderBy(s => s.Version).Select(s => s.Version), scripts.Select(s => s.Version));
    }

    [Fact]
    public async Task Initialize_IsIdempotent()
    {
        var repository = Create();
        await repository.InitializeAsync();
        await repository.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_version";
        Assert.Equal((long)SqliteAdminRepositoryService.MigrationScripts().Count, (long)command.ExecuteScalar());
    }

    [Fact]
    public async Task Instances_Crud()
    {
        var repository = Create();
        await repository.InitializeAsync();

        var id = await repository.InsertInstanceAsync(new InstanceRecord { Name = "prod", Url = "https://prod", ClientId = "admin", ClientSecretProtected = "xyz" });

        var instance = await repository.GetInstanceAsync(id);
        Assert.Equal("prod", instance.Name);
        Assert.Equal("xyz", instance.ClientSecretProtected);
        Assert.Equal(DateTimeKind.Utc, instance.CreatedUtc.Kind);
        Assert.True((DateTime.UtcNow - instance.CreatedUtc).TotalMinutes < 1);

        instance.Url = "https://prod2";
        await repository.UpdateInstanceAsync(instance);
        Assert.Equal("https://prod2", (await repository.GetInstancesAsync()).Single().Url);

        // names are unique (case insensitive)
        await Assert.ThrowsAnyAsync<SqliteException>(() => repository.InsertInstanceAsync(new InstanceRecord { Name = "PROD", Url = "x" }));

        await repository.DeleteInstanceAsync(id);
        Assert.Empty(await repository.GetInstancesAsync());
    }

    [Theory]
    [InlineData("/i/prod", "/i/prod")]
    [InlineData("//evil.com", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("https://evil.com", "/")]
    [InlineData(null, "/")]
    public void SafeReturnUrl_AllowsOnlyLocalPaths(string returnUrl, string expected)
    {
        Assert.Equal(expected, AdminAuthentication.SafeReturnUrl(returnUrl));
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }
}
