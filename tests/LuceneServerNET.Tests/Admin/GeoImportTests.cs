using LuceneServerNET.Admin.Services.Geo;
using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Core.Models.Mapping;
using SpatialNET;
using SpatialNET.Imp;
using SpatialNET.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LuceneServerNET.Tests.Admin;

public class GeoImportTests
{
    private const string GrazPolygonWgs84 = "POLYGON((15.40 47.05, 15.48 47.05, 15.48 47.09, 15.40 47.09, 15.40 47.05))";

    private static double[] BBox(string bbox) => bbox.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();

    #region Parse

    [Theory]
    [InlineData("POINT (15 47)", true)]
    [InlineData("SRID=31256;POINT (-101323 207623)", true)]
    [InlineData("multipolygon(((0 0,1 0,1 1,0 0)))", true)]
    [InlineData("{\"type\":\"Point\",\"coordinates\":[15,47]}", true)]
    [InlineData("Graz", false)]
    [InlineData("{\"name\":\"x\"}", false)]
    public void LooksLikeGeometry_Text(string value, bool expected)
    {
        Assert.Equal(expected, GeoConverter.LooksLikeGeometry(value));
    }

    [Fact]
    public void LooksLikeGeometry_Bytes()
    {
        var wkb = Wkt.Read("POINT (15 47)", ImpGeometryFactory.Instance).ToWkb();
        Assert.True(GeoConverter.LooksLikeGeometry(wkb));
        Assert.False(GeoConverter.LooksLikeGeometry(new byte[] { 0x00 }));
        Assert.False(GeoConverter.LooksLikeGeometry(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A }));   // png
    }

    [Fact]
    public void Parse_Ewkt_Srid()
    {
        var parsed = GeoConverter.Parse("SRID=31256;POINT (-101323.59 207623.96)");
        Assert.Equal(31256, parsed.Srid);
        Assert.IsAssignableFrom<IPoint2D>(parsed.Geometry);
    }

    [Fact]
    public void Parse_Wkb_And_Ewkb_Srid()
    {
        var wkb = Wkt.Read("POINT (15 47)", ImpGeometryFactory.Instance).ToWkb();
        Assert.Null(GeoConverter.Parse(wkb).Srid);

        // EWKB (little endian): byte order, type | 0x20000000, srid, x, y
        var ewkb = new byte[1 + 4 + 4 + 16];
        ewkb[0] = 1;
        BitConverter.GetBytes(1u | 0x20000000u).CopyTo(ewkb, 1);
        BitConverter.GetBytes(31256).CopyTo(ewkb, 5);
        BitConverter.GetBytes(-101323.59).CopyTo(ewkb, 9);
        BitConverter.GetBytes(207623.96).CopyTo(ewkb, 17);

        var parsed = GeoConverter.Parse(ewkb);
        Assert.Equal(31256, parsed.Srid);
        Assert.Equal(-101323.59, ((IPoint2D)parsed.Geometry).X, 6);
    }

    [Fact]
    public void Parse_GeoJson_WithLegacyCrs()
    {
        var parsed = GeoConverter.Parse("{\"type\":\"Point\",\"coordinates\":[-101323.59,207623.96],\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:EPSG::31256\"}}}");
        Assert.Equal(31256, parsed.Srid);
    }

    #endregion

    #region Transform

    [Fact]
    public void FromXY_ProjectedToWgs84()
    {
        // Proj4Net README: WGS84 15/47 => EPSG:31256 -101323.59 207623.96
        var result = GeoConverter.FromXY(-101323.59, 207623.96, 31256);
        Assert.Equal(15, result.Longitude, 4);
        Assert.Equal(47, result.Latitude, 4);
        Assert.Null(result.BBox);
    }

    [Fact]
    public void FromXY_Wgs84_IsUnchanged()
    {
        var result = GeoConverter.FromXY(15.4395, 47.0707, 4326);
        Assert.Equal("15.4395 47.0707", result.Point);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999999)]
    public void UnknownEpsg_Throws(int epsg)
    {
        Assert.Throws<ArgumentException>(() => GeoConverter.ValidateEpsg(epsg));
    }

    [Fact]
    public void FromXY_OutsideOfWgs84_Throws()
    {
        // projected coordinates read as lon/lat
        Assert.Throws<ArgumentException>(() => GeoConverter.FromXY(-101323.59, 207623.96, 4326));
    }

    #endregion

    #region Geometries

    [Fact]
    public void FromGeometry_Point_HasNoBBox()
    {
        var result = GeoConverter.FromGeometry("POINT (15.4395 47.0707)", 4326, includeGeoJson: false);
        Assert.Equal("15.4395 47.0707", result.Point);
        Assert.Null(result.BBox);
        Assert.Null(result.GeoJson);
    }

    [Fact]
    public void FromGeometry_Polygon_PointOnSurface_And_BBox()
    {
        var result = GeoConverter.FromGeometry(GrazPolygonWgs84, 4326, includeGeoJson: false);

        Assert.InRange(result.Longitude, 15.40, 15.48);
        Assert.InRange(result.Latitude, 47.05, 47.09);
        Assert.Equal([15.40, 47.05, 15.48, 47.09], BBox(result.BBox!));
    }

    [Fact]
    public void FromGeometry_ConcavePolygon_PointIsInside()
    {
        // "U" shape: the centroid is outside, the point on surface must be inside
        var u = "POLYGON((0 0, 3 0, 3 3, 2 3, 2 1, 1 1, 1 3, 0 3, 0 0))";
        var result = GeoConverter.FromGeometry(u, 4326, includeGeoJson: false);

        var polygon = Wkt.Read<IPolygon2D>(u, ImpGeometryFactory.Instance);
        Assert.True(polygon.Intersects(ImpGeometryFactory.Instance.CreatePoint(new SpatialNET.Coordinate(result.Longitude, result.Latitude), null!)));
    }

    [Fact]
    public void FromGeometry_Projected_UsesSridOfValue_And_WritesGeoJson()
    {
        // square of 1 km in EPSG:31256 around the Proj4Net sample point, EWKT SRID wins over the default (4326)
        var wkt = "SRID=31256;POLYGON((-101823 207123, -100823 207123, -100823 208123, -101823 208123, -101823 207123))";
        var result = GeoConverter.FromGeometry(wkt, 4326, includeGeoJson: true);

        Assert.Equal(15, result.Longitude, 2);
        Assert.Equal(47, result.Latitude, 2);

        var bbox = BBox(result.BBox!);
        Assert.True(bbox[0] < 15 && bbox[2] > 15 && bbox[1] < 47 && bbox[3] > 47);

        using var geoJson = JsonDocument.Parse(result.GeoJson!);
        Assert.Equal("Polygon", geoJson.RootElement.GetProperty("type").GetString());
        Assert.False(geoJson.RootElement.TryGetProperty("crs", out _));
        var first = geoJson.RootElement.GetProperty("coordinates")[0][0];
        Assert.InRange(first[0].GetDouble(), 14.9, 15.1);
        Assert.InRange(first[1].GetDouble(), 46.9, 47.1);
        // 7 decimals
        Assert.True(first[0].GetRawText().Split('.')[1].Length <= 7);
    }

    [Fact]
    public void FromGeometry_LineString_And_Multi()
    {
        var line = GeoConverter.FromGeometry("LINESTRING(15 47, 16 48)", 4326, false);
        Assert.Equal([15, 47, 16, 48], BBox(line.BBox!));

        var multi = GeoConverter.FromGeometry("MULTIPOLYGON(((15 47, 15.1 47, 15.1 47.1, 15 47)),((16 48, 16.1 48, 16.1 48.1, 16 48)))", 4326, false);
        Assert.Equal([15, 47, 16.1, 48.1], BBox(multi.BBox!));
    }

    #endregion

    #region Inference and conversion

    [Fact]
    public void Infer_GeometryColumn_IsGeoField()
    {
        var sample = new List<IDictionary<string, object>>
        {
            new Dictionary<string, object> { ["name"] = "Graz", ["geom"] = "SRID=31256;POINT (-101323 207623)" },
            new Dictionary<string, object> { ["name"] = "x", ["geom"] = null },
        };

        var mapping = MappingInference.Infer(sample);

        Assert.DoesNotContain(mapping.Fields, f => f.Column == "geom");
        var geo = Assert.Single(mapping.GeoFields);
        Assert.Equal(("geom", "geom", "geom_bbox", 31256), (geo.GeometryColumn, geo.FieldName, geo.BBoxFieldName, geo.Epsg));
        Assert.False(geo.StoreGeoJson);
    }

    [Fact]
    public void Infer_XY_RequiresEpsg()
    {
        var mapping = MappingInference.Infer([new Dictionary<string, object> { ["name"] = "a", ["Rechtswert"] = "-101323", ["Hochwert"] = "207623" }]);

        var geo = Assert.Single(mapping.GeoFields);
        Assert.Equal(("Rechtswert", "Hochwert", 0), (geo.XColumn, geo.YColumn, geo.Epsg));
    }

    [Fact]
    public void ToIndexMapping_GeometryField_AddsBBox_AndOptionalGeoJson()
    {
        var inferred = new InferredMapping
        {
            Fields = [new InferredField { Column = "name", Name = "name", Primary = true }],
            GeoFields = [GeoFieldMapping.ForGeometry("geom", "geom", 4326), GeoFieldMapping.ForColumns("x", "y", "location", 31256)]
        };

        var mapping = MappingInference.ToIndexMapping(inferred);
        Assert.Equal(["name", "geom", "geom_bbox", "location"], mapping.Fields.Select(f => f.Name));
        Assert.Equal(FieldTypes.GeoType, mapping.GetField("geom").FieldType);
        Assert.False(mapping.GetField("geom_bbox").Index);

        inferred.GeoFields[0].StoreGeoJson = true;
        Assert.Contains("geom_geojson", MappingInference.ToIndexMapping(inferred).Fields.Select(f => f.Name));
    }

    [Fact]
    public void TryConvert_GeometryColumn()
    {
        var mapping = new InferredMapping { GeoFields = [GeoFieldMapping.ForGeometry("geom", "geom", 4326)] };
        mapping.GeoFields[0].StoreGeoJson = true;

        Assert.True(ImportService.TryConvert(new Dictionary<string, object> { ["geom"] = GrazPolygonWgs84 }, mapping, out var document, out _));
        Assert.True(document.ContainsKey("geom"));
        Assert.Equal("15.4,47.05,15.48,47.09", document["geom_bbox"]);
        Assert.Contains("Polygon", (string)document["geom_geojson"]);

        // empty geometry value => no geo field, no error
        Assert.True(ImportService.TryConvert(new Dictionary<string, object> { ["geom"] = null }, mapping, out document, out _));
        Assert.Empty(document);

        // invalid geometry => row error
        Assert.False(ImportService.TryConvert(new Dictionary<string, object> { ["geom"] = "POLYGON((broken" }, mapping, out _, out var error));
        Assert.Contains("geom", error);
    }

    [Fact]
    public void TryConvert_ProjectedXY()
    {
        var mapping = new InferredMapping { GeoFields = [GeoFieldMapping.ForColumns("x", "y", "location", 31256)] };

        Assert.True(ImportService.TryConvert(new Dictionary<string, object> { ["x"] = "-101323.59", ["y"] = 207623.96 }, mapping, out var document, out _));
        var parts = ((string)document["location"]).Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(15, parts[0], 4);
        Assert.Equal(47, parts[1], 4);
    }

    [Fact]
    public async Task GeoJsonFeatureCollection_IsFlattened()
    {
        var json = """
            { "type": "FeatureCollection", "bbox": [15, 47, 16, 48],
              "features": [
                { "type": "Feature", "id": 7, "properties": { "name": "Graz", "pop": 300000 }, "geometry": { "type": "Point", "coordinates": [15.43, 47.07] } },
                { "type": "Feature", "properties": { "name": "none" }, "geometry": null }
              ] }
            """;

        var rows = new List<IDictionary<string, object>>();
        await foreach (var row in DocumentReaders.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), SourceFormat.Json))
        {
            rows.Add(row);
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal("Graz", rows[0]["name"]);
        Assert.Equal(7L, rows[0]["id"]);
        Assert.Contains("\"Point\"", (string)rows[0]["geometry"]);
        Assert.Null(rows[1]["geometry"]);

        var mapping = MappingInference.Infer(rows);
        Assert.Equal("geometry", Assert.Single(mapping.GeoFields).GeometryColumn);
    }

    #endregion
}
