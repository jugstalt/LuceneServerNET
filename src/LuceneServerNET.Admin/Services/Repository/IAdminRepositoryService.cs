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

/// <summary>A scheduled import from a database query into an index (see ImportJobRunner).</summary>
public class ImportJobRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Instance { get; set; } = "";
    public long ConnectionId { get; set; }
    public string Sql { get; set; } = "";

    /// <summary>Alias (mode replace_alias) or index name.</summary>
    public string Target { get; set; } = "";

    /// <summary>replace_alias, clear, append (see ImportJobModes).</summary>
    public string Mode { get; set; } = "replace_alias";

    /// <summary>Serialized InferredMapping + phonetics (fixed when the job is saved).</summary>
    public string MappingJson { get; set; } = "";

    public int BatchSize { get; set; } = 500;
    public int MaxErrors { get; set; } = 1000;

    /// <summary>Older index generations kept after switching the alias (mode replace_alias).</summary>
    public int KeepGenerations { get; set; } = 1;

    /// <summary>Cron expression (5 fields: minute hour day month weekday); empty = manual job (run now only).</summary>
    public string Cron { get; set; } = "0 3 * * *";

    public bool HasSchedule => !String.IsNullOrWhiteSpace(Cron);

    /// <summary>Run once at startup if a scheduled run was missed while the admin application was stopped.</summary>
    public bool CatchUp { get; set; } = true;

    /// <summary>Time zone of the cron expression (IANA/Windows id); null = UTC.</summary>
    public string? TimeZone { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
}

public class ImportJobRunRecord
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }

    /// <summary>running, succeeded, warning (rows failed), failed, cancelled, interrupted.</summary>
    public string Status { get; set; } = "running";

    public string TriggeredBy { get; set; } = "";
    public string? IndexName { get; set; }
    public long ReadCount { get; set; }
    public long Imported { get; set; }
    public long Failed { get; set; }
    public string? Message { get; set; }
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

    Task<IReadOnlyList<ImportJobRecord>> GetImportJobsAsync(CancellationToken cancellationToken = default);
    Task<ImportJobRecord?> GetImportJobAsync(long id, CancellationToken cancellationToken = default);
    Task<long> InsertImportJobAsync(ImportJobRecord job, CancellationToken cancellationToken = default);
    Task UpdateImportJobAsync(ImportJobRecord job, CancellationToken cancellationToken = default);

    /// <summary>Deletes the job and its runs.</summary>
    Task DeleteImportJobAsync(long id, CancellationToken cancellationToken = default);

    Task<long> InsertImportJobRunAsync(ImportJobRunRecord run, CancellationToken cancellationToken = default);
    Task UpdateImportJobRunAsync(ImportJobRunRecord run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportJobRunRecord>> GetImportJobRunsAsync(long jobId, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Last run of every job (job id => run).</summary>
    Task<IReadOnlyDictionary<long, ImportJobRunRecord>> GetLastImportJobRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks runs that are still "running" (admin app was stopped) as interrupted; returns the count.</summary>
    Task<int> MarkRunningImportJobRunsInterruptedAsync(CancellationToken cancellationToken = default);
}
