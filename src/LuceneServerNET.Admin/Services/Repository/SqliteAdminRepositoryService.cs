using Dapper;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Admin.Services.Repository;

public class SqliteAdminRepositoryService : IAdminRepositoryService
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteAdminRepositoryService> _logger;

    static SqliteAdminRepositoryService()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.RemoveTypeMap(typeof(DateTime));
        SqlMapper.AddTypeHandler(new UtcDateTimeHandler());
    }

    public SqliteAdminRepositoryService(string connectionString, ILogger<SqliteAdminRepositoryService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    private async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    #region Schema

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, script TEXT NOT NULL, applied_utc TEXT NOT NULL)");

        var applied = (await connection.QueryAsync<long>("SELECT version FROM schema_version")).ToHashSet();

        foreach (var (version, name, sql) in MigrationScripts())
        {
            if (applied.Contains(version))
            {
                continue;
            }

            _logger.LogInformation("Applying admin database migration {Script}", name);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await connection.ExecuteAsync(sql, transaction: transaction);
            await connection.ExecuteAsync(
                "INSERT INTO schema_version (version, script, applied_utc) VALUES (@version, @name, @now)",
                new { version, name, now = DateTime.UtcNow }, transaction);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    /// <summary>Embedded resources Sql/NNN_name.sql ordered by NNN.</summary>
    internal static IReadOnlyList<(long Version, string Name, string Sql)> MigrationScripts()
    {
        var assembly = typeof(SqliteAdminRepositoryService).Assembly;
        var regex = new Regex(@"\.Sql\.(?<v>\d+)_[^.]+\.sql$", RegexOptions.IgnoreCase);

        return assembly.GetManifestResourceNames()
            .Select(n => (Name: n, Match: regex.Match(n)))
            .Where(x => x.Match.Success)
            .Select(x =>
            {
                using var stream = assembly.GetManifestResourceStream(x.Name)!;
                using var reader = new StreamReader(stream);
                var version = long.Parse(x.Match.Groups["v"].Value, CultureInfo.InvariantCulture);
                var name = x.Name[(x.Name.IndexOf(".Sql.", StringComparison.Ordinal) + 5)..];
                return (version, name, reader.ReadToEnd());
            })
            .OrderBy(x => x.version)
            .ToArray();
    }

    #endregion

    #region Instances

    private const string InstanceColumns = "id, name, url, client_id, client_secret_protected, created_utc, modified_utc";

    public async Task<IReadOnlyList<InstanceRecord>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return (await connection.QueryAsync<InstanceRecord>($"SELECT {InstanceColumns} FROM instances ORDER BY name")).ToList();
    }

    public async Task<InstanceRecord?> GetInstanceAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InstanceRecord>($"SELECT {InstanceColumns} FROM instances WHERE id = @id", new { id });
    }

    public async Task<long> InsertInstanceAsync(InstanceRecord instance, CancellationToken cancellationToken = default)
    {
        instance.CreatedUtc = instance.ModifiedUtc = DateTime.UtcNow;

        await using var connection = await OpenAsync(cancellationToken);
        instance.Id = await connection.ExecuteScalarAsync<long>(
            @"INSERT INTO instances (name, url, client_id, client_secret_protected, created_utc, modified_utc)
              VALUES (@Name, @Url, @ClientId, @ClientSecretProtected, @CreatedUtc, @ModifiedUtc);
              SELECT last_insert_rowid();", instance);
        return instance.Id;
    }

    public async Task UpdateInstanceAsync(InstanceRecord instance, CancellationToken cancellationToken = default)
    {
        instance.ModifiedUtc = DateTime.UtcNow;

        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            @"UPDATE instances SET name = @Name, url = @Url, client_id = @ClientId,
                     client_secret_protected = @ClientSecretProtected, modified_utc = @ModifiedUtc
              WHERE id = @Id", instance);
    }

    public async Task DeleteInstanceAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("DELETE FROM instances WHERE id = @id", new { id });
    }

    #endregion

    /// <summary>SQLite stores DateTime as ISO 8601 text; write and read it as UTC.</summary>
    private class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(System.Data.IDbDataParameter parameter, DateTime value)
            => parameter.Value = value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        public override DateTime Parse(object value)
            => DateTime.Parse(value.ToString()!, CultureInfo.InvariantCulture,
                              DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }
}
