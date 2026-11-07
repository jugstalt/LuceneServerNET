using LuceneServerNET.Admin.Services.Audit;
using LuceneServerNET.Admin.Services.Repository;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuceneServerNET.Tests.Admin;

public class AuditLogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lucene-admin-audit-{Guid.NewGuid():N}.db");

    private async Task<SqliteAdminRepositoryService> CreateAsync()
    {
        var repository = new SqliteAdminRepositoryService($"Data Source={_path};Pooling=False", NullLogger<SqliteAdminRepositoryService>.Instance);
        await repository.InitializeAsync();
        return repository;
    }

    [Fact]
    public async Task Log_And_Query_WithFilters()
    {
        var repository = await CreateAsync();
        var audit = new AuditLogger(repository, NullLogger<AuditLogger>.Instance);

        await audit.LogAsync("alice", AuditActions.IndexCreate, "prod", "orte", "5 fields");
        await audit.LogAsync("bob", AuditActions.IndexDelete, "prod", "alt");
        await audit.LogAsync("alice", AuditActions.Import, "test", "orte", "file orte.csv · failed 3", success: false);
        await audit.LogAsync("", AuditActions.LoginFailed, success: false);

        var (all, total) = await repository.QueryAuditAsync(new AuditQuery());
        Assert.Equal(4, total);
        Assert.Equal(AuditActions.LoginFailed, all[0].Action);           // newest first
        Assert.Equal("(anonymous)", all[0].UserName);

        Assert.Equal(2, (await repository.QueryAuditAsync(new AuditQuery { UserName = "ALICE" })).Total);
        Assert.Equal(2, (await repository.QueryAuditAsync(new AuditQuery { Instance = "prod" })).Total);
        Assert.Equal(2, (await repository.QueryAuditAsync(new AuditQuery { Text = "orte" })).Total);
        Assert.Equal(2, (await repository.QueryAuditAsync(new AuditQuery { Success = false })).Total);
        Assert.Single((await repository.QueryAuditAsync(new AuditQuery { Action = AuditActions.IndexDelete })).Items);

        var page = await repository.QueryAuditAsync(new AuditQuery { Offset = 1, Limit = 2 });
        Assert.Equal(4, page.Total);
        Assert.Equal(2, page.Items.Count);

        Assert.Equal(
            new[] { AuditActions.Import, AuditActions.IndexCreate, AuditActions.IndexDelete, AuditActions.LoginFailed }.OrderBy(a => a, StringComparer.Ordinal),
            await repository.GetAuditActionsAsync());
    }

    [Fact]
    public async Task Query_ByTimeRange_And_Cleanup()
    {
        var repository = await CreateAsync();
        await repository.InsertAuditAsync(new AuditRecord { TimestampUtc = DateTime.UtcNow.AddDays(-200), UserName = "a", Action = "old" });
        await repository.InsertAuditAsync(new AuditRecord { TimestampUtc = DateTime.UtcNow.AddDays(-1), UserName = "a", Action = "new" });

        Assert.Equal(1, (await repository.QueryAuditAsync(new AuditQuery { FromUtc = DateTime.UtcNow.AddDays(-7) })).Total);
        Assert.Equal(1, (await repository.QueryAuditAsync(new AuditQuery { ToUtc = DateTime.UtcNow.AddDays(-7) })).Total);

        Assert.Equal(1, await repository.DeleteAuditOlderThanAsync(DateTime.UtcNow.AddDays(-180)));
        Assert.Equal("new", Assert.Single((await repository.QueryAuditAsync(new AuditQuery())).Items).Action);
    }

    [Fact]
    public async Task Log_TruncatesLongDetails()
    {
        var repository = await CreateAsync();
        await new AuditLogger(repository, NullLogger<AuditLogger>.Instance).LogAsync("a", "x", details: new string('d', 5000));

        Assert.Equal(4000, Assert.Single((await repository.QueryAuditAsync(new AuditQuery())).Items).Details!.Length);
    }

    [Fact]
    public async Task Log_NeverThrows()
    {
        // database not initialized => insert fails, the action must not fail
        var repository = new SqliteAdminRepositoryService($"Data Source={_path};Pooling=False", NullLogger<SqliteAdminRepositoryService>.Instance);
        await new AuditLogger(repository, NullLogger<AuditLogger>.Instance).LogAsync("a", "x");
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }
}
