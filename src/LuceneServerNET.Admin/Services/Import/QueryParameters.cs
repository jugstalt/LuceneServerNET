using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Admin.Services.Import;

/// <summary>
/// Named parameters of a source query ("... WHERE OID = @oid"). Values are passed as database parameters,
/// never inserted into the SQL text.
/// </summary>
public static partial class QueryParameters
{
    // string literals and comments are removed before searching for @name ('@x' in a string is no parameter; @@ROWCOUNT is no parameter)
    [GeneratedRegex(@"'(?:[^']|'')*'|--[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex LiteralsAndComments();

    [GeneratedRegex(@"(?<![@\w])@([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex Parameter();

    /// <summary>Parameter names of the query (without '@', lower case, distinct).</summary>
    public static IReadOnlyList<string> Names(string? sql)
    {
        if (String.IsNullOrWhiteSpace(sql)) return [];
        var text = LiteralsAndComments().Replace(sql, " ");
        return Parameter().Matches(text).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();
    }

    /// <summary>JSON object {"oid": 4711, "gem": "60101"} => parameter values (number => long / double, string, bool, null).</summary>
    public static Dictionary<string, object?> FromJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new FormatException("Parameters must be a JSON object, e.g. {\"oid\": 4711}");

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name.TrimStart('@')] = property.Value.ValueKind switch
            {
                JsonValueKind.Number when property.Value.TryGetInt64(out var l) => l,
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => throw new FormatException($"Parameter '{property.Name}': only numbers, strings, booleans and null are allowed")
            };
        }
        return result;
    }

    /// <summary>Text input (preview): integer / decimal number if it parses (invariant), else text.</summary>
    public static object? FromText(string? text)
    {
        if (text is null) return null;
        var trimmed = text.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return text;
    }

    /// <summary>Parameters of the query that have no value.</summary>
    public static IEnumerable<string> Missing(string sql, IReadOnlyDictionary<string, object?>? values)
        => Names(sql).Where(n => values is null || !values.Keys.Any(k => k.Equals(n, StringComparison.OrdinalIgnoreCase)));

    internal static void AddTo(DbCommand command, string sql, IReadOnlyDictionary<string, object?>? values)
    {
        var names = Names(sql);
        if (names.Count == 0) return;

        if (Missing(sql, values).ToList() is { Count: > 0 } missing)
        {
            throw new ArgumentException($"Missing query parameters: {String.Join(", ", missing.Select(m => "@" + m))}");
        }

        foreach (var name in names)
        {
            var value = values!.First(v => v.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }

    /// <summary>Stable text of a parameter set (to combine identical triggers).</summary>
    public static string Key(IReadOnlyDictionary<string, object?> values)
        => String.Join("&", values.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                                  .Select(v => $"{v.Key.ToLowerInvariant()}={Convert.ToString(v.Value, CultureInfo.InvariantCulture)}"));
}
