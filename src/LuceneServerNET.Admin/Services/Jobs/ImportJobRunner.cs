using Cronos;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Admin.Services.Audit;
using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Admin.Services.Instances;
using LuceneServerNET.Admin.Services.Repository;
using LuceneServerNET.Client;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LuceneServerNET.Admin.Services.Jobs;

public static class ImportJobModes
{
    /// <summary>Import into a new index generation {target}_{yyyyMMddHHmmss}, then switch the alias {target} to it.</summary>
    public const string ReplaceAlias = "replace_alias";

    /// <summary>Remove all documents of the index {target}, then import.</summary>
    public const string Clear = "clear";

    /// <summary>
    /// Append or update: add the documents to the index {target}; with a key field, documents with the same key are
    /// replaced. Only this mode accepts triggers with parameters (queued, see JobQueueProcessor).
    /// </summary>
    public const string Append = "append";

    public static readonly string[] All = [ReplaceAlias, Clear, Append];
}

public static class JobTriggerKinds
{
    public const string Params = "params", Delete = "delete";
}

public static class ImportJobStatus
{
    public const string Running = "running", Succeeded = "succeeded", Warning = "warning", Failed = "failed", Cancelled = "cancelled", Interrupted = "interrupted";
}

/// <summary>Sources and index settings of a job (column mapping_json; fixed when the job is saved).</summary>
public class ImportJobMapping
{
    /// <summary>Mapping of jobs saved before sources existed (moved to the first source by <see cref="Normalize"/>).</summary>
    public InferredMapping Mapping { get; set; } = new();

    public Core.Phonetics.Algorithm Phonetics { get; set; }

    /// <summary>Encoded characters of the primary fields (e.g. "./").</summary>
    public string? EncodeCharacters { get; set; }

    /// <summary>Append or update: indexed string field that identifies a document (webgis: "id"); null = append only.</summary>
    public string? KeyField { get; set; }

    /// <summary>A source has query parameters: the job only runs with parameters (trigger).</summary>
    public bool HasParameters => Sources.Any(s => s.Parameters.Count > 0);

    public List<ImportJobSource> Sources { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this);

    public static ImportJobMapping FromJson(string json)
        => String.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<ImportJobMapping>(json) ?? new();

    /// <summary>Loads the job settings; a job of an older version becomes one source.</summary>
    public static ImportJobMapping ForJob(ImportJobRecord job)
    {
        var result = FromJson(job.MappingJson);
        result.Normalize(job);
        return result;
    }

    public void Normalize(ImportJobRecord job)
    {
        if (Sources.Count == 0 && (Mapping.Fields.Count > 0 || Mapping.GeoFields.Count > 0))
        {
            EncodeCharacters ??= Mapping.EncodeCharacters;
            Sources.Add(new ImportJobSource { Name = job.Name, ConnectionId = job.ConnectionId, Sql = job.Sql, Mapping = Mapping });
        }
        Mapping = new();
    }

    /// <summary>
    /// The index mapping of all sources: union of the fields (a field used by several sources must have the same
    /// type, index and store options), primary fields of all sources.
    /// </summary>
    public IndexMapping ToIndexMapping()
    {
        var result = new IndexMapping { Fields = new List<FieldMapping>() };
        var primary = new List<string>();

        foreach (var source in Sources)
        {
            source.Mapping.EncodeCharacters = EncodeCharacters;
            var mapping = MappingInference.ToIndexMapping(source.Mapping, Phonetics);
            foreach (var field in mapping.Fields)
            {
                var existing = result.Fields.FirstOrDefault(f => f.Name == field.Name);
                if (existing is null)
                {
                    result.Fields.Add(field);
                }
                else if (existing.FieldType != field.FieldType || existing.Index != field.Index || existing.Store != field.Store)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' is mapped differently in the sources ({existing.FieldType} / {field.FieldType})");
                }
            }
            primary.AddRange(mapping.PrimaryFields ?? []);
            result.PrimaryFieldsPhonetics = mapping.PrimaryFieldsPhonetics;
            result.PrimaryFieldsEncodeCharacters = mapping.PrimaryFieldsEncodeCharacters;
        }

        result.PrimaryFields = primary.Distinct().ToList();
        return result;
    }
}

/// <summary>Live progress of a running job.</summary>
public class JobRunProgress
{
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Rows read by the last successful run (estimate for the percentage), null = unknown.</summary>
    public long? Expected { get; init; }

    public long Read { get; set; }
    public long Imported { get; set; }
    public long Failed { get; set; }

    /// <summary>Current source (1-based) and number of sources.</summary>
    public int Source { get; set; } = 1;
    public int Sources { get; set; } = 1;

