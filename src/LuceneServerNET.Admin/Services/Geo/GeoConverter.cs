using Proj4Net.Core;
using SpatialNET;
using SpatialNET.Imp;
using SpatialNET.IO;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Admin.Services.Geo;

/// <summary>A geometry read from a value, with the SRID found in the value (EWKB, EWKT, GeoJSON crs), if any.</summary>
public record ParsedGeometry(IGeometry2D Geometry, int? Srid);

/// <summary>Result for the index: representative point and (for lines/polygons) bounding box in WGS84.</summary>
public record GeoResult(double Longitude, double Latitude, string? BBox, string? GeoJson)
{
    /// <summary>Value for a "geo" field: "lon lat" (GeoType.Parse format).</summary>
    public string Point => FormattableString.Invariant($"{Math.Round(Longitude, 7)} {Math.Round(Latitude, 7)}");
}

/// <summary>
/// Reads geometries (WKB/EWKB, WKT/EWKT, GeoJSON) and converts them to WGS84 (EPSG:4326) with Proj4Net:
/// point = point on surface (lines/polygons) computed in the source system, bbox = envelope of the densified
/// envelope boundary, optional original geometry as GeoJSON (RFC 7946, 7 decimals).
/// </summary>
public static class GeoConverter
{
    public const int Wgs84 = 4326;

    /// <summary>Common EPSG codes for the UI.</summary>
    public static readonly (int Code, string Name)[] CommonEpsg =
    [
        (4326, "WGS 84 (lon/lat)"),
        (3857, "Web Mercator"),
        (31254, "MGI / Austria GK West"),
        (31255, "MGI / Austria GK Central"),
        (31256, "MGI / Austria GK East"),
        (31287, "MGI / Austria Lambert"),
        (25832, "ETRS89 / UTM 32N"),
        (25833, "ETRS89 / UTM 33N"),
        (2056, "CH1903+ / LV95"),
    ];

    private static readonly ConcurrentDictionary<int, Func<double, double, (double X, double Y)>> Transforms = new();
    private static readonly object ProjLock = new();

    static GeoConverter()
    {
        GeometryFactory.Default ??= ImpGeometryFactory.Instance;
    }

    private static IGeometryFactory Factory => ImpGeometryFactory.Instance;

    #region Parse

    private static readonly Regex WktStart = new(@"^\s*(SRID\s*=\s*(?<srid>\d+)\s*;)?\s*(?<wkt>(POINT|LINESTRING|POLYGON|MULTIPOINT|MULTILINESTRING|MULTIPOLYGON|GEOMETRYCOLLECTION)\b.*)$",
                                                 RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>True if the value looks like a geometry (WKB bytes, WKT/EWKT or a GeoJSON geometry object).</summary>
    public static bool LooksLikeGeometry(object? value) => value switch
    {
        byte[] bytes => bytes.Length >= 5 && bytes[0] is 0 or 1,
        string s => WktStart.IsMatch(s) || IsGeoJsonGeometry(s),
        _ => false
    };

    private static bool IsGeoJsonGeometry(string s)
    {
        var t = s.TrimStart();
        return t.StartsWith('{') && t.Contains("\"coordinates\"", StringComparison.Ordinal) && t.Contains("\"type\"", StringComparison.Ordinal);
    }

    public static ParsedGeometry Parse(object value)
    {
        switch (value)
        {
            case byte[] wkb:
                return new ParsedGeometry(Wkb.Read(wkb, Factory), ReadEwkbSrid(wkb));
            case string text:
                var match = WktStart.Match(text);
                if (match.Success)
                {
                    int? srid = match.Groups["srid"].Success ? int.Parse(match.Groups["srid"].Value, CultureInfo.InvariantCulture) : null;
                    return new ParsedGeometry(Wkt.Read(match.Groups["wkt"].Value, Factory), srid);
                }
                if (IsGeoJsonGeometry(text))
                {
                    return new ParsedGeometry(GeoJson.Read(text, Factory), ReadGeoJsonCrs(text));
                }
                throw new FormatException("Value is not a WKB, WKT or GeoJSON geometry");
            default:
                throw new FormatException($"Value of type {value?.GetType().Name ?? "null"} is not a geometry");
        }
    }

    /// <summary>SRID of an EWKB header (PostGIS ST_AsEWKB); null for ISO WKB.</summary>
    internal static int? ReadEwkbSrid(byte[] wkb)
    {
        if (wkb.Length < 9) return null;

        var littleEndian = wkb[0] == 1;
        var type = littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(wkb.AsSpan(1)) : BinaryPrimitives.ReadUInt32BigEndian(wkb.AsSpan(1));
        const uint SridFlag = 0x20000000;
        if ((type & SridFlag) == 0) return null;

        var srid = littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(wkb.AsSpan(5)) : BinaryPrimitives.ReadInt32BigEndian(wkb.AsSpan(5));
        return srid > 0 ? srid : null;
    }

    /// <summary>Legacy GeoJSON "crs": { "properties": { "name": "EPSG:31256" | "urn:ogc:def:crs:EPSG::31256" } }.</summary>
    internal static int? ReadGeoJsonCrs(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("crs", out var crs) &&
                crs.TryGetProperty("properties", out var properties) &&
                properties.TryGetProperty("name", out var name))
            {
                var m = Regex.Match(name.GetString() ?? "", @"EPSG:+(\d+)", RegexOptions.IgnoreCase);
                if (m.Success) return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }
        catch (JsonException) { }
        return null;
    }

