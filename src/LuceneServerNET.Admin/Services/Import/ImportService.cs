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

                if (TryConvert(row, mapping, out var document, out var error))
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
    internal static bool TryConvert(IDictionary<string, object?> row, InferredMapping mapping, out IDictionary<string, object>? document, out string? error)
    {
        document = new Dictionary<string, object>(StringComparer.Ordinal);
        error = null;

        foreach (var field in mapping.Fields.Where(f => !f.Skip))
        {
            if (!row.TryGetValue(field.Column, out var raw) || raw is null || raw is string s && String.IsNullOrWhiteSpace(s))
            {
                continue;
            }

            if (!TryConvertValue(raw, field.FieldType, out var value))
            {
                error = $"Column '{field.Column}': value '{raw}' is not a valid {field.FieldType}";
                document = null;
                return false;
            }

            document[field.Name] = value!;
        }

        if (mapping.Geo != null &&
            row.TryGetValue(mapping.Geo.LatitudeColumn, out var latRaw) && latRaw != null &&
            row.TryGetValue(mapping.Geo.LongitudeColumn, out var lonRaw) && lonRaw != null)
        {
            if (TryConvertValue(latRaw, FieldTypes.DoubleType, out var lat) && TryConvertValue(lonRaw, FieldTypes.DoubleType, out var lon))
            {
                // GeoType.Parse format: "lon lat"
                document[mapping.Geo.FieldName] = FormattableString.Invariant($"{(double)lon!} {(double)lat!}");
            }
            else
            {
                error = $"Invalid coordinates '{latRaw}' / '{lonRaw}'";
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
    public static InferredMapping FromExistingMapping(IndexMapping mapping, IEnumerable<string> columns)
    {
        var result = new InferredMapping();
        foreach (var column in columns)
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
