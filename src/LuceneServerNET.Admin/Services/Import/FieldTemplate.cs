using System.Globalization;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Admin.Services.Import;

/// <summary>A text replacement applied to the values of template fields (after the template is filled).</summary>
public class TextReplacement
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

/// <summary>
/// Field templates like "{PLZ} {ORT} {STR} {HNR}": placeholders are column names (case insensitive) or variables
/// ("{@metaid}", "{@category}"). NULL => empty, numbers invariant, dates ISO 8601. The result is cleaned:
/// whitespace collapsed, empty brackets "()" / "[]" removed, trimmed.
/// </summary>
public static partial class FieldTemplate
{
    public const string MetaIdVariable = "@metaid", CategoryVariable = "@category";

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\(\s*\)|\[\s*\]")]
    private static partial Regex EmptyBracketsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>Column names and variables used in the template.</summary>
    public static IEnumerable<string> Placeholders(string? template)
        => String.IsNullOrEmpty(template) ? [] : PlaceholderRegex().Matches(template).Select(m => m.Groups[1].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Placeholders that are neither a column nor a variable (for validation).</summary>
    public static IEnumerable<string> UnknownPlaceholders(string? template, IEnumerable<string> columns, IEnumerable<string>? variables = null)
    {
        var known = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
        known.UnionWith(variables ?? [MetaIdVariable, CategoryVariable]);
        return Placeholders(template).Where(p => !known.Contains(p));
    }

    public static string Render(string template, IDictionary<string, object?> row,
                                IReadOnlyDictionary<string, string>? variables = null,
                                IEnumerable<TextReplacement>? replacements = null)
    {
        var text = PlaceholderRegex().Replace(template, match =>
        {
            var name = match.Groups[1].Value.Trim();
            if (name.StartsWith('@'))
            {
                return variables != null && variables.TryGetValue(name, out var variable) ? variable : "";
            }
            return Format(Lookup(row, name));
        });

        text = Clean(text);
        foreach (var replacement in replacements ?? [])
        {
            if (!String.IsNullOrEmpty(replacement.From))
            {
                text = text.Replace(replacement.From, replacement.To ?? "", StringComparison.Ordinal);
            }
        }
        return text;
    }

    internal static string Clean(string text)
    {
        text = WhitespaceRegex().Replace(text, " ");
        // "Adresse in Graz ( )" => "Adresse in Graz"
        string before;
        do
        {
            before = text;
            text = EmptyBracketsRegex().Replace(text, "");
        } while (text != before);
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    private static object? Lookup(IDictionary<string, object?> row, string column)
    {
        if (row.TryGetValue(column, out var value)) return value;
        foreach (var (key, v) in row)
        {
            if (key.Equals(column, StringComparison.OrdinalIgnoreCase)) return v;
        }
        return null;
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        DateTime dt => dt.ToString(dt.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        byte[] => "",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
