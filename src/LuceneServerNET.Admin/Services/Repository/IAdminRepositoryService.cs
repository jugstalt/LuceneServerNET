namespace LuceneServerNET.Admin.Services.Repository;

/// <summary>A LuceneServer.NET instance stored in the admin database (the secret is protected with DataProtection).</summary>
public class InstanceRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? ClientId { get; set; }
    public string? ClientSecretProtected { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
}

/// <summary>A database connection used as import source (the connection string is protected with DataProtection).</summary>
public class DbConnectionRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ConnectionStringProtected { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
}

/// <summary>An entry of the audit log (who did what, when, on which instance).</summary>
public class AuditRecord
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Instance { get; set; }
    public string? Target { get; set; }
    public string? Details { get; set; }
    public bool Success { get; set; } = true;
}

public class AuditQuery
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? UserName { get; set; }
    public string? Action { get; set; }
    public string? Instance { get; set; }

    /// <summary>Searched in target and details.</summary>
    public string? Text { get; set; }

    public bool? Success { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; } = 50;
}

/// <summary>
/// All persistence of the admin UI. The implementation owns every SQL statement (Dapper).
/// </summary>
public interface IAdminRepositoryService
{
    /// <summary>Creates / migrates the schema (embedded scripts Sql/NNN_*.sql, tracked in schema_version).</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstanceRecord>> GetInstancesAsync(CancellationToken cancellationToken = default);
    Task<InstanceRecord?> GetInstanceAsync(long id, CancellationToken cancellationToken = default);
    Task<long> InsertInstanceAsync(InstanceRecord instance, CancellationToken cancellationToken = default);
    Task UpdateInstanceAsync(InstanceRecord instance, CancellationToken cancellationToken = default);
    Task DeleteInstanceAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DbConnectionRecord>> GetDbConnectionsAsync(CancellationToken cancellationToken = default);
    Task<DbConnectionRecord?> GetDbConnectionAsync(long id, CancellationToken cancellationToken = default);
    Task<long> InsertDbConnectionAsync(DbConnectionRecord connection, CancellationToken cancellationToken = default);
    Task UpdateDbConnectionAsync(DbConnectionRecord connection, CancellationToken cancellationToken = default);
    Task DeleteDbConnectionAsync(long id, CancellationToken cancellationToken = default);

    Task InsertAuditAsync(AuditRecord record, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<AuditRecord> Items, int Total)> QueryAuditAsync(AuditQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAuditActionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes entries older than the given time; returns the number of deleted entries.</summary>
    Task<int> DeleteAuditOlderThanAsync(DateTime utc, CancellationToken cancellationToken = default);
}
