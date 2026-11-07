using LuceneServerNET.Core.Models.Mapping;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Admin.Services.Import;

/// <summary>
/// A geo field of the import: either a point from two coordinate columns (X/Y or lon/lat)
/// or a geometry column (WKB/EWKB, WKT/EWKT, GeoJSON). Values are converted to WGS84;
/// lines/polygons become their point on surface plus a bounding box field.
/// </summary>
public class GeoFieldMapping
{
    /// <summary>Name of the "geo" field (representative point).</summary>
    public string FieldName { get; set; } = "location";

    /// <summary>X / longitude column (point from coordinate columns).</summary>
    public string? XColumn { get; set; }

    /// <summary>Y / latitude column (point from coordinate columns).</summary>
    public string? YColumn { get; set; }

    /// <summary>Geometry column (WKB, WKT, GeoJSON).</summary>
    public string? GeometryColumn { get; set; }

    /// <summary>Coordinate system of the source (EPSG). A SRID in the value (EWKB, EWKT) wins. 0 = not chosen yet.</summary>
    public int Epsg { get; set; } = Geo.GeoConverter.Wgs84;

    /// <summary>Field for the bounding box "minx,miny,maxx,maxy" (WGS84) of lines and polygons.</summary>
    public string BBoxFieldName { get; set; } = "location_bbox";

    /// <summary>Store the original geometry as GeoJSON (WGS84) in <see cref="GeoJsonFieldName"/>.</summary>
    public bool StoreGeoJson { get; set; }

    public string GeoJsonFieldName { get; set; } = "location_geojson";

    public bool IsGeometry => GeometryColumn != null;

    public string SourceDescription => IsGeometry ? GeometryColumn! : $"{XColumn} / {YColumn}";

    public static GeoFieldMapping ForColumns(string xColumn, string yColumn, string fieldName, int epsg)
        => new() { XColumn = xColumn, YColumn = yColumn, FieldName = fieldName, Epsg = epsg, BBoxFieldName = $"{fieldName}_bbox", GeoJsonFieldName = $"{fieldName}_geojson" };

    public static GeoFieldMapping ForGeometry(string column, string fieldName, int epsg)
        => new() { GeometryColumn = column, FieldName = fieldName, Epsg = epsg, BBoxFieldName = $"{fieldName}_bbox", GeoJsonFieldName = $"{fieldName}_geojson" };
}

/// <summary>Proposed mapping for a sample of documents: column => field + type.</summary>
public class InferredMapping
{
    public List<InferredField> Fields { get; set; } = [];
    public List<GeoFieldMapping> GeoFields { get; set; } = [];

    /// <summary>Characters of the primary fields that are encoded by the server (e.g. "./"), null = none.</summary>
    public string? EncodeCharacters { get; set; }
}

public class InferredField
{
    public string Column { get; set; } = "";
    public string Name { get; set; } = "";
    public string FieldType { get; set; } = FieldTypes.TextType;
    public bool Index { get; set; } = true;
    public bool Store { get; set; } = true;
    public bool Primary { get; set; }

    /// <summary>Column is not imported.</summary>
    public bool Skip { get; set; }
}

/// <summary>
/// Proposes field types from sample values: int32, double, datetime, geo (lat/lon columns),
/// string (short, uniform values like codes or ids) or text (everything else).
/// </summary>
public static class MappingInference
{
    private static readonly string[] LatitudeNames = ["lat", "latitude", "y_wgs84", "breite"];
    private static readonly string[] LongitudeNames = ["lon", "lng", "long", "longitude", "x_wgs84", "laenge", "länge"];
    private static readonly string[] XNames = ["x", "rechtswert", "easting", "east", "x_coord", "xcoord"];
    private static readonly string[] YNames = ["y", "hochwert", "northing", "north", "y_coord", "ycoord"];
    private static readonly Regex IsoDateRegex = new(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?)?$", RegexOptions.Compiled);

    public static InferredMapping Infer(IReadOnlyList<IDictionary<string, object?>> sample)
    {
        var columns = sample.SelectMany(r => r.Keys).Distinct(StringComparer.Ordinal).ToList();
        var result = new InferredMapping();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            var values = sample.Select(r => r.TryGetValue(column, out var v) ? v : null)
                               .Where(v => v != null && !(v is string s && String.IsNullOrWhiteSpace(s)))
                               .ToList();

            // geometry column (WKB, WKT, GeoJSON) => geo field, not a normal field
            if (values.Count > 0 && values.All(Geo.GeoConverter.LooksLikeGeometry))
            {
                var name = UniqueName(ToFieldName(column), usedNames);
                usedNames.Add($"{name}_bbox");
                result.GeoFields.Add(GeoFieldMapping.ForGeometry(column, name, SridOf(values) ?? Geo.GeoConverter.Wgs84));
                continue;
            }

            result.Fields.Add(new InferredField()
            {
                Column = column,
                Name = UniqueName(ToFieldName(column), usedNames),
                FieldType = InferType(values!)
            });
        }

