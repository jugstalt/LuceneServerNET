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
}