    public double? Percent => Expected is > 0 ? Math.Min(99.9, Read * 100.0 / Expected.Value) : null;

    public double RowsPerSecond
    {
        get
        {
            var seconds = (DateTime.UtcNow - StartedUtc).TotalSeconds;
            return seconds > 0.5 ? Read / seconds : 0;
        }
    }

    public TimeSpan Elapsed => DateTime.UtcNow - StartedUtc;

    /// <summary>"12,400 of ≈27,500 (45 %) · 2,100/s · 0:06"</summary>
    public string Text
    {
        get
        {
            var text = Expected is > 0 ? $"{Read:N0} of ≈{Expected:N0} rows ({Percent:0} %)" : $"{Read:N0} rows";
            if (Failed > 0) text += $" · {Failed:N0} failed";
            if (Sources > 1) text += $" · source {Source}/{Sources}";
            if (RowsPerSecond > 0) text += $" · {RowsPerSecond:N0}/s";
            return text + $" · {(int)Elapsed.TotalMinutes}:{Elapsed.Seconds:00}";
        }
    }
}

/// <summary>Runs import jobs: one run per job at a time.</summary>
public class ImportJobRunner
{
    private readonly IAdminRepositoryService _repository;
    private readonly InstanceRegistry _registry;
    private readonly DatabaseSource _database;
    private readonly ImportService _importer;
    private readonly AuditLogger _audit;
    private readonly JobWebhook _webhook;
    private readonly ILogger<ImportJobRunner> _logger;
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _running = new();
    private readonly ConcurrentDictionary<long, (string TriggeredBy, string? Note)> _queued = new();
    private readonly ConcurrentDictionary<long, JobRunProgress> _progress = new();

    /// <summary>Raised when a run starts or ends (job id).</summary>
    public event Action<long>? RunChanged;

    /// <summary>Raised (at most once per second and job) while a run makes progress (job id).</summary>
    public event Action<long>? ProgressChanged;

    /// <summary>Progress of the running job, null if it does not run.</summary>
    public JobRunProgress? GetProgress(long jobId) => _progress.TryGetValue(jobId, out var progress) ? progress : null;

    public ImportJobRunner(IAdminRepositoryService repository, InstanceRegistry registry, DatabaseSource database,
                           ImportService importer, AuditLogger audit, JobWebhook webhook, ILogger<ImportJobRunner> logger)
    {
        _repository = repository;
        _registry = registry;
        _database = database;
        _importer = importer;
        _audit = audit;
        _webhook = webhook;
        _logger = logger;
    }

    public bool IsRunning(long jobId) => _running.ContainsKey(jobId);

    /// <summary>True if the job runs once more after the current run (triggered while it was running).</summary>
    public bool IsQueued(long jobId) => _queued.ContainsKey(jobId);

    /// <summary>
    /// Starts a run, or - if the job is running - queues one more run after the current one (several triggers
    /// during a run result in one more run).
    /// </summary>
    public async Task<JobTriggerResponse> TriggerAsync(long jobId, string triggeredBy, string? note = null)
    {
        var started = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = RunAsync(jobId, triggeredBy, note: note, onStarted: id => started.TrySetResult(id));
        if (run.IsFaulted || run.IsCompleted && !started.Task.IsCompleted)
        {
            // already running
            _queued[jobId] = (triggeredBy, note);
            if (!IsRunning(jobId) && _queued.TryRemove(jobId, out var queued))
            {
                // the run ended in between: start now
                return await TriggerAsync(jobId, queued.TriggeredBy, queued.Note);
            }
            RunChanged?.Invoke(jobId);
            return new JobTriggerResponse { Status = "queued" };
        }

        var finished = await Task.WhenAny(started.Task, run);
        if (finished == started.Task || started.Task.IsCompleted)
        {
            return new JobTriggerResponse { Status = "started", RunId = await started.Task };
        }
        await run;   // failed before the run was saved: throws
        throw new InvalidOperationException("The run could not be started");
    }

    public void Cancel(long jobId)
    {
        _queued.TryRemove(jobId, out _);
        if (_running.TryGetValue(jobId, out var cancellation)) cancellation.Cancel();
    }

    /// <summary>Name of an index generation of a target ({target}_{yyyyMMddHHmmss}).</summary>
    public static string GenerationName(string target, DateTime utc) => $"{target}_{utc:yyyyMMddHHmmss}";

    public static bool IsGenerationOf(string index, string target) => Regex.IsMatch(index, $"^{Regex.Escape(target)}_\\d{{14}}$");

