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

    #region Database connections (import sources)

    private const string DbConnectionColumns = "id, name, provider, connection_string_protected, created_utc, modified_utc";

    public async Task<IReadOnlyList<DbConnectionRecord>> GetDbConnectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return (await connection.QueryAsync<DbConnectionRecord>($"SELECT {DbConnectionColumns} FROM db_connections ORDER BY name")).ToList();
    }

    public async Task<DbConnectionRecord?> GetDbConnectionAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<DbConnectionRecord>($"SELECT {DbConnectionColumns} FROM db_connections WHERE id = @id", new { id });
    }

    public async Task<long> InsertDbConnectionAsync(DbConnectionRecord record, CancellationToken cancellationToken = default)
    {
        record.CreatedUtc = record.ModifiedUtc = DateTime.UtcNow;

        await using var connection = await OpenAsync(cancellationToken);
        record.Id = await connection.ExecuteScalarAsync<long>(
            @"INSERT INTO db_connections (name, provider, connection_string_protected, created_utc, modified_utc)
              VALUES (@Name, @Provider, @ConnectionStringProtected, @CreatedUtc, @ModifiedUtc);
              SELECT last_insert_rowid();", record);
        return record.Id;
    }

    public async Task UpdateDbConnectionAsync(DbConnectionRecord record, CancellationToken cancellationToken = default)
    {
        record.ModifiedUtc = DateTime.UtcNow;

        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            @"UPDATE db_connections SET name = @Name, provider = @Provider,
                     connection_string_protected = @ConnectionStringProtected, modified_utc = @ModifiedUtc
              WHERE id = @Id", record);
    }

    public async Task DeleteDbConnectionAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync("DELETE FROM db_connections WHERE id = @id", new { id });
    }

    #endregion

    #region Audit log

    public async Task InsertAuditAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            @"INSERT INTO audit_log (timestamp_utc, user_name, action, instance, target, details, success)
              VALUES (@TimestampUtc, @UserName, @Action, @Instance, @Target, @Details, @Success)", record);
    }

    public async Task<(IReadOnlyList<AuditRecord> Items, int Total)> QueryAuditAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var where = new List<string>();
        var parameters = new DynamicParameters();

        if (query.FromUtc.HasValue) { where.Add("timestamp_utc >= @from"); parameters.Add("from", query.FromUtc.Value); }
        if (query.ToUtc.HasValue) { where.Add("timestamp_utc < @to"); parameters.Add("to", query.ToUtc.Value); }
        if (!String.IsNullOrWhiteSpace(query.UserName)) { where.Add("user_name = @user COLLATE NOCASE"); parameters.Add("user", query.UserName.Trim()); }
        if (!String.IsNullOrWhiteSpace(query.Action)) { where.Add("action = @action"); parameters.Add("action", query.Action); }
        if (!String.IsNullOrWhiteSpace(query.Instance)) { where.Add("instance = @instance COLLATE NOCASE"); parameters.Add("instance", query.Instance.Trim()); }
        if (!String.IsNullOrWhiteSpace(query.Text)) { where.Add("(target LIKE @text OR details LIKE @text)"); parameters.Add("text", $"%{query.Text.Trim()}%"); }
        if (query.Success.HasValue) { where.Add("success = @success"); parameters.Add("success", query.Success.Value); }

        var whereSql = where.Count == 0 ? "" : "WHERE " + String.Join(" AND ", where);
        parameters.Add("limit", Math.Clamp(query.Limit, 1, 1000));
        parameters.Add("offset", Math.Max(0, query.Offset));

        await using var connection = await OpenAsync(cancellationToken);
        var total = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM audit_log {whereSql}", parameters);
        var items = await connection.QueryAsync<AuditRecord>(
            $@"SELECT id, timestamp_utc, user_name, action, instance, target, details, success
               FROM audit_log {whereSql}
               ORDER BY timestamp_utc DESC, id DESC
               LIMIT @limit OFFSET @offset", parameters);

        return (items.ToList(), total);
    }

    public async Task<IReadOnlyList<string>> GetAuditActionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return (await connection.QueryAsync<string>("SELECT DISTINCT action FROM audit_log ORDER BY action")).ToList();
    }

    public async Task<int> DeleteAuditOlderThanAsync(DateTime utc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteAsync("DELETE FROM audit_log WHERE timestamp_utc < @utc", new { utc });
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
