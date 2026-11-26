using LuceneServerNET.Admin.Services.Repository;

namespace LuceneServerNET.Admin.Services.Jobs;

/// <summary>
/// Processes the queued triggers with parameters (table job_trigger_queue): a job runs when its newest call is
/// older than the collect window (or its oldest call older than the maximum wait) and it does not run already.
/// While a "rebuild and switch alias" job with the same target runs, the calls stay queued and are applied
/// after the switch (otherwise they would be lost with the old generation).
/// The queue is in the database: calls survive a restart. Disabled with Admin:Jobs:Enabled = false.
/// </summary>
public class JobQueueProcessor : BackgroundService
{
    /// <summary>Calls arriving within this time are processed in one run.</summary>
    public static readonly TimeSpan CollectWindow = TimeSpan.FromSeconds(2);

    /// <summary>A steady stream of calls is processed at least this often.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    private readonly IAdminRepositoryService _repository;
    private readonly ImportJobRunner _runner;
    private readonly IConfiguration _config;
    private readonly ILogger<JobQueueProcessor> _logger;

    public JobQueueProcessor(IAdminRepositoryService repository, ImportJobRunner runner, IConfiguration config, ILogger<JobQueueProcessor> logger)
    {
        _repository = repository;
        _runner = runner;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Admin:Jobs:Enabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var states = await _repository.GetJobTriggerQueueStatesAsync(stoppingToken);
                if (states.Count == 0) continue;

                var jobs = await _repository.GetImportJobsAsync(stoppingToken);
                foreach (var state in states)
                {
                    var job = jobs.FirstOrDefault(j => j.Id == state.JobId);
                    if (job is null || _runner.IsRunning(job.Id)) continue;
                    if (!IsDue(state, DateTime.UtcNow)) continue;
                    if (IsBlockedByRebuild(job, jobs, _runner.IsRunning)) continue;

                    _ = _runner.RunAsync(job.Id, $"trigger:{job.Name} (queue)", stoppingToken, queue: true);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Processing the trigger queue failed");
            }
        }
    }

    internal static bool IsDue(JobTriggerQueueState state, DateTime nowUtc)
        => nowUtc - state.NewestUtc >= CollectWindow || nowUtc - state.OldestUtc >= MaxWait;

    /// <summary>A "rebuild and switch alias" job with the same instance and target is running.</summary>
    internal static bool IsBlockedByRebuild(ImportJobRecord job, IEnumerable<ImportJobRecord> jobs, Func<long, bool> isRunning)
        => jobs.Any(other => other.Id != job.Id && other.Mode == ImportJobModes.ReplaceAlias &&
                             other.Instance == job.Instance && other.Target == job.Target && isRunning(other.Id));
}
