using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LuceneServerNET.Admin.Services.Import;

public enum SourceFormat { Csv, Json }

/// <summary>
/// Reads documents (column => value) from an uploaded file. Values are strings (CSV)
/// or primitive CLR values (JSON: string, long, double, bool, null; nested objects as JSON text).
/// </summary>
public static class DocumentReaders
{
    public static SourceFormat DetectFormat(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".json" or ".ndjson" or ".jsonl" => SourceFormat.Json,
            _ => SourceFormat.Csv
        };

    public static IAsyncEnumerable<IDictionary<string, object?>> ReadAsync(Stream stream, SourceFormat format, CancellationToken cancellationToken = default)
        => format == SourceFormat.Json ? ReadJsonAsync(stream, cancellationToken) : ReadCsvAsync(stream, cancellationToken);

    #region CSV

    public static async IAsyncEnumerable<IDictionary<string, object?>> ReadCsvAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            DetectDelimiterValues = [",", ";", "\t", "|"],
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null
        };

        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            yield break;
        }

        var headers = csv.HeaderRecord ?? [];

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < headers.Length; i++)
            {
                var value = csv.GetField(i);
                row[headers[i]] = String.IsNullOrEmpty(value) ? null : value;
            }
            yield return row;
        }
    }

    #endregion

    #region JSON

    /// <summary>Reads a JSON array of objects, a single object with an array property, or NDJSON (one object per line).</summary>
    public static async IAsyncEnumerable<IDictionary<string, object?>> ReadJsonAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        // try a complete JSON document first
        JsonDocument? document = null;
        try
        {
            document = await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            document = null;
        }

        if (document != null)
        {
            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    // { "items": [...] } => first array property
                    var array = root.EnumerateObject().Select(p => p.Value).FirstOrDefault(v => v.ValueKind == JsonValueKind.Array);
                    root = array.ValueKind == JsonValueKind.Array ? array : root;
                }

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (item.ValueKind == JsonValueKind.Object) yield return ToRow(item);
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    yield return ToRow(root);
                }
            }
            yield break;
        }

        // NDJSON
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (String.IsNullOrWhiteSpace(line)) continue;
            using var lineDocument = JsonDocument.Parse(line);
            if (lineDocument.RootElement.ValueKind == JsonValueKind.Object)
            {
                yield return ToRow(lineDocument.RootElement);
            }
        }
    }

    private static IDictionary<string, object?> ToRow(JsonElement element)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            row[property.Name] = ToValue(property.Value);
        }
        return row;
    }

    internal static object? ToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var l) ? (object)l : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText()
    };

    #endregion
}
