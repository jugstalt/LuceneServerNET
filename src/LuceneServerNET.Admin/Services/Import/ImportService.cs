using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Mapping;
using System.Diagnostics;
using System.Globalization;

namespace LuceneServerNET.Admin.Services.Import;

public class ImportOptions
{
    public const int DefaultBatchSize = 500;

    /// <summary>Documents per request (1 - 10000).</summary>
    public int BatchSize { get; set; } = DefaultBatchSize;

    /// <summary>Refresh the index once at the end (makes the documents visible immediately).</summary>
    public bool RefreshAtEnd { get; set; } = true;

    /// <summary>Remove all documents before the import.</summary>
    public bool ClearIndexFirst { get; set; }

    /// <summary>Import stops when more rows failed (0 = never stop).</summary>
    public int MaxErrors { get; set; } = 1000;

    /// <summary>Template variables ("@metaid", "@category").</summary>
    public IReadOnlyDictionary<string, string>? Variables { get; set; }
}

public record ImportProgress(long Read, long Imported, long Failed, int Batches);

public record ImportError(long Row, string Message);

public class ImportReport
{
    public long Read { get; set; }
    public long Imported { get; set; }
    public List<ImportError> Errors { get; } = [];
    public bool Cancelled { get; set; }
    public bool StoppedBecauseOfErrors { get; set; }
    public string? FatalError { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Imports documents in batches. Rows that can not be converted to the mapping are skipped and reported.
/// If the server rejects a batch, its documents are sent one by one to isolate the failing rows.
/// </summary>
public class ImportService
{
    private readonly ILogger<ImportService> _logger;

    public ImportService(ILogger<ImportService> logger)
    {
        _logger = logger;
    }

    public async Task<ImportReport> ImportAsync(LuceneServerV2Client client,
                                                string index,
                                                IAsyncEnumerable<IDictionary<string, object?>> rows,
                                                InferredMapping mapping,
                                                ImportOptions options,
                                                IProgress<ImportProgress>? progress = null,
                                                CancellationToken cancellationToken = default)
    {
        var batchSize = Math.Clamp(options.BatchSize, 1, 10_000);
        var report = new ImportReport();
        var stopwatch = Stopwatch.StartNew();
        var batch = new List<(long Row, IDictionary<string, object> Document)>(batchSize);
        int batches = 0;

        void Report() => progress?.Report(new ImportProgress(report.Read, report.Imported, report.Errors.Count, batches));

        try
        {
            if (options.ClearIndexFirst)
            {
                await client.RemoveDocumentsAsync(index, "*:*", cancellationToken: cancellationToken);
            }

            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rowNumber = ++report.Read;

                if (TryConvert(row, mapping, out var document, out var error, options.Variables))
                {
                    batch.Add((rowNumber, document!));
                }
                else
                {
                    report.Errors.Add(new ImportError(rowNumber, error!));
                }

                if (batch.Count >= batchSize)
                {
                    await SendBatchAsync(client, index, batch, report, cancellationToken);
                    batches++;
                    batch.Clear();
                    Report();
                }

                if (options.MaxErrors > 0 && report.Errors.Count >= options.MaxErrors)
                {
                    report.StoppedBecauseOfErrors = true;
                    break;
                }
            }

            if (batch.Count > 0 && !report.StoppedBecauseOfErrors)
            {
                await SendBatchAsync(client, index, batch, report, cancellationToken);
                batches++;
            }
        }
        catch (OperationCanceledException)
        {
            report.Cancelled = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import into {Index} failed", index);
            report.FatalError = ex.Message;
        }
        finally
        {
            if (options.RefreshAtEnd && report.Imported > 0)
            {
                try
                {
                    await client.RefreshIndexAsync(index, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    report.FatalError ??= $"Refresh failed: {ex.Message}";
                }
            }

            report.Duration = stopwatch.Elapsed;
            Report();
        }

        return report;
    }

    private static async Task SendBatchAsync(LuceneServerV2Client client,
                                             string index,
                                             List<(long Row, IDictionary<string, object> Document)> batch,
                                             ImportReport report,
                                             CancellationToken cancellationToken)
    {
        try
        {
            await client.AddDocumentsAsync(index, batch.Select(b => b.Document).ToList(), cancellationToken);
            report.Imported += batch.Count;
        }
        catch (Exception) when (batch.Count > 1 && !cancellationToken.IsCancellationRequested)
        {
            // isolate the failing documents
            foreach (var (row, document) in batch)
            {
                try
                {
                    await client.AddDocumentsAsync(index, [document], cancellationToken);
                    report.Imported++;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    report.Errors.Add(new ImportError(row, ex.Message));
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            report.Errors.Add(new ImportError(batch[0].Row, ex.Message));
        }
    }

    /// <summary>Renames the columns to field names and converts the values to the field types.</summary>
    internal static bool TryConvert(IDictionary<string, object?> row, InferredMapping mapping, out IDictionary<string, object>? document, out string? error,
                                    IReadOnlyDictionary<string, string>? variables = null)
    {
        document = new Dictionary<string, object>(StringComparer.Ordinal);
        error = null;

        foreach (var field in mapping.Fields.Where(f => !f.Skip))
        {
            object? raw;
            if (field.IsTemplate)
            {
                raw = FieldTemplate.Render(field.Template!, row, variables, mapping.Replacements);
            }
            else if (!row.TryGetValue(field.Column, out raw))
            {
                continue;
            }

            if (raw is null || raw is string s && String.IsNullOrWhiteSpace(s))
            {
                continue;
            }

            if (!TryConvertValue(raw, field.FieldType, out var value))
            {
                error = $"{(field.IsTemplate ? $"Template of '{field.Name}'" : $"Column '{field.Column}'")}: value '{raw}' is not a valid {field.FieldType}";
                document = null;
                return false;
            }

            document[field.Name] = value!;
        }

        foreach (var geo in mapping.GeoFields)
        {
            try
            {
                Geo.GeoResult? result = null;

                if (geo.IsGeometry)
                {
                    if (row.TryGetValue(geo.GeometryColumn!, out var geometryRaw) && geometryRaw is not null && !(geometryRaw is string g && String.IsNullOrWhiteSpace(g)))
                    {
                        result = Geo.GeoConverter.FromGeometry(geometryRaw, geo.Epsg, geo.StoreGeoJson);
                    }
                }
                else if (row.TryGetValue(geo.XColumn!, out var xRaw) && xRaw != null && !(xRaw is string xs && String.IsNullOrWhiteSpace(xs)) &&
                         row.TryGetValue(geo.YColumn!, out var yRaw) && yRaw != null && !(yRaw is string ys && String.IsNullOrWhiteSpace(ys)))
                {
                    if (!TryConvertValue(xRaw, FieldTypes.DoubleType, out var x) || !TryConvertValue(yRaw, FieldTypes.DoubleType, out var y))
                    {
                        throw new FormatException($"Invalid coordinates '{xRaw}' / '{yRaw}'");
                    }
                    result = Geo.GeoConverter.FromXY((double)x!, (double)y!, geo.Epsg);
                }

                if (result is null)
                {
                    continue;
                }

                // GeoType.Parse format: "lon lat"
                document[geo.FieldName] = result.Point;
                if (result.BBox != null) document[geo.BBoxFieldName] = result.BBox;
                if (result.GeoJson != null) document[geo.GeoJsonFieldName] = result.GeoJson;
            }
            catch (Exception ex)
            {
                error = $"Geo field '{geo.FieldName}' ({geo.SourceDescription}): {ex.Message}";
                document = null;
                return false;
            }
        }

        return true;
    }

    internal static bool TryConvertValue(object raw, string fieldType, out object? value)
    {
        var text = raw is string str ? str.Trim() : Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
        value = null;

        switch (fieldType)
        {
            case FieldTypes.Int32Type:
                if (raw is int or long or short && Convert.ToInt64(raw) is >= int.MinValue and <= int.MaxValue) { value = Convert.ToInt32(raw); return true; }
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) { value = i; return true; }
                return false;
            case FieldTypes.DoubleType:
            case FieldTypes.SingleType:
                if (raw is double or float or int or long) { value = Convert.ToDouble(raw); return true; }
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { value = d; return true; }
                return false;
            case FieldTypes.DateTimeType:
                if (raw is DateTime dt) { value = dt; return true; }
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) { value = parsed; return true; }
                return false;
            default:
                value = text;
                return true;
        }
    }

    /// <summary>Assigns the columns of a file to the fields of an existing mapping (by name, case insensitive).</summary>
    public static InferredMapping FromExistingMapping(IndexMapping mapping, IEnumerable<string> columns, IReadOnlyList<IDictionary<string, object?>>? sample = null)
    {
        var result = new InferredMapping();

        // geo sources of the file/query => geo fields of the existing mapping
        var geoFields = mapping.Fields.Where(f => f.FieldType == FieldTypes.GeoType).ToList();
        var geometryColumns = new HashSet<string>(StringComparer.Ordinal);
        if (sample is { Count: > 0 } && geoFields.Count > 0)
        {
            foreach (var geo in MappingInference.Infer(sample).GeoFields)
            {
                var target = geoFields.FirstOrDefault(f => f.Name.Equals(geo.FieldName, StringComparison.OrdinalIgnoreCase))
                             ?? (geoFields.Count == 1 && result.GeoFields.Count == 0 ? geoFields[0] : null);
                if (target == null) continue;

                geo.FieldName = target.Name;
                geo.BBoxFieldName = $"{target.Name}_bbox";
                geo.GeoJsonFieldName = $"{target.Name}_geojson";
                geo.StoreGeoJson = mapping.GetField(geo.GeoJsonFieldName) != null;
                result.GeoFields.Add(geo);
                if (geo.GeometryColumn != null) geometryColumns.Add(geo.GeometryColumn);
            }
        }

        foreach (var column in columns.Where(c => !geometryColumns.Contains(c)))
        {
            var field = mapping.Fields.FirstOrDefault(f => f.Name.Equals(column, StringComparison.OrdinalIgnoreCase)
                                                         || f.Name.Equals(MappingInference.ToFieldName(column), StringComparison.OrdinalIgnoreCase));
            result.Fields.Add(new InferredField()
            {
                Column = column,
                Name = field?.Name ?? MappingInference.ToFieldName(column),
                FieldType = field?.FieldType ?? FieldTypes.TextType,
                Index = field?.Index ?? true,
                Store = field?.Store ?? true,
                Primary = field != null && (mapping.PrimaryFields?.Contains(field.Name) ?? false),
                Skip = field == null
            });
        }
        return result;
    }
}
