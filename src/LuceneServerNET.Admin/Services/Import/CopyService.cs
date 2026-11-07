using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Mapping;
using System.Diagnostics;

namespace LuceneServerNET.Admin.Services.Import;

public class CopyOptions
{
    public int BatchSize { get; set; } = ImportOptions.DefaultBatchSize;

    /// <summary>Copy the custom metadata of the index.</summary>
    public bool CopyMetadata { get; set; } = true;

    /// <summary>If the target exists: replace its mapping and documents. Otherwise an existing target is an error.</summary>
    public bool ReplaceExisting { get; set; }
}

/// <summary>
/// Copies an index (mapping, metadata, documents) to another index on the same or another instance.
/// Documents are read with the export endpoint, so only stored fields are copied.
/// </summary>
public class CopyService
{
    /// <summary>Fields that are not stored and therefore get lost by a copy.</summary>
    public static IReadOnlyList<string> NotStoredFields(IndexMapping mapping)
        => mapping.Fields.Where(f => !f.Store && f.FieldType != FieldTypes.GuidType).Select(f => f.Name).ToList();

    public async Task<ImportReport> CopyAsync(LuceneServerV2Client source, string sourceIndex,
                                              LuceneServerV2Client target, string targetIndex,
                                              CopyOptions options,
                                              IProgress<ImportProgress>? progress = null,
                                              Action<long?>? onTotal = null,
                                              CancellationToken cancellationToken = default)
    {
        var report = new ImportReport();
        var stopwatch = Stopwatch.StartNew();
        var batchSize = Math.Clamp(options.BatchSize, 1, 10_000);
        int batches = 0;

        try
        {
            if (source.ServerUrl == target.ServerUrl && sourceIndex == targetIndex)
            {
                throw new ArgumentException("Source and target are the same index");
            }

            var mapping = await source.GetMappingAsync(sourceIndex, cancellationToken)
                          ?? throw new InvalidOperationException($"Index {sourceIndex} has no mapping");

            if (await target.IndexExistsAsync(targetIndex, cancellationToken))
            {
                if (!options.ReplaceExisting)
                {
                    throw new InvalidOperationException($"Target index {targetIndex} already exists");
                }
                await target.RemoveDocumentsAsync(targetIndex, "*:*", cancellationToken: cancellationToken);
            }
            else
            {
                await target.CreateIndexAsync(targetIndex, cancellationToken);
            }
            await target.PutMappingAsync(targetIndex, mapping, cancellationToken);

            if (options.CopyMetadata)
            {
                foreach (var (name, value) in await source.GetMetadataAsync(sourceIndex, cancellationToken))
                {
                    await target.PutMetadataAsync(targetIndex, name, value, cancellationToken);
                }
            }

            var batch = new List<IDictionary<string, object>>(batchSize);
            await foreach (var document in source.ExportDocumentsAsync(sourceIndex, total => onTotal?.Invoke(total), cancellationToken))
            {
                report.Read++;
                batch.Add(document);

                if (batch.Count >= batchSize)
                {
                    await target.AddDocumentsAsync(targetIndex, batch, cancellationToken);
                    report.Imported += batch.Count;
                    batches++;
                    batch = new List<IDictionary<string, object>>(batchSize);
                    progress?.Report(new ImportProgress(report.Read, report.Imported, 0, batches));
                }
            }

            if (batch.Count > 0)
            {
                await target.AddDocumentsAsync(targetIndex, batch, cancellationToken);
                report.Imported += batch.Count;
                batches++;
            }
        }
        catch (OperationCanceledException)
        {
            report.Cancelled = true;
        }
        catch (Exception ex)
        {
            report.FatalError = ex.Message;
        }
        finally
        {
            if (report.Imported > 0)
            {
                try
                {
                    await target.RefreshIndexAsync(targetIndex, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    report.FatalError ??= $"Refresh failed: {ex.Message}";
                }
            }
            report.Duration = stopwatch.Elapsed;
            progress?.Report(new ImportProgress(report.Read, report.Imported, 0, batches));
        }

        return report;
    }
}
