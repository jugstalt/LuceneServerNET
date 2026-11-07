using LuceneServerNET.Core.Models.Mapping;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Admin.Services.Import;

/// <summary>Combines a latitude and a longitude column into one geo field ("lon lat").</summary>
public record GeoCombination(string LatitudeColumn, string LongitudeColumn, string FieldName);

/// <summary>Proposed mapping for a sample of documents: column => field + type.</summary>
public class InferredMapping
{
    public List<InferredField> Fields { get; set; } = [];
    public GeoCombination? Geo { get; set; }
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

            result.Fields.Add(new InferredField()
            {
                Column = column,
                Name = UniqueName(ToFieldName(column), usedNames),
                FieldType = InferType(values!)
            });
        }

        // lat/lon => additional geo field
        var latField = result.Fields.FirstOrDefault(f => LatitudeNames.Contains(f.Column.ToLowerInvariant()) && IsNumeric(f.FieldType));
        var lonField = result.Fields.FirstOrDefault(f => LongitudeNames.Contains(f.Column.ToLowerInvariant()) && IsNumeric(f.FieldType));
        if (latField != null && lonField != null)
        {
            result.Geo = new GeoCombination(latField.Column, lonField.Column, UniqueName("location", usedNames));
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

        if (inferred.Geo != null)
        {
            mapping.Fields.Add(new FieldMapping(inferred.Geo.FieldName, FieldTypes.GeoType) { Index = true, Store = true });
        }

        mapping.PrimaryFields = inferred.Fields.Where(f => f.Primary && !f.Skip).Select(f => f.Name).ToList();
        mapping.PrimaryFieldsPhonetics = phonetics;

        return mapping;
    }
}
