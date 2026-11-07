using LuceneServerNET.Admin.Services.Repository;
using Microsoft.AspNetCore.Components.Authorization;

namespace LuceneServerNET.Admin.Services.Audit;

/// <summary>Action names of the audit log (stable strings, used for filtering).</summary>
public static class AuditActions
{
    public const string Login = "login", LoginFailed = "login.failed", Logout = "logout";
    public const string InstanceSave = "instance.save", InstanceDelete = "instance.delete";
    public const string ConnectionSave = "connection.save", ConnectionDelete = "connection.delete", ConnectionShow = "connection.show", ConnectionTest = "connection.test";
    public const string IndexCreate = "index.create", IndexDelete = "index.delete", IndexClear = "index.clear", IndexRefresh = "index.refresh";
    public const string DocumentsDelete = "documents.delete", MetadataSave = "metadata.save";
    public const string Import = "import", Copy = "copy", Restore = "restore", Export = "export";
    public const string AliasSet = "alias.set", AliasDelete = "alias.delete";
    public const string JobSave = "job.save", JobDelete = "job.delete", JobRun = "job.run", JobTrigger = "job.trigger", JobToken = "job.token";
}

/// <summary>
/// Writes the audit log. Never throws: a failing audit write is logged, the admin action itself is not affected.
/// </summary>
public class AuditLogger
{
    private readonly IAdminRepositoryService _repository;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(IAdminRepositoryService repository, ILogger<AuditLogger> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task LogAsync(string user, string action, string? instance = null, string? target = null, string? details = null, bool success = true)
    {
        try
        {
            await _repository.InsertAuditAsync(new AuditRecord
            {
                TimestampUtc = DateTime.UtcNow,
                UserName = String.IsNullOrWhiteSpace(user) ? "(anonymous)" : user,
                Action = action,
                Instance = instance,
                Target = target,
                Details = details is { Length: > 4000 } ? details[..4000] : details,
                Success = success
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Writing the audit log failed ({Action} {Instance} {Target})", action, instance, target);
        }
    }
}

/// <summary>Audit log for components: takes the user from the authentication state of the circuit.</summary>
public class AuditService
{
    private readonly AuditLogger _logger;
    private readonly AuthenticationStateProvider _authentication;

    public AuditService(AuditLogger logger, AuthenticationStateProvider authentication)
    {
        _logger = logger;
        _authentication = authentication;
    }

    public async Task LogAsync(string action, string? instance = null, string? target = null, string? details = null, bool success = true)
    {
        string user;
        try
        {
            user = (await _authentication.GetAuthenticationStateAsync()).User.Identity?.Name ?? "";
        }
        catch
        {
            user = "";
        }
        await _logger.LogAsync(user, action, instance, target, details, success);
    }
}

/// <summary>Deletes audit entries older than Admin:Audit:RetentionDays (default 180), once a day.</summary>
public class AuditCleanupService : BackgroundService
{
    private readonly IAdminRepositoryService _repository;
    private readonly IConfiguration _config;
    private readonly ILogger<AuditCleanupService> _logger;

    public AuditCleanupService(IAdminRepositoryService repository, IConfiguration config, ILogger<AuditCleanupService> logger)
    {
        _repository = repository;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var days = _config.GetValue("Admin:Audit:RetentionDays", 180);
                if (days > 0)
                {
                    var deleted = await _repository.DeleteAuditOlderThanAsync(DateTime.UtcNow.AddDays(-days), stoppingToken);
                    if (deleted > 0)
                    {
                        _logger.LogInformation("Deleted {Count} audit entries older than {Days} days", deleted, days);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Audit log cleanup failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