    #endregion

    #region Transform

    /// <summary>Throws if the EPSG code is unknown to Proj4Net.</summary>
    public static void ValidateEpsg(int epsg) => GetTransform(epsg);

    private static Func<double, double, (double X, double Y)> GetTransform(int epsg)
    {
        if (epsg <= 0) throw new ArgumentException("EPSG code is required");
        if (epsg == Wgs84) return (x, y) => (x, y);

        return Transforms.GetOrAdd(epsg, code =>
        {
            lock (ProjLock)
            {
                var crsFactory = new CoordinateReferenceSystemFactory();
                CoordinateReferenceSystem source;
                try
                {
                    source = crsFactory.CreateFromName($"EPSG:{code}");
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Unknown EPSG code {code}", ex);
                }
                var target = crsFactory.CreateFromName($"EPSG:{Wgs84}");
                var transform = new CoordinateTransformFactory().CreateTransform(source, target);

                return (x, y) =>
                {
                    lock (transform)
                    {
                        var result = transform.Transform(new Proj4Net.Core.Coordinate(x, y));
                        return (result.X, result.Y);
                    }
                };
            }
        });
    }

    public static (double Longitude, double Latitude) ToWgs84(double x, double y, int epsg)
    {
        var (lon, lat) = GetTransform(epsg)(x, y);
        if (double.IsNaN(lon) || double.IsNaN(lat) || Math.Abs(lon) > 180 || Math.Abs(lat) > 90)
        {
            throw new ArgumentException(FormattableString.Invariant($"Coordinate {x} {y} (EPSG:{epsg}) is outside of WGS84 ({lon} {lat})"));
        }
        return (lon, lat);
    }

    #endregion

    #region Convert

    /// <summary>Point from two coordinate columns.</summary>
    public static GeoResult FromXY(double x, double y, int epsg)
    {
        var (lon, lat) = ToWgs84(x, y, epsg);
        return new GeoResult(lon, lat, null, null);
    }

    /// <summary>Geometry value: SRID of the value wins over <paramref name="defaultEpsg"/>.</summary>
    public static GeoResult FromGeometry(object value, int defaultEpsg, bool includeGeoJson)
    {
        var parsed = Parse(value);
        var epsg = parsed.Srid ?? defaultEpsg;
        var geometry = parsed.Geometry;

        // point on surface / envelope in the source system (correct geometry), then transformed
        var point = geometry switch
        {
            IPoint2D p => new SpatialNET.Coordinate(p.X, p.Y),
            _ => geometry.PointOnSurface() is { } onSurface
                    ? ToCoordinate(onSurface)
                    : throw new FormatException("Empty geometry")
        };
        var (lon, lat) = ToWgs84(point.X, point.Y, epsg);

        string? bbox = null;
        if (geometry is not IPoint2D)
        {
            var envelope = geometry.Envelope();
            bbox = EnvelopeToWgs84(envelope, epsg);
        }

        var geoJson = includeGeoJson ? ToWgs84GeoJson(geometry, epsg) : null;
        return new GeoResult(lon, lat, bbox, geoJson);
    }

    private static SpatialNET.Coordinate ToCoordinate(object pointOnSurface) => pointOnSurface switch
    {
        SpatialNET.Coordinate c => c,
        IPoint2D p => new SpatialNET.Coordinate(p.X, p.Y),
        _ => throw new InvalidOperationException("No point on surface")
    };

    /// <summary>Transforms the envelope boundary (densified, 16 points per side) and returns "minx,miny,maxx,maxy" in WGS84.</summary>
    internal static string EnvelopeToWgs84(Envelope2D envelope, int epsg)
    {
        const int steps = 16;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        void Add(double x, double y)
        {
            var (lon, lat) = ToWgs84(x, y, epsg);
            minX = Math.Min(minX, lon); minY = Math.Min(minY, lat);
            maxX = Math.Max(maxX, lon); maxY = Math.Max(maxY, lat);
        }

        for (int i = 0; i <= steps; i++)
        {
            var fx = envelope.MinX + envelope.Width * i / steps;
            var fy = envelope.MinY + envelope.Height * i / steps;
            Add(fx, envelope.MinY); Add(fx, envelope.MaxY);
            Add(envelope.MinX, fy); Add(envelope.MaxX, fy);
        }

        return String.Join(",", new[] { minX, minY, maxX, maxY }.Select(v => Math.Round(v, 7).ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>GeoJSON geometry in WGS84 with 7 decimals (RFC 7946).</summary>
    internal static string ToWgs84GeoJson(IGeometry2D geometry, int epsg)
    {
        var node = JsonNode.Parse(geometry.ToGeoJson())!;
        TransformCoordinates(node["coordinates"], epsg);
        if (node is JsonObject obj)
        {
            obj.Remove("crs");
        }
        return node.ToJsonString();
    }

    private static void TransformCoordinates(JsonNode? node, int epsg)
    {
        if (node is not JsonArray array || array.Count == 0) return;

        if (array[0] is JsonValue)
        {
            // position [x, y, (z)]
            var (lon, lat) = ToWgs84(array[0]!.GetValue<double>(), array[1]!.GetValue<double>(), epsg);
            array[0] = Math.Round(lon, 7);
            array[1] = Math.Round(lat, 7);
            return;
        }

        foreach (var child in array)
        {
            TransformCoordinates(child, epsg);
        }
    }

    #endregion
}
