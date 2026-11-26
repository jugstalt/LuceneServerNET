using LuceneServerNET.Admin.Services.Repository;
using Microsoft.AspNetCore.DataProtection;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Admin.Services.Import;

public static class DbProviders
{
    public const string SqlServer = "SqlServer", PostgreSql = "PostgreSql", Sqlite = "Sqlite";

    public static readonly string[] All = [SqlServer, PostgreSql, Sqlite];

    public static DbConnection CreateConnection(string provider, string connectionString) => provider switch
    {
        SqlServer => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
        PostgreSql => new Npgsql.NpgsqlConnection(connectionString),
        Sqlite => new Microsoft.Data.Sqlite.SqliteConnection(connectionString),
        _ => throw new ArgumentException($"Unknown database provider '{provider}'")
    };

    public static string Example(string provider) => provider switch
    {
        SqlServer => "Server=myserver;Database=mydb;User Id=reader;Password=...;TrustServerCertificate=True",
        PostgreSql => "Host=myserver;Database=mydb;Username=reader;Password=...",
        Sqlite => "Data Source=C:\\data\\mydb.sqlite;Mode=ReadOnly",
        _ => ""
    };
}

/// <summary>A stored database connection (connection string decrypted, server side only).</summary>
public record DbSourceConnection(long Id, string Name, string Provider, string ConnectionString);

/// <summary>
/// Reads documents from a database query. The query runs in a transaction that is always rolled back
/// and must start with SELECT or WITH: use a read-only database user anyway.
/// </summary>
public class DatabaseSource
{
    private readonly IAdminRepositoryService _repository;
    private readonly IDataProtector _protector;
    private readonly IConfiguration _config;

    public DatabaseSource(IAdminRepositoryService repository, IDataProtectionProvider dataProtection, IConfiguration config)
    {
        _repository = repository;
        _protector = dataProtection.CreateProtector("LuceneServerNET.Admin.DbConnections");
        _config = config;
    }

    private int CommandTimeoutSeconds => _config.GetValue("Admin:Import:CommandTimeoutSeconds", 600);

    #region Connections

