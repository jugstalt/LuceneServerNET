#nullable enable
using LuceneServerNET.Admin.Services.Geo;
using LuceneServerNET.Admin.Services.Import;

namespace LuceneServerNET.Tests.Admin;

/// <summary>
/// Needs a PostGIS database: set LUCENE_TEST_POSTGIS to a connection string, e.g.
/// docker run --rm -d -p 55432:5432 -e POSTGRES_PASSWORD=test postgis/postgis
/// LUCENE_TEST_POSTGIS="Host=localhost;Port=55432;Username=postgres;Password=test"
/// Without it the test does nothing.
/// </summary>
public class PostGisSourceTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("LUCENE_TEST_POSTGIS");

    [Fact]
    public async Task SelectStar_ReadsGeometryAndGeographyAsEwkb()
    {
        if (ConnectionString is null) return;

        // a temp table only lives in one session: build the rows with VALUES instead
        const string sql = """
            SELECT * FROM (VALUES
                (1, 'polygon', ST_GeomFromText('POLYGON((0 0, 100 0, 100 100, 0 100, 0 0))', 31256), ST_GeogFromText('POINT(15.44 47.07)')),
                (2, 'empty', NULL::geometry, NULL::geography)
            ) AS t(id, name, geom, geog)
            """;

        var rows = new List<IDictionary<string, object?>>();
        await foreach (var row in DatabaseSource.ReadAsync(DbProviders.PostgreSql, ConnectionString, sql))
        {
            rows.Add(row);
        }

        Assert.Equal(2, rows.Count);
        var geom = Assert.IsType<byte[]>(rows[0]["geom"]);
        var geog = Assert.IsType<byte[]>(rows[0]["geog"]);
        Assert.True(GeoConverter.LooksLikeGeometry(geom));

        Assert.Equal(31256, GeoConverter.Parse(geom).Srid);
        Assert.Equal(4326, GeoConverter.Parse(geog).Srid);
        Assert.Null(rows[1]["geom"]);
        Assert.Null(rows[1]["geog"]);

        var inferred = MappingInference.Infer(rows);
        Assert.Contains(inferred.GeoFields, g => g.GeometryColumn == "geom");
    }

    [Fact]
    public async Task AsEwkb_StillWorks()
    {
        if (ConnectionString is null) return;

        await foreach (var row in DatabaseSource.ReadAsync(DbProviders.PostgreSql, ConnectionString,
                           "SELECT 1 AS id, ST_AsEWKB(ST_GeomFromText('POINT(1 2)', 31256)) AS geom"))
        {
            Assert.Equal(31256, GeoConverter.Parse(Assert.IsType<byte[]>(row["geom"])).Srid);
            Assert.Equal(1L, row["id"]);
        }
    }
}
