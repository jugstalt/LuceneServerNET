using LuceneServerNET.Engine.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LuceneServerNET.Diagnostics;

/// <summary>
/// Ready check: RootPath (and ArchivePath, when configured) exists and is writable.
/// Writes and deletes a small probe file; fails e.g. for a full or read-only volume.
/// </summary>
public class StorageHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<LuceneServiceOptions> _options;

    public StorageHealthCheck(IOptionsMonitor<LuceneServiceOptions> options)
    {
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;

        if (String.IsNullOrWhiteSpace(options.RootPath))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("LuceneServer:RootPath is not configured"));
        }

        var error = ProbeWritable(options.RootPath, "RootPath");
        if (error == null && !String.IsNullOrWhiteSpace(options.ArchivePath))
        {
            error = ProbeWritable(options.ArchivePath, "ArchivePath");
        }

        return Task.FromResult(error == null
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy(error));
    }

    private static string ProbeWritable(string path, string name)
    {
        try
        {
            Directory.CreateDirectory(path);  // same as the server does on first use

            var probeFile = Path.Combine(path, $".health-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, "ok");
            File.Delete(probeFile);

            return null;
        }
        catch (Exception ex)
        {
            return $"{name} is not writable: {ex.GetType().Name}";
        }
    }
}
