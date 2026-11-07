using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Mapping;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace LuceneServerNET.Admin.Services.Import;

public class CopyOptions
{
    public int BatchSize { get; set; } = ImportOptions.DefaultBatchSize;

    /// <summary>Copy the custom metadata of the index.</summary>
    public bool CopyMetadata { get; set; } = true;

    /// <summary>If the target exists: replace its mapping and documents. Otherwise an existing target is an error.</summary>
    public bool ReplaceExisting { get; set; }
}

/// <summary>manifest.json of an index backup (ZIP).</summary>
public class BackupManifest
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;
    public string Index { get; set; } = "";
    public string? Instance { get; set; }
    public string? ServerVersion { get; set; }
    public DateTime CreatedUtc { get; set; }
    public long DocumentCount { get; set; }

    /// <summary>Fields that are not stored and therefore not in documents.ndjson.</summary>
    public List<string> NotStoredFields { get; set; } = [];
}

/// <summary>
/// Copies an index (mapping, metadata, documents) to another index on the same or another instance,
/// writes index backups (ZIP: manifest.json, mapping.json, metadata.json, documents.ndjson) and restores them.
/// Documents are read with the export endpoint, so only stored fields are copied / backed up.
/// </summary>
public class CopyService
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Fields that are not stored and therefore get lost by a copy.</summary>
    public static IReadOnlyList<string> NotStoredFields(IndexMapping mapping)
        => mapping.Fields.Where(f => !f.Store && f.FieldType != FieldTypes.GuidType).Select(f => f.Name).ToList();

    #region Copy

    public async Task<ImportReport> CopyAsync(LuceneServerV2Client source, string sourceIndex,
                                              LuceneServerV2Client target, string targetIndex,
                                              CopyOptions options,
                                              IProgress<ImportProgress>? progress = null,
                                              Action<long?>? onTotal = null,
                                              CancellationToken cancellationToken = default)
    {
        if (source.ServerUrl == target.ServerUrl && sourceIndex == targetIndex)
        {
            return new ImportReport { FatalError = "Source and target are the same index" };
        }

        try
        {
            var mapping = await source.GetMappingAsync(sourceIndex, cancellationToken)
                          ?? throw new InvalidOperationException($"Index {sourceIndex} has no mapping");
            var metadata = options.CopyMetadata ? await source.GetMetadataAsync(sourceIndex, cancellationToken) : null;

            return await WriteIndexAsync(target, targetIndex, mapping, metadata,
                                         source.ExportDocumentsAsync(sourceIndex, total => onTotal?.Invoke(total), cancellationToken),
                                         options, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new ImportReport { Cancelled = true };
        }
        catch (Exception ex)
        {
            return new ImportReport { FatalError = ex.Message };
        }
    }

    /// <summary>Creates/replaces the target index and writes mapping, metadata and documents in batches.</summary>
    public async Task<ImportReport> WriteIndexAsync(LuceneServerV2Client target, string targetIndex,
                                                    IndexMapping mapping, IDictionary<string, string>? metadata,
                                                    IAsyncEnumerable<IDictionary<string, object>> documents,
                                                    CopyOptions options,
                                                    IProgress<ImportProgress>? progress = null,
                                                    CancellationToken cancellationToken = default)
    {
        var report = new ImportReport();
        var stopwatch = Stopwatch.StartNew();
        var batchSize = Math.Clamp(options.BatchSize, 1, 10_000);
        int batches = 0;

        try
        {
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

            foreach (var (name, value) in metadata ?? new Dictionary<string, string>())
            {
                await target.PutMetadataAsync(targetIndex, name, value, cancellationToken);
            }

            var batch = new List<IDictionary<string, object>>(batchSize);
            await foreach (var document in documents.WithCancellation(cancellationToken))
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

    #endregion

    #region Backup

    /// <summary>Writes a backup ZIP of the index to <paramref name="zipStream"/> (seekable or not).</summary>
    public async Task<BackupManifest> WriteBackupAsync(LuceneServerV2Client source, string index, string? instanceName, Stream zipStream, CancellationToken cancellationToken = default)
    {
        var mapping = await source.GetMappingAsync(index, cancellationToken) ?? throw new InvalidOperationException($"Index {index} has no mapping");
        var metadata = await source.GetMetadataAsync(index, cancellationToken);
        string? version = null;
        try { version = (await source.GetServerInfoAsync(cancellationToken))?.Version; } catch { /* needs admin role */ }

        var manifest = new BackupManifest
        {
            Index = index,
            Instance = instanceName,
            ServerVersion = version,
            CreatedUtc = DateTime.UtcNow,
            NotStoredFields = NotStoredFields(mapping).ToList()
        };

        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteJsonEntryAsync(zip, "mapping.json", mapping);
            await WriteJsonEntryAsync(zip, "metadata.json", metadata);

            var entry = zip.CreateEntry("documents.ndjson", CompressionLevel.Optimal);
            await using (var stream = entry.Open())
            {
                var newLine = "\n"u8.ToArray();
                await foreach (var document in source.ExportDocumentsAsync(index, cancellationToken: cancellationToken))
                {
                    await JsonSerializer.SerializeAsync(stream, document, cancellationToken: cancellationToken);
                    await stream.WriteAsync(newLine, cancellationToken);
                    manifest.DocumentCount++;
                }
            }

            await WriteJsonEntryAsync(zip, "manifest.json", manifest);
        }

        return manifest;
    }

    private static async Task WriteJsonEntryAsync<T>(ZipArchive zip, string name, T value)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, Indented);
    }

    /// <summary>An opened backup ZIP: manifest, mapping, metadata and a reader for the documents.</summary>
    public sealed class Backup : IDisposable
    {
        private readonly ZipArchive _zip;

        internal Backup(ZipArchive zip, BackupManifest manifest, IndexMapping mapping, IDictionary<string, string> metadata)
        {
            _zip = zip;
            Manifest = manifest;
            Mapping = mapping;
            Metadata = metadata;
        }

        public BackupManifest Manifest { get; }
        public IndexMapping Mapping { get; }
        public IDictionary<string, string> Metadata { get; }

        public async IAsyncEnumerable<IDictionary<string, object>> ReadDocumentsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var entry = _zip.GetEntry("documents.ndjson") ?? throw new InvalidDataException("documents.ndjson is missing");
            await using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                yield return JsonSerializer.Deserialize<Dictionary<string, object>>(line)!;
            }
        }

        public void Dispose() => _zip.Dispose();
    }

    /// <summary>Opens and validates a backup ZIP (the stream must be seekable and stay open while the backup is used).</summary>
    public static Backup OpenBackup(Stream zipStream)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("The file is not a ZIP file");
        }

        try
        {
            var manifest = ReadJson<BackupManifest>(zip, "manifest.json");
            if (manifest.Format > BackupManifest.CurrentFormat)
            {
                throw new InvalidDataException($"Backup format {manifest.Format} is newer than this admin application supports");
            }
            var mapping = ReadJson<IndexMapping>(zip, "mapping.json");
            var metadata = zip.GetEntry("metadata.json") is null ? new Dictionary<string, string>() : ReadJson<Dictionary<string, string>>(zip, "metadata.json");
            if (zip.GetEntry("documents.ndjson") is null) throw new InvalidDataException("documents.ndjson is missing");

            return new Backup(zip, manifest, mapping, metadata);
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    private static T ReadJson<T>(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"{name} is missing - not an index backup");
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException($"{name} is empty");
    }

    #endregion
}