    public async Task<IReadOnlyList<DbSourceConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default)
        => (await _repository.GetDbConnectionsAsync(cancellationToken))
            .Select(r => new DbSourceConnection(r.Id, r.Name, r.Provider, Unprotect(r.ConnectionStringProtected)))
            .ToList();

    /// <summary>Adds or updates a connection. An empty connection string on update keeps the stored one.</summary>
    public async Task SaveConnectionAsync(long? id, string name, string provider, string? connectionString, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required");
        if (!DbProviders.All.Contains(provider)) throw new ArgumentException($"Unknown provider '{provider}'");

        var existing = (await _repository.GetDbConnectionsAsync(cancellationToken))
            .FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null && existing.Id != id)
        {
            throw new ArgumentException($"A connection with the name '{name}' already exists");
        }

        if (id.HasValue)
        {
            var record = await _repository.GetDbConnectionAsync(id.Value, cancellationToken) ?? throw new ArgumentException("Connection not found");
            record.Name = name;
            record.Provider = provider;
            if (ConnectionStringMask.ContainsMask(connectionString))
            {
                connectionString = ConnectionStringMask.Unmask(connectionString!, Unprotect(record.ConnectionStringProtected));
            }
            if (!String.IsNullOrWhiteSpace(connectionString))
            {
                record.ConnectionStringProtected = _protector.Protect(connectionString);
            }
            await _repository.UpdateDbConnectionAsync(record, cancellationToken);
        }
        else
        {
            if (String.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required");
            await _repository.InsertDbConnectionAsync(new DbConnectionRecord
            {
                Name = name,
                Provider = provider,
                ConnectionStringProtected = _protector.Protect(connectionString)
            }, cancellationToken);
        }
    }

    public Task DeleteConnectionAsync(long id, CancellationToken cancellationToken = default)
        => _repository.DeleteDbConnectionAsync(id, cancellationToken);

    /// <summary>Opens the connection; returns the server version or throws.</summary>
    public static async Task<string> TestAsync(string provider, string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = DbProviders.CreateConnection(provider, connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection.ServerVersion;
    }

    private string Unprotect(string protectedValue)
    {
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch
        {
            return "";  // keys lost => connection string has to be entered again
        }
    }

    #endregion

    #region Read

    private static readonly Regex ReadOnlyStatement = new(@"^\s*(--[^\n]*\n\s*|/\*.*?\*/\s*)*(select|with)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Only a single SELECT (or WITH ... SELECT) statement is allowed.</summary>
    public static void ValidateQuery(string sql)
    {
        if (String.IsNullOrWhiteSpace(sql) || !ReadOnlyStatement.IsMatch(sql))
        {
            throw new ArgumentException("Only SELECT queries (or WITH ... SELECT) are allowed");
        }
        if (sql.TrimEnd().TrimEnd(';').Contains(';'))
        {
            throw new ArgumentException("Only one statement is allowed");
        }
    }

    public IAsyncEnumerable<IDictionary<string, object?>> ReadAsync(DbSourceConnection source, string sql, CancellationToken cancellationToken = default)
        => ReadAsync(source.Provider, source.ConnectionString, sql, CommandTimeoutSeconds, cancellationToken);

    /// <summary>Query with named parameters (@name), see <see cref="QueryParameters"/>.</summary>
    public IAsyncEnumerable<IDictionary<string, object?>> ReadAsync(DbSourceConnection source, string sql, IReadOnlyDictionary<string, object?>? parameters,
                                                                     CancellationToken cancellationToken = default)
        => ReadAsync(source.Provider, source.ConnectionString, sql, CommandTimeoutSeconds, cancellationToken, parameters);

    public static async IAsyncEnumerable<IDictionary<string, object?>> ReadAsync(string provider,
                                                                                  string connectionString,
                                                                                  string sql,
                                                                                  int commandTimeoutSeconds = 600,
                                                                                  [EnumeratorCancellation] CancellationToken cancellationToken = default,
                                                                                  IReadOnlyDictionary<string, object?>? parameters = null)
    {
        ValidateQuery(sql);

        await using var connection = DbProviders.CreateConnection(provider, connectionString);
        await connection.OpenAsync(cancellationToken);

        // never commit anything the query might change
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction;
            command.CommandTimeout = commandTimeoutSeconds;
            QueryParameters.AddTo(command, sql, parameters);

            if (command is Npgsql.NpgsqlCommand npgsqlCommand)
            {
                await RequestSpatialColumnsAsTextAsync(npgsqlCommand, cancellationToken);
            }

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var typeNames = Enumerable.Range(0, reader.FieldCount).Select(i => SafeDataTypeName(reader, i)).ToArray();
            var spatial = typeNames.Select(t => provider == DbProviders.PostgreSql && IsSpatialType(t)).ToArray();

            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (int i = 0; i < columns.Length; i++)
                {
                    if (await reader.IsDBNullAsync(i, cancellationToken))
                    {
                        row[UniqueColumn(columns, i)] = null;
                    }
                    else if (spatial[i])
                    {
                        // PostGIS geometry / geography: the driver has no type for it, the text form is hex EWKB (with SRID)
                        row[UniqueColumn(columns, i)] = Convert.FromHexString(reader.GetFieldValue<string>(i));
                    }
                    else
                    {
                        row[UniqueColumn(columns, i)] = ToValue(ReadValue(reader, i, columns[i], typeNames[i], provider));
                    }
                }
                yield return row;
            }
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Npgsql has no type for PostGIS geometry / geography: request these columns in text format (hex EWKB with SRID).
    /// The column types are read with a schema only query first.
    /// </summary>
    private static async Task RequestSpatialColumnsAsTextAsync(Npgsql.NpgsqlCommand command, CancellationToken cancellationToken)
    {
        bool[] spatial;
        await using (var schema = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken))
        {
            spatial = Enumerable.Range(0, schema.FieldCount).Select(i => IsSpatialType(SafeDataTypeName(schema, i))).ToArray();
        }
        if (spatial.Any(s => s))
        {
            command.UnknownResultTypeList = spatial;
        }
    }

    private static bool IsSpatialType(string typeName)
    {
        var name = typeName.Split('.').Last().Trim('"').ToLowerInvariant();
        return name is "geometry" or "geography";
    }

    private static string SafeDataTypeName(DbDataReader reader, int index)
    {
        try
        {
            return reader.GetDataTypeName(index);
        }
        catch
        {
            return "";
        }
    }

    private static object ReadValue(DbDataReader reader, int index, string column, string typeName, string provider)
    {
        try
        {
            return reader.GetValue(index);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidCastException or InvalidOperationException or System.IO.FileNotFoundException)
        {
            var hint = provider == DbProviders.SqlServer && IsSpatialType(typeName)
                ? $"select it as WKB, e.g. {column}.STAsBinary() AS {column}"
                : "convert it in the query, e.g. to text";
            throw new NotSupportedException($"Column {column} ({typeName}) can not be read: {hint}", ex);
        }
    }

    /// <summary>Unnamed or duplicate columns (e.g. "SELECT a.id, b.id") get a suffix.</summary>
    private static string UniqueColumn(string[] columns, int index)
    {
        var name = String.IsNullOrWhiteSpace(columns[index]) ? $"column{index + 1}" : columns[index];
        var before = columns.Take(index).Count(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
        return before == 0 ? name : $"{name}{before + 1}";
    }

    /// <summary>Database values => values the import pipeline understands.</summary>
    internal static object? ToValue(object? value) => value switch
    {
        null => null,
        decimal d => (double)d,
        float f => (double)f,
        short s => (long)s,
        byte b => (long)b,
        int i => (long)i,
        DateTimeOffset dto => dto.UtcDateTime,
        Guid g => g.ToString(),
        byte[] b when Geo.GeoConverter.LooksLikeGeometry(b) => b,   // WKB / EWKB geometry
        byte[] => null,                     // other binary columns are not imported
        bool b => b ? "true" : "false",
        _ => value
    };

    #endregion
}