        // lat/lon (WGS84) or x/y (projected, EPSG has to be chosen) => additional geo field
        var latField = NumericField(result, LatitudeNames);
        var lonField = NumericField(result, LongitudeNames);
        if (latField != null && lonField != null)
        {
            result.GeoFields.Add(GeoFieldMapping.ForColumns(lonField.Column, latField.Column, UniqueName("location", usedNames), Geo.GeoConverter.Wgs84));
        }
        else
        {
            var xField = NumericField(result, XNames);
            var yField = NumericField(result, YNames);
            if (xField != null && yField != null)
            {
                result.GeoFields.Add(GeoFieldMapping.ForColumns(xField.Column, yField.Column, UniqueName("location", usedNames), 0));
            }
        }

        // primary (default search) fields: all text fields, else the first string field
        var primary = result.Fields.Where(f => f.FieldType == FieldTypes.TextType).ToList();
        if (primary.Count == 0)
        {
            primary = result.Fields.Where(f => f.FieldType == FieldTypes.StringType).Take(1).ToList();
        }
        primary.ForEach(f => f.Primary = true);

        return result;
    }

    internal static string InferType(IReadOnlyList<object> values)
    {
        if (values.Count == 0)
        {
            return FieldTypes.TextType;
        }

        if (values.All(v => v is int || v is long l && l is >= int.MinValue and <= int.MaxValue || v is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return FieldTypes.Int32Type;
        }

        if (values.All(v => v is double || v is float || v is long || v is int || v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            return FieldTypes.DoubleType;
        }

        if (values.All(v => v is DateTime || v is DateTimeOffset))
        {
            return FieldTypes.DateTimeType;
        }

        var strings = values.Select(v => v is bool b ? (b ? "true" : "false") : v.ToString() ?? "").ToList();

        if (strings.All(s => IsoDateRegex.IsMatch(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)))
        {
            return FieldTypes.DateTimeType;
        }

        // short and uniform (codes, ids, categories) => string (not tokenized), else text
        var maxLength = strings.Max(s => s.Length);
        var withWhitespace = strings.Count(s => s.Any(Char.IsWhiteSpace));
        if (maxLength <= 64 && withWhitespace <= strings.Count / 10)
        {
            return FieldTypes.StringType;
        }

        return FieldTypes.TextType;
    }

    private static InferredField? NumericField(InferredMapping mapping, string[] names)
        => mapping.Fields.FirstOrDefault(f => names.Contains(f.Column.ToLowerInvariant()) && IsNumeric(f.FieldType));

    /// <summary>SRID of the first value that has one (EWKB, EWKT, GeoJSON crs).</summary>
    private static int? SridOf(IEnumerable<object?> values)
    {
        foreach (var value in values.Take(20))
        {
            try
            {
                if (Geo.GeoConverter.Parse(value!).Srid is int srid) return srid;
            }
            catch { }
        }
        return null;
    }

    private static bool IsNumeric(string fieldType) => fieldType is FieldTypes.DoubleType or FieldTypes.SingleType or FieldTypes.Int32Type;

    /// <summary>Field names must not be empty or start with '_' (reserved by the server).</summary>
    internal static string ToFieldName(string column)
    {
        var name = Regex.Replace(column.Trim(), @"\s+", "_");
        name = name.TrimStart('_');
        return String.IsNullOrEmpty(name) ? "field" : name;
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        var candidate = name;
        for (int i = 2; !used.Add(candidate); i++)
        {
            candidate = $"{name}{i}";
        }
        return candidate;
    }

    /// <summary>Creates the server mapping from the (edited) proposal.</summary>
    public static IndexMapping ToIndexMapping(InferredMapping inferred, Core.Phonetics.Algorithm phonetics = Core.Phonetics.Algorithm.None)
    {
        var mapping = new IndexMapping() { Fields = new List<FieldMapping>() };

        foreach (var field in inferred.Fields.Where(f => !f.Skip))
        {
            mapping.Fields.Add(new FieldMapping(field.Name, field.FieldType) { Index = field.Index, Store = field.Store });
        }

        foreach (var geo in inferred.GeoFields)
        {
            mapping.Fields.Add(new FieldMapping(geo.FieldName, FieldTypes.GeoType) { Index = true, Store = true });
            if (geo.IsGeometry)
            {
                mapping.Fields.Add(new FieldMapping(geo.BBoxFieldName, FieldTypes.StringType) { Index = false, Store = true });
                if (geo.StoreGeoJson)
                {
                    mapping.Fields.Add(new FieldMapping(geo.GeoJsonFieldName, FieldTypes.StringType) { Index = false, Store = true });
                }
            }
        }

        mapping.PrimaryFields = inferred.Fields.Where(f => f.Primary && !f.Skip).Select(f => f.Name).ToList();
        mapping.PrimaryFieldsPhonetics = phonetics;
        var encode = (inferred.EncodeCharacters ?? "").Where(c => !Char.IsWhiteSpace(c)).Distinct().ToArray();
        mapping.PrimaryFieldsEncodeCharacters = encode.Length > 0 ? encode : null!;

        return mapping;
    }
}