    /// <summary>Validates a job before saving; returns an error message or null.</summary>
    public static string? Validate(ImportJobRecord job)
    {
        if (String.IsNullOrWhiteSpace(job.Name)) return "Name is required";
        if (!System.Text.RegularExpressions.Regex.IsMatch(job.Target ?? "", "^[a-z0-9_-]{1,64}$")) return "Invalid target name (a-z, 0-9, '_', '-')";
        if (job.Mode == ImportJobModes.ReplaceAlias && job.Target!.Length > 64 - 15) return "Target name too long for generations (max. 49 characters)";
        if (!ImportJobModes.All.Contains(job.Mode)) return "Unknown mode";
        if (!WebhookTriggers.All.Contains(job.WebhookOn)) return "Unknown webhook trigger";
        if (JobWebhook.ValidateUrl(job.WebhookUrl) is { } webhookError) return webhookError;
        if (job.KeepGenerations < 0) return "Keep generations must be >= 0";
        try
        {
            if (job.HasSchedule) CronExpression.Parse(job.Cron.Trim());
            ResolveTimeZone(job.TimeZone);
        }
        catch (Exception ex)
        {
            return $"Schedule: {ex.Message}";
        }
        return null;
    }

    /// <summary>Validates the sources of a job; returns an error message or null.</summary>
    public static string? ValidateSources(ImportJobMapping settings, ImportJobRecord? job = null)
    {
        if (settings.Sources.Count == 0) return "Add a source";
        if (settings.Sources.Select(s => s.MetaId).Distinct().Count() != settings.Sources.Count) return "Two sources have the same meta id";

        foreach (var source in settings.Sources)
        {
            if (source.ConnectionId == 0) return $"{source.DisplayName}: choose a database connection";
            try
            {
                DatabaseSource.ValidateQuery(source.Sql);
            }
            catch (Exception ex)
            {
                return $"{source.DisplayName}: {ex.Message}";
            }
            if (!ImportJobSource.IsMetaId(source.MetaId)) return $"{source.DisplayName}: the meta id must be 32 hex characters (a-f, 0-9)";
            if (!source.HasMapping) return $"{source.DisplayName}: detect the mapping with the preview first";
            if (Components.Pages.CreateIndex.Validate("x", source.Mapping) is { } mappingError) return $"{source.DisplayName}: {mappingError}";
            if (source.Preset == WebgisPreset.Name && WebgisPreset.Validate(source) is { } webgisError) return webgisError;
        }

        IndexMapping indexMapping;
        try
        {
            indexMapping = settings.ToIndexMapping();
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        if (settings.KeyField is { Length: > 0 } keyField)
        {
            var field = indexMapping.Fields.FirstOrDefault(f => f.Name == keyField);
            if (field is null || field.FieldType != FieldTypes.StringType || !field.Index)
            {
                return $"Key field '{keyField}': must be an indexed string field of the mapping";
            }
        }
        if (settings.Sources.FirstOrDefault(s => s.DeleteTemplate is not null && s.DeleteParameters.Count == 0) is { } noDeleteParameter)
        {
            return $"{noDeleteParameter.DisplayName}: the delete template needs at least one parameter, e.g. {{@metaid}}.{{@oid}}";
        }
        if (settings.Sources.Any(s => s.DeleteTemplate is not null) && String.IsNullOrEmpty(settings.KeyField))
        {
            return "A delete template needs a key field";
        }

        if (job is not null && settings.HasParameters)
        {
            if (job.Mode != ImportJobModes.Append) return "Queries with parameters (@name) are only possible in mode 'append or update'";
            if (job.HasSchedule) return "A job with query parameters runs only by trigger: remove the schedule";
        }
        return null;
    }

    public static TimeZoneInfo ResolveTimeZone(string? id)
        => String.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id.Trim());

    /// <summary>Next occurrences of the schedule after <paramref name="fromUtc"/>.</summary>
    public static IEnumerable<DateTime> NextOccurrences(string? cron, string? timeZone, DateTime fromUtc, int count)
    {
        if (String.IsNullOrWhiteSpace(cron)) yield break;
        var expression = CronExpression.Parse(cron.Trim());
        var zone = ResolveTimeZone(timeZone);
        var current = fromUtc;
        for (int i = 0; i < count; i++)
        {
            var next = expression.GetNextOccurrence(current, zone);
            if (next is null) yield break;
            yield return next.Value;
            current = next.Value;
        }
    }

