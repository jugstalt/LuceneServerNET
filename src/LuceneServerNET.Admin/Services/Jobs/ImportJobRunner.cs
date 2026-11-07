using Cronos;
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

    /// <summary>Add the documents to the index {target}.</summary>
    public const string Append = "append";

    public static readonly string[] All = [ReplaceAlias, Clear, Append];
}

public static class ImportJobStatus
{
    public const string Running = "running", Succeeded = "succeeded", Warning = "warning", Failed = "failed", Cancelled = "cancelled", Interrupted = "interrupted";
}

/// <summary>The mapping of a job (fixed when the job is saved).</summary>
public class ImportJobMapping
{
    public InferredMapping Mapping { get; set; } = new();
    public Core.Phonetics.Algorithm Phonetics { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);
    public static ImportJobMapping FromJson(string json) => JsonSerializer.Deserialize<ImportJobMapping>(json) ?? new();
}

/// <summary>Runs import jobs: one run per job at a time.</summary>
public class ImportJobRunner
{
    private readonly IAdminRepositoryService _repository;
    private readonly InstanceRegistry _registry;
    private readonly DatabaseSource _database;
    private readonly ImportService _importer;
    private readonly AuditLogger _audit;
    private readonly ILogger<ImportJobRunner> _logger;
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _running = new();

    /// <summary>Raised when a run starts or ends (job id).</summary>
    public event Action<long>? RunChanged;

    public ImportJobRunner(IAdminRepositoryService repository, InstanceRegistry registry, DatabaseSource database,
                           ImportService importer, AuditLogger audit, ILogger<ImportJobRunner> logger)
    {
        _repository = repository;
        _registry = registry;
        _database = database;
        _importer = importer;
        _audit = audit;
        _logger = logger;
    }

    public bool IsRunning(long jobId) => _running.ContainsKey(jobId);

    public void Cancel(long jobId)
    {
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
        if (job.KeepGenerations < 0) return "Keep generations must be >= 0";
        try
        {
            DatabaseSource.ValidateQuery(job.Sql);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        try
        {
            CronExpression.Parse(job.Cron);
            ResolveTimeZone(job.TimeZone);
        }
        catch (Exception ex)
        {
            return $"Schedule: {ex.Message}";
        }
        return null;
    }

    public static TimeZoneInfo ResolveTimeZone(string? id)
        => String.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id.Trim());

    /// <summary>Next occurrences of the schedule after <paramref name="fromUtc"/>.</summary>
    public static IEnumerable<DateTime> NextOccurrences(string cron, string? timeZone, DateTime fromUtc, int count)
    {
        var expression = CronExpression.Parse(cron);
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

    public async Task<ImportJobRunRecord> RunAsync(long jobId, string triggeredBy, CancellationToken cancellationToken = default)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_running.TryAdd(jobId, cancellation))
        {
            throw new InvalidOperationException("The job is already running");
        }

        var run = new ImportJobRunRecord { JobId = jobId, StartedUtc = DateTime.UtcNow, Status = ImportJobStatus.Running, TriggeredBy = triggeredBy };
        ImportJobRecord? job = null;
        try
        {
            run.Id = await _repository.InsertImportJobRunAsync(run, CancellationToken.None);
            RunChanged?.Invoke(jobId);

            job = await _repository.GetImportJobAsync(jobId, CancellationToken.None) ?? throw new InvalidOperationException("Job not found");
            await ExecuteAsync(job, run, cancellation.Token);
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

            await _audit.LogAsync(triggeredBy, AuditActions.JobRun, job?.Instance, job?.Target,
                $"job {job?.Name ?? jobId.ToString()} · {run.Status} · index {run.IndexName} · read {run.ReadCount}, imported {run.Imported}, failed {run.Failed}" +
                (run.Message is null ? "" : $" · {run.Message}"),
                success: run.Status is ImportJobStatus.Succeeded or ImportJobStatus.Warning);

            _running.TryRemove(jobId, out _);
            cancellation.Dispose();
            RunChanged?.Invoke(jobId);
        }

