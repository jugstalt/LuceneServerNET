using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Admin.Services.Repository;
using LuceneServerNET.Core.Models.Mapping;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuceneServerNET.Tests.Admin;

public class DatabaseSourceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lucene-admin-source-{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath};Pooling=False";

    public DatabaseSourceTests()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE places (id INTEGER, name TEXT, area REAL, founded TEXT, lat REAL, lon REAL, data BLOB);
            INSERT INTO places VALUES (1, 'Graz', 127.6, '1128-01-01', 47.07, 15.43, x'00');
            INSERT INTO places VALUES (2, 'Wien', 414.9, '0881-01-01', 48.2, 16.37, NULL);
            INSERT INTO places VALUES (3, NULL, NULL, NULL, NULL, NULL, NULL);
            """;
        command.ExecuteNonQuery();
    }

    private async Task<List<IDictionary<string, object>>> Read(string sql)
    {
        var rows = new List<IDictionary<string, object>>();
        await foreach (var row in DatabaseSource.ReadAsync(DbProviders.Sqlite, ConnectionString, sql))
        {
            rows.Add(row);
        }
        return rows;
    }

    [Fact]
    public async Task Read_ConvertsValues()
    {
        var rows = await Read("SELECT id, name, area, data FROM places ORDER BY id");

        Assert.Equal(3, rows.Count);
        Assert.Equal(1L, rows[0]["id"]);
        Assert.Equal("Graz", rows[0]["name"]);
        Assert.Equal(127.6, rows[0]["area"]);
        Assert.Null(rows[0]["data"]);       // binary columns are skipped
        Assert.Null(rows[2]["name"]);       // DBNull => null
    }

    [Fact]
    public async Task Read_DuplicateAndUnnamedColumnsGetUniqueNames()
    {
        var rows = await Read("SELECT a.id, b.id, 1 + 1 AS \"\" FROM places a JOIN places b ON a.id = b.id WHERE a.id = 1");

        Assert.Equal(["id", "id2", "column3"], rows[0].Keys);
    }

    [Fact]
    public async Task Read_WithCommentsAndCte()
    {
        var rows = await Read("-- all places\n/* cte */ WITH p AS (SELECT * FROM places) SELECT name FROM p WHERE id = 2;");

        Assert.Equal("Wien", Assert.Single(rows)["name"]);
    }

    [Theory]
    [InlineData("DELETE FROM places")]
    [InlineData("UPDATE places SET name = 'x'")]
    [InlineData("SELECT 1; DELETE FROM places")]
    [InlineData("  ")]
    public async Task Read_RejectsNonSelectStatements(string sql)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Read(sql));
        Assert.Equal(3, (await Read("SELECT id FROM places")).Count);
    }

    [Fact]
    public async Task Infer_FromDatabaseRows()
    {
        var rows = await Read("SELECT id, name, area, lat, lon FROM places WHERE id < 3");

        var mapping = MappingInference.Infer(rows);

        Assert.Equal(FieldTypes.Int32Type, mapping.Fields.Single(f => f.Column == "id").FieldType);
        Assert.Equal(FieldTypes.DoubleType, mapping.Fields.Single(f => f.Column == "area").FieldType);
        Assert.NotNull(mapping.Geo);
    }

    [Fact]
    public void InferType_DateTimeValues()
    {
        Assert.Equal(FieldTypes.DateTimeType, MappingInference.InferType([new DateTime(2024, 1, 1), DateTime.UtcNow]));
    }

    [Theory]
    [InlineData(12.5, 12.5)]
    [InlineData((short)3, 3L)]
    [InlineData(true, "true")]
    public void ToValue(object input, object expected)
    {
        Assert.Equal(expected, DatabaseSource.ToValue(input));
    }

    [Fact]
    public void ToValue_DecimalAndGuid()
    {
        Assert.Equal(1.25, DatabaseSource.ToValue(1.25m));
        var guid = Guid.NewGuid();
        Assert.Equal(guid.ToString(), DatabaseSource.ToValue(guid));
    }

    [Fact]
    public async Task Connections_AreStoredEncrypted()
    {
        var adminDb = Path.Combine(Path.GetTempPath(), $"lucene-admin-conn-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteAdminRepositoryService($"Data Source={adminDb};Pooling=False", NullLogger<SqliteAdminRepositoryService>.Instance);
            await repository.InitializeAsync();
            var source = new DatabaseSource(repository, new EphemeralDataProtectionProvider(), new ConfigurationBuilder().Build());

            await source.SaveConnectionAsync(null, "places", DbProviders.Sqlite, ConnectionString);

            var record = Assert.Single(await repository.GetDbConnectionsAsync());
            Assert.DoesNotContain(_dbPath, record.ConnectionStringProtected);

            var connection = Assert.Single(await source.GetConnectionsAsync());
            Assert.Equal(ConnectionString, connection.ConnectionString);

            // empty connection string on update keeps the stored one
            await source.SaveConnectionAsync(connection.Id, "places2", DbProviders.Sqlite, "");
            connection = Assert.Single(await source.GetConnectionsAsync());
            Assert.Equal("places2", connection.Name);
            Assert.Equal(ConnectionString, connection.ConnectionString);

            await Assert.ThrowsAsync<ArgumentException>(() => source.SaveConnectionAsync(null, "PLACES2", DbProviders.Sqlite, "x"));
            await Assert.ThrowsAsync<ArgumentException>(() => source.SaveConnectionAsync(null, "other", "Oracle", "x"));

            await source.DeleteConnectionAsync(connection.Id);
            Assert.Empty(await source.GetConnectionsAsync());
        }
        finally
        {
            try { File.Delete(adminDb); } catch { }
        }
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }
}