    public async Task<ImportJobRunRecord> RunAsync(long jobId, string triggeredBy, CancellationToken cancellationToken = default,
                                                   string? note = null, Action<long>? onStarted = null, bool dryRun = false, bool queue = false)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_running.TryAdd(jobId, cancellation))
        {
            throw new InvalidOperationException("The job is already running");
        }

        var run = new ImportJobRunRecord { JobId = jobId, StartedUtc = DateTime.UtcNow, Status = ImportJobStatus.Running, TriggeredBy = triggeredBy, Note = note };
        ImportJobRecord? job = null;
        try
        {
            run.Id = await _repository.InsertImportJobRunAsync(run, CancellationToken.None);
            onStarted?.Invoke(run.Id);
            RunChanged?.Invoke(jobId);

            job = await _repository.GetImportJobAsync(jobId, CancellationToken.None) ?? throw new InvalidOperationException("Job not found");
            _progress[jobId] = new JobRunProgress { StartedUtc = run.StartedUtc, Expected = await ExpectedRowsAsync(jobId) };
            if (queue) await ExecuteQueueAsync(job, run, cancellation.Token);
            else await ExecuteAsync(job, run, dryRun, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            run.Status = ImportJobStatus.Cancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import job {JobId} failed", jobId);
            run.Status = ImportJobStatus.Failed;
            run.Message = ex.Message;
        }
        finally
        {
            run.FinishedUtc = DateTime.UtcNow;
            try
            {
                await _repository.UpdateImportJobRunAsync(run, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving the run of import job {JobId} failed", jobId);
            }

            // runs of queued triggers are frequent: only failures go to the audit log
            var succeeded = run.Status is ImportJobStatus.Succeeded or ImportJobStatus.Warning;
            if (!queue || !succeeded)
            {
                await _audit.LogAsync(triggeredBy, AuditActions.JobRun, job?.Instance, job?.Target,
                    $"job {job?.Name ?? jobId.ToString()} · {run.Status} · index {run.IndexName} · read {run.ReadCount}, imported {run.Imported}, failed {run.Failed}" +
                    (run.Message is null ? "" : $" · {run.Message}") + (run.Note is null ? "" : $" · note: {run.Note}"),
                    success: succeeded);
            }

            try
            {
                await _repository.DeleteOldImportJobRunsAsync(jobId, KeepRuns, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Removing old runs of import job {JobId} failed", jobId);
            }

            _running.TryRemove(jobId, out _);
            _progress.TryRemove(jobId, out _);
            cancellation.Dispose();

            // triggered while running: once more (right away, the webhook of this run is called in parallel)
            if (_queued.TryRemove(jobId, out var queued))
            {
                _logger.LogInformation("Starting the queued run of import job {JobId}", jobId);
                _ = RunAsync(jobId, queued.TriggeredBy, CancellationToken.None, queued.Note);
            }
            RunChanged?.Invoke(jobId);
        }

        if (job is not null && !dryRun && JobWebhook.ShouldCall(job, run.Status))
        {
            try
            {
                run.WebhookResult = await _webhook.SendAsync(job, _webhook.CreatePayload(job, run), CancellationToken.None);
                await _repository.UpdateImportJobRunAsync(run, CancellationToken.None);
                RunChanged?.Invoke(jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook of import job {JobId} failed", jobId);
            }
        }

        return run;
    }

    /// <summary>Dry run: reads and converts all rows, sends nothing (no index, metadata or alias changes).</summary>
    public const string DryRunIndex = "(dry run)";

    /// <summary>Runs kept per job (older runs are removed).</summary>
    public const int KeepRuns = 500;

    /// <summary>
    /// Processes the queued triggers of an "append or update" job: deletes ({"delete": ...}) and one query per
    /// parameter set into the target index (alias), documents with the same key are replaced. One refresh at the end.
    /// Processed calls are removed from the queue, also if the run fails (the run history shows the error).
    /// </summary>
    private async Task ExecuteQueueAsync(ImportJobRecord job, ImportJobRunRecord run, CancellationToken cancellationToken)
    {
        var items = await _repository.GetJobTriggerQueueAsync(job.Id, CancellationToken.None);
        if (items.Count == 0)
        {
            run.Status = ImportJobStatus.Succeeded;
            run.Message = "nothing queued";
            return;
        }
        var upToId = items.Max(i => i.Id);

        try
        {
            if (job.Mode != ImportJobModes.Append) throw new InvalidOperationException("Triggers with parameters need the mode 'append or update'");

            var instance = await _registry.GetAsync(job.Instance, cancellationToken) ?? throw new InvalidOperationException($"Instance {job.Instance} not found");
            var client = _registry.CreateClient(instance);
            var connections = await _database.GetConnectionsAsync(cancellationToken);
            var settings = ImportJobMapping.ForJob(job);
            if (ValidateSources(settings) is { } sourceError) throw new InvalidOperationException(sourceError);

            var index = job.Target;
            if (!await client.IndexExistsAsync(index, cancellationToken))
            {
                await client.CreateIndexAsync(index, cancellationToken);
                await client.PutMappingAsync(index, settings.ToIndexMapping(), cancellationToken);
            }
            run.IndexName = index;

            // identical calls once (the newest note wins)
            var calls = items.GroupBy(i => (i.Kind, Source: i.Source?.ToLowerInvariant(), Key: QueryParameters.Key(QueryParameters.FromJson(JsonDocument.Parse(i.ParamsJson).RootElement))))
                             .Select(g => g.Last()).ToList();
            var notes = items.Where(i => i.Note is not null).Select(i => i.Note!).Distinct().ToList();
            run.Note = notes.Count == 0 ? null : notes.Count == 1 ? notes[0] : $"{notes[^1]} (+{notes.Count - 1})";
            await _repository.UpdateImportJobRunAsync(run, CancellationToken.None);

            // deletes
            var deleteKeys = new List<string>();
            foreach (var call in calls.Where(c => c.Kind == JobTriggerKinds.Delete))
            {
                var parameters = QueryParameters.FromJson(JsonDocument.Parse(call.ParamsJson).RootElement);
                foreach (var source in settings.Sources.Where(s => s.Accepts(parameters.Keys, call.Source, delete: true)))
                {
                    var key = FieldTemplate.Render(source.DeleteTemplate!, new Dictionary<string, object?>(), source.VariablesWith(parameters));
                    if (key.Length > 0) deleteKeys.Add(key);
                }
            }
            if (deleteKeys.Count > 0)
            {
                await client.RemoveDocumentsByValuesAsync(index, settings.KeyField!, deleteKeys.Distinct().ToList(), cancellationToken);
            }

            // updates: one query per parameter set and source
            var report = new ImportReport();
            var errors = new List<string>();
            TimeSpan readTime = default, convertTime = default, sendTime = default;
            var parameterSets = calls.Where(c => c.Kind == JobTriggerKinds.Params).ToList();
            foreach (var call in parameterSets)
            {
                var parameters = QueryParameters.FromJson(JsonDocument.Parse(call.ParamsJson).RootElement);
                foreach (var source in settings.Sources.Where(s => s.Accepts(parameters.Keys, call.Source, delete: false)))
                {
                    var connection = connections.FirstOrDefault(c => c.Id == source.ConnectionId)
                                     ?? throw new InvalidOperationException($"{source.DisplayName}: database connection not found");
                    var options = new ImportOptions
                    {
                        BatchSize = job.BatchSize,
                        MaxErrors = 0,
                        RefreshAtEnd = false,
                        KeyField = settings.KeyField,
                        Variables = source.VariablesWith(parameters)
                    };
                    var sourceReport = await _importer.ImportAsync(client, index, _database.ReadAsync(connection, source.Sql, parameters, cancellationToken),
                                                                   source.Mapping, options, ProgressReporter(job.Id, report.Read, report.Imported, errors.Count), cancellationToken);
                    report.Read += sourceReport.Read;
                    report.Imported += sourceReport.Imported;
                    readTime += sourceReport.ReadTime;
                    convertTime += sourceReport.ConvertTime;
                    sendTime += sourceReport.SendTime;
                    errors.AddRange(sourceReport.Errors.Select(e => $"{source.DisplayName} ({QueryParameters.Key(parameters)}) row {e.Row}: {e.Message}"));
                    if (sourceReport.Cancelled) throw new OperationCanceledException();
                    if (sourceReport.FatalError is not null) throw new InvalidOperationException($"{source.DisplayName} ({QueryParameters.Key(parameters)}): {sourceReport.FatalError}");
                }
            }

            if (report.Imported > 0 || deleteKeys.Count > 0)
            {
                await client.RefreshIndexAsync(index, CancellationToken.None);
            }

            run.ReadCount = report.Read;
            run.Imported = report.Imported;
            run.Failed = errors.Count;
            run.Status = errors.Count > 0 ? ImportJobStatus.Warning : ImportJobStatus.Succeeded;
            run.Message = $"queued triggers: {parameterSets.Count} parameter sets, {deleteKeys.Distinct().Count()} deletes ({items.Count} calls)" +
                          (errors.Count == 0 ? "" : $" · {errors.Count} rows skipped, first: {errors[0]}") +
                          " · " + ImportReport.FormatTimes(readTime, convertTime, sendTime);
        }
        finally
        {
            await _repository.DeleteJobTriggerQueueAsync(job.Id, upToId, CancellationToken.None);
        }
    }

    private async Task ExecuteAsync(ImportJobRecord job, ImportJobRunRecord run, bool dryRun, CancellationToken cancellationToken)
    {
        var instance = await _registry.GetAsync(job.Instance, cancellationToken) ?? throw new InvalidOperationException($"Instance {job.Instance} not found");
        var client = _registry.CreateClient(instance);
        var connections = await _database.GetConnectionsAsync(cancellationToken);
        var settings = ImportJobMapping.ForJob(job);
        if (ValidateSources(settings) is { } sourceError) throw new InvalidOperationException(sourceError);
        if (settings.HasParameters)
        {
            throw new InvalidOperationException("The queries have parameters (@name): the job runs only by trigger with parameters");
        }
        var indexMapping = settings.ToIndexMapping();
        var clearFirst = false;

        string index;
        string? previousIndex = null;

        if (dryRun)
        {
            index = DryRunIndex;
        }
        else if (job.Mode == ImportJobModes.ReplaceAlias)
        {
            var aliases = await client.GetAliasesAsync(cancellationToken);
            aliases.TryGetValue(job.Target, out previousIndex);
            if (previousIndex is null && await client.IndexExistsAsync(job.Target, cancellationToken))
            {
                throw new InvalidOperationException($"'{job.Target}' is an index, not an alias. Choose another target or use the mode 'clear'.");
            }

            // generations are rebuilt from the source by every run: no archive (writing it is the slowest part)
            index = GenerationName(job.Target, run.StartedUtc);
            await client.CreateIndexAsync(index, archive: false, cancellationToken);
            await client.PutMappingAsync(index, indexMapping, cancellationToken);
        }
        else
        {
            index = job.Target;
            if (!await client.IndexExistsAsync(index, cancellationToken))
            {
                await client.CreateIndexAsync(index, cancellationToken);
                await client.PutMappingAsync(index, indexMapping, cancellationToken);
            }
            clearFirst = job.Mode == ImportJobModes.Clear;
        }
        run.IndexName = index;
        await _repository.UpdateImportJobRunAsync(run, CancellationToken.None);

        // metadata entry per source (webgis reads it with the meta id from the document id)
        foreach (var source in settings.Sources.Where(s => !s.Metadata.IsEmpty && !dryRun))
        {
            await client.PutMetadataAsync(index, source.MetaId, source.Metadata.ToJson(source.MetaId), cancellationToken);
        }
        if (clearFirst)
        {
            // entries of sources that were removed from the job
            var metaIds = settings.Sources.Select(s => s.MetaId).ToHashSet();
            foreach (var name in (await client.GetMetadataAsync(index, cancellationToken)).Keys.Where(k => ImportJobSource.IsMetaId(k) && !metaIds.Contains(k)))
            {
                await client.RemoveMetadataAsync(index, name, cancellationToken);
            }
        }

        var report = new ImportReport();
        var errors = new List<string>();
        TimeSpan readTime = default, convertTime = default, sendTime = default;
        for (int i = 0; i < settings.Sources.Count; i++)
        {
            var source = settings.Sources[i];
            var connection = connections.FirstOrDefault(c => c.Id == source.ConnectionId)
                             ?? throw new InvalidOperationException($"{source.DisplayName}: database connection not found");
            if (_progress.TryGetValue(job.Id, out var progress))
            {
                progress.Source = i + 1;
                progress.Sources = settings.Sources.Count;
            }

            var options = new ImportOptions
            {
                BatchSize = job.BatchSize,
                MaxErrors = job.MaxErrors <= 0 ? 0 : Math.Max(1, job.MaxErrors - errors.Count),
                ClearIndexFirst = clearFirst && i == 0,
                RefreshAtEnd = false,
                Variables = source.Variables,
                DryRun = dryRun,
                KeyField = job.Mode == ImportJobModes.Append ? settings.KeyField : null
            };

            var sourceReport = await _importer.ImportAsync(client, index, _database.ReadAsync(connection, source.Sql, cancellationToken),
                                                           source.Mapping, options,
                                                           ProgressReporter(job.Id, report.Read, report.Imported, errors.Count), cancellationToken);
            report.Read += sourceReport.Read;
            report.Imported += sourceReport.Imported;
            readTime += sourceReport.ReadTime;
            convertTime += sourceReport.ConvertTime;
            sendTime += sourceReport.SendTime;
            errors.AddRange(sourceReport.Errors.Select(e => settings.Sources.Count > 1 ? $"{source.DisplayName} row {e.Row}: {e.Message}" : $"row {e.Row}: {e.Message}"));
            report.Cancelled = sourceReport.Cancelled;
            report.StoppedBecauseOfErrors = sourceReport.StoppedBecauseOfErrors;
            report.FatalError = sourceReport.FatalError is null ? null : (settings.Sources.Count > 1 ? $"{source.DisplayName}: {sourceReport.FatalError}" : sourceReport.FatalError);

            if (report.Cancelled || report.StoppedBecauseOfErrors || report.FatalError is not null) break;
        }

        // where the time went: source (read), conversion, server (send)
        var times = ImportReport.FormatTimes(readTime, convertTime, sendTime, dryRun);

        if (dryRun)
        {
            run.ReadCount = report.Read;
            run.Imported = report.Imported;
            run.Failed = errors.Count;
            run.Status = report.Cancelled ? ImportJobStatus.Cancelled
                       : report.FatalError is not null || report.StoppedBecauseOfErrors ? ImportJobStatus.Failed
                       : errors.Count > 0 ? ImportJobStatus.Warning : ImportJobStatus.Succeeded;
            var rate = readTime.TotalSeconds > 0 ? FormattableString.Invariant($" · {report.Read / readTime.TotalSeconds:0} rows/s read") : "";
            run.Message = $"dry run, nothing sent · {times}{rate}" +
                          (report.FatalError is null ? "" : $" · {report.FatalError}") +
                          (errors.Count == 0 ? "" : $" · {errors.Count} rows not convertible, first: {errors[0]}");
            return;
        }

        if (report.Imported > 0)
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
        run.ReadCount = report.Read;
        run.Imported = report.Imported;
        run.Failed = errors.Count;

        var failed = report.FatalError is not null || report.StoppedBecauseOfErrors || report.Cancelled;
        if (failed)
        {
            run.Status = report.Cancelled ? ImportJobStatus.Cancelled : ImportJobStatus.Failed;
            run.Message = report.FatalError ?? (report.StoppedBecauseOfErrors ? $"Stopped after {errors.Count} errors: {errors.LastOrDefault()}" : "Cancelled");

            if (job.Mode == ImportJobModes.ReplaceAlias)
            {
                // keep the alias on the previous generation, drop the incomplete one
                await TryRemoveIndexAsync(client, index);
                run.Message += $" · alias {job.Target} unchanged" + (previousIndex is null ? "" : $" ({previousIndex})");
            }
            run.Message += $" · {times}";
            return;
        }

        if (job.Mode == ImportJobModes.ReplaceAlias)
        {
            await client.SetAliasAsync(job.Target, index, CancellationToken.None);
            var removed = await RemoveOldGenerationsAsync(client, job.Target, index, job.KeepGenerations);
            run.Message = $"alias {job.Target} → {index}" + (previousIndex is null ? "" : $" (was {previousIndex})") +
                          (removed.Count == 0 ? "" : $" · removed {String.Join(", ", removed)}");
        }

        run.Status = errors.Count > 0 ? ImportJobStatus.Warning : ImportJobStatus.Succeeded;
        if (errors.Count > 0)
        {
            run.Message = $"{errors.Count} rows skipped, first: {errors[0]}" +
                          (run.Message is null ? "" : $" · {run.Message}");
        }
        run.Message = run.Message is null ? times : $"{run.Message} · {times}";
    }

    /// <summary>Rows read by the last successful run (estimate for the progress).</summary>
    private async Task<long?> ExpectedRowsAsync(long jobId)
    {
        try
        {
            var last = (await _repository.GetImportJobRunsAsync(jobId, 20, CancellationToken.None))
                .FirstOrDefault(r => r.Status is ImportJobStatus.Succeeded or ImportJobStatus.Warning);
            return last?.ReadCount > 0 ? last.ReadCount : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Updates the live progress of the job; ProgressChanged at most once per second.</summary>
    private IProgress<ImportProgress> ProgressReporter(long jobId, long readBefore = 0, long importedBefore = 0, long failedBefore = 0)
    {
        var lastEvent = DateTime.MinValue;
        return new SynchronousProgress<ImportProgress>(p =>
        {
            if (!_progress.TryGetValue(jobId, out var progress)) return;
            progress.Read = readBefore + p.Read;
            progress.Imported = importedBefore + p.Imported;
            progress.Failed = failedBefore + p.Failed;

            if ((DateTime.UtcNow - lastEvent).TotalSeconds >= 1)
            {
                lastEvent = DateTime.UtcNow;
                ProgressChanged?.Invoke(jobId);
            }
        });
    }

    /// <summary>IProgress without SynchronizationContext posting (the import runs in the background).</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>Removes generations of the target that are older than the kept ones and not used by any alias.</summary>
    internal static async Task<List<string>> RemoveOldGenerationsAsync(LuceneServerV2Client client, string target, string current, int keep)
    {
        var aliased = (await client.GetAliasesAsync()).Values.ToHashSet();
        var old = (await client.GetIndicesAsync())
            .Where(i => IsGenerationOf(i, target) && i != current && !aliased.Contains(i))
            .OrderByDescending(i => i, StringComparer.Ordinal)
            .Skip(Math.Max(0, keep))
            .ToList();

        var removed = new List<string>();
        foreach (var index in old)
        {
            if (await TryRemoveIndexAsync(client, index)) removed.Add(index);
        }
        return removed;
    }

    private static async Task<bool> TryRemoveIndexAsync(LuceneServerV2Client client, string index)
    {
        try
        {
            await client.RemoveIndexAsync(index, CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Starts enabled jobs when their cron schedule is due (checked every 30 seconds). At startup, jobs with
/// CatchUp run once if a scheduled run was missed while the admin application was stopped. Disable with Admin:Jobs:Enabled = false
/// (e.g. if more than one admin instance runs against the same database).
/// </summary>
public class ImportJobScheduler : BackgroundService
{
    private readonly IAdminRepositoryService _repository;
    private readonly ImportJobRunner _runner;
    private readonly IConfiguration _config;
    private readonly ILogger<ImportJobScheduler> _logger;

    public ImportJobScheduler(IAdminRepositoryService repository, ImportJobRunner runner, IConfiguration config, ILogger<ImportJobScheduler> logger)
    {
        _repository = repository;
        _runner = runner;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var interrupted = await _repository.MarkRunningImportJobRunsInterruptedAsync(stoppingToken);
            if (interrupted > 0) _logger.LogWarning("{Count} import job runs were interrupted by the last shutdown", interrupted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Marking interrupted import job runs failed");
        }

        if (!_config.GetValue("Admin:Jobs:Enabled", true))
        {
            _logger.LogInformation("Import job scheduler is disabled (Admin:Jobs:Enabled = false)");
            return;
        }

        var lastCheck = DateTime.UtcNow;
        try
        {
            var lastRuns = await _repository.GetLastImportJobRunsAsync(stoppingToken);
            foreach (var job in (await _repository.GetImportJobsAsync(stoppingToken)).Where(j => j.Enabled && j.CatchUp))
            {
                if (IsMissed(job, lastRuns.TryGetValue(job.Id, out var run) ? run.StartedUtc : null, lastCheck) && !_runner.IsRunning(job.Id))
                {
                    _logger.LogInformation("Catching up the missed run of import job {Job}", job.Name);
                    _ = _runner.RunAsync(job.Id, $"job:{job.Name} (catch-up)", stoppingToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Catching up import jobs failed");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            try
            {
                foreach (var job in (await _repository.GetImportJobsAsync(stoppingToken)).Where(j => j.Enabled))
                {
                    if (IsDue(job, lastCheck, now) && !_runner.IsRunning(job.Id))
                    {
                        _logger.LogInformation("Starting import job {Job}", job.Name);
                        _ = _runner.RunAsync(job.Id, $"job:{job.Name}", stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Import job scheduler failed");
            }
            lastCheck = now;
        }
    }

    /// <summary>True if an occurrence of the schedule lies in (lastCheckUtc, nowUtc].</summary>
    internal static bool IsDue(ImportJobRecord job, DateTime lastCheckUtc, DateTime nowUtc)
    {
        if (!job.HasSchedule) return false;
        try
        {
            var next = CronExpression.Parse(job.Cron.Trim()).GetNextOccurrence(lastCheckUtc, ImportJobRunner.ResolveTimeZone(job.TimeZone));
            return next.HasValue && next.Value <= nowUtc;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True if a scheduled occurrence was missed: one lies after the last run (or, without runs, after the last change
    /// of the job) and before <paramref name="nowUtc"/>.
    /// </summary>
    internal static bool IsMissed(ImportJobRecord job, DateTime? lastRunUtc, DateTime nowUtc)
    {
        if (!job.HasSchedule) return false;
        var since = lastRunUtc ?? job.ModifiedUtc;
        try
        {
            var next = CronExpression.Parse(job.Cron.Trim()).GetNextOccurrence(since, ImportJobRunner.ResolveTimeZone(job.TimeZone));
            return next.HasValue && next.Value <= nowUtc;
        }
        catch
        {
            return false;
        }
    }
}