        return run;
    }

    private async Task ExecuteAsync(ImportJobRecord job, ImportJobRunRecord run, CancellationToken cancellationToken)
    {
        var instance = await _registry.GetAsync(job.Instance, cancellationToken) ?? throw new InvalidOperationException($"Instance {job.Instance} not found");
        var client = _registry.CreateClient(instance);
        var source = (await _database.GetConnectionsAsync(cancellationToken)).FirstOrDefault(c => c.Id == job.ConnectionId)
                     ?? throw new InvalidOperationException("Database connection not found");
        var jobMapping = ImportJobMapping.FromJson(job.MappingJson);
        var indexMapping = MappingInference.ToIndexMapping(jobMapping.Mapping, jobMapping.Phonetics);
        var options = new ImportOptions { BatchSize = job.BatchSize, MaxErrors = job.MaxErrors, RefreshAtEnd = true };

        string index;
        string? previousIndex = null;

        if (job.Mode == ImportJobModes.ReplaceAlias)
        {
            var aliases = await client.GetAliasesAsync(cancellationToken);
            aliases.TryGetValue(job.Target, out previousIndex);
            if (previousIndex is null && await client.IndexExistsAsync(job.Target, cancellationToken))
            {
                throw new InvalidOperationException($"'{job.Target}' is an index, not an alias. Choose another target or use the mode 'clear'.");
            }

            index = GenerationName(job.Target, run.StartedUtc);
            await client.CreateIndexAsync(index, cancellationToken);
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
            options.ClearIndexFirst = job.Mode == ImportJobModes.Clear;
        }
        run.IndexName = index;
        await _repository.UpdateImportJobRunAsync(run, CancellationToken.None);

        var report = await _importer.ImportAsync(client, index, _database.ReadAsync(source, job.Sql, cancellationToken),
                                                 jobMapping.Mapping, options, null, cancellationToken);
        run.ReadCount = report.Read;
        run.Imported = report.Imported;
        run.Failed = report.Errors.Count;

        var failed = report.FatalError is not null || report.StoppedBecauseOfErrors || report.Cancelled;
        if (failed)
        {
            run.Status = report.Cancelled ? ImportJobStatus.Cancelled : ImportJobStatus.Failed;
            run.Message = report.FatalError ?? (report.StoppedBecauseOfErrors ? $"Stopped after {report.Errors.Count} errors: {report.Errors.LastOrDefault()?.Message}" : "Cancelled");

            if (job.Mode == ImportJobModes.ReplaceAlias)
            {
                // keep the alias on the previous generation, drop the incomplete one
                await TryRemoveIndexAsync(client, index);
                run.Message += $" · alias {job.Target} unchanged" + (previousIndex is null ? "" : $" ({previousIndex})");
            }
            return;
        }

        if (job.Mode == ImportJobModes.ReplaceAlias)
        {
            await client.SetAliasAsync(job.Target, index, CancellationToken.None);
            var removed = await RemoveOldGenerationsAsync(client, job.Target, index, job.KeepGenerations);
            run.Message = $"alias {job.Target} → {index}" + (previousIndex is null ? "" : $" (was {previousIndex})") +
                          (removed.Count == 0 ? "" : $" · removed {String.Join(", ", removed)}");
        }

        run.Status = report.Errors.Count > 0 ? ImportJobStatus.Warning : ImportJobStatus.Succeeded;
        if (report.Errors.Count > 0)
        {
            run.Message = $"{report.Errors.Count} rows skipped, first: row {report.Errors[0].Row}: {report.Errors[0].Message}" +
                          (run.Message is null ? "" : $" · {run.Message}");
        }
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
/// Starts enabled jobs when their cron schedule is due (checked every 30 seconds). Runs missed while the
/// admin application was stopped are not caught up. Disable with Admin:Jobs:Enabled = false
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
        try
        {
            var next = CronExpression.Parse(job.Cron).GetNextOccurrence(lastCheckUtc, ImportJobRunner.ResolveTimeZone(job.TimeZone));
            return next.HasValue && next.Value <= nowUtc;
        }
        catch
        {
            return false;
        }
    }
}
