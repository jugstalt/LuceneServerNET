using LuceneServerNET.Admin.Services.Repository;
using Microsoft.AspNetCore.DataProtection;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LuceneServerNET.Admin.Services.Jobs;

public static class WebhookTriggers
{
    public const string Always = "always", Failure = "failure";

    public static readonly string[] All = [Failure, Always];
}

/// <summary>JSON body of a job webhook (POST). <c>text</c> is a summary that Slack/Mattermost compatible webhooks show directly.</summary>
public class JobWebhookPayload
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("job")] public string Job { get; set; } = "";
    [JsonPropertyName("jobId")] public long JobId { get; set; }
    [JsonPropertyName("runId")] public long RunId { get; set; }
    [JsonPropertyName("instance")] public string Instance { get; set; } = "";
    [JsonPropertyName("target")] public string Target { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("index")] public string? Index { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("triggeredBy")] public string TriggeredBy { get; set; } = "";
    [JsonPropertyName("startedUtc")] public DateTime StartedUtc { get; set; }
    [JsonPropertyName("finishedUtc")] public DateTime? FinishedUtc { get; set; }
    [JsonPropertyName("durationSeconds")] public double DurationSeconds { get; set; }
    [JsonPropertyName("read")] public long Read { get; set; }
    [JsonPropertyName("imported")] public long Imported { get; set; }
    [JsonPropertyName("failed")] public long Failed { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }

    /// <summary>Link to the job page (only if Admin:PublicUrl is configured).</summary>
    [JsonPropertyName("url")] public string? Url { get; set; }
}

/// <summary>Calls the webhook of a job after a run (POST, JSON, up to 3 attempts).</summary>
public class JobWebhook
{
    public const string HttpClientName = "job-webhooks";
    public const int Attempts = 3;

    private readonly IHttpClientFactory _httpClients;
    private readonly IDataProtector _protector;
    private readonly string? _publicUrl;
    private readonly ILogger<JobWebhook> _logger;

    /// <summary>Pause before the 2nd and 3rd attempt (tests set it to zero).</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public JobWebhook(IHttpClientFactory httpClients, IDataProtectionProvider dataProtection, IConfiguration config, ILogger<JobWebhook> logger)
    {
        _httpClients = httpClients;
        _protector = dataProtection.CreateProtector("LuceneServerNET.Admin.JobWebhooks");
        _publicUrl = config["Admin:PublicUrl"]?.TrimEnd('/');
        _logger = logger;
    }

    public static bool IsSuccess(string status) => status is ImportJobStatus.Succeeded or ImportJobStatus.Warning;

    /// <summary>True if the job has a webhook that is called for a run with this status.</summary>
    public static bool ShouldCall(ImportJobRecord job, string status)
        => !String.IsNullOrWhiteSpace(job.WebhookUrl) && (job.WebhookOn == WebhookTriggers.Always || !IsSuccess(status));

    public static string? ValidateUrl(string? url)
    {
        if (String.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? null
            : "Webhook: invalid URL (http or https)";
    }

    /// <summary>Validates a header line "Name: value"; returns an error message or null.</summary>
    public static string? ValidateHeader(string? header)
    {
        if (String.IsNullOrWhiteSpace(header)) return null;
        return ParseHeader(header) is null ? "Webhook header: use \"Name: value\"" : null;
    }

    public static (string Name, string Value)? ParseHeader(string header)
    {
        var index = header.IndexOf(':');
        if (index <= 0) return null;
        var name = header[..index].Trim();
        var value = header[(index + 1)..].Trim();
        return name.Length == 0 || name.Any(c => Char.IsWhiteSpace(c) || Char.IsControl(c)) || value.Any(Char.IsControl) ? null : (name, value);
    }

    public string ProtectHeader(string header) => _protector.Protect(header.Trim());

    private string? UnprotectHeader(string? protectedHeader)
    {
        if (String.IsNullOrEmpty(protectedHeader)) return null;
        try
        {
            return _protector.Unprotect(protectedHeader);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The webhook header can not be decrypted (DataProtection keys changed?)");
            return null;
        }
    }

    public JobWebhookPayload CreatePayload(ImportJobRecord job, ImportJobRunRecord run)
    {
        var duration = ((run.FinishedUtc ?? DateTime.UtcNow) - run.StartedUtc).TotalSeconds;
        var success = IsSuccess(run.Status);
        var text = String.Create(CultureInfo.InvariantCulture,
            $"{(success ? "✅" : "❌")} Import job {job.Name}: {run.Status} · {job.Instance}/{run.IndexName ?? job.Target} · " +
            $"read {run.ReadCount}, imported {run.Imported}, failed {run.Failed} · {duration:0.0} s")
            + (String.IsNullOrEmpty(run.Message) ? "" : $" · {run.Message}");

        return new JobWebhookPayload
        {
            Text = text,
            Job = job.Name,
            JobId = job.Id,
            RunId = run.Id,
            Instance = job.Instance,
            Target = job.Target,
            Mode = job.Mode,
            Index = run.IndexName,
            Status = run.Status,
            Success = success,
            TriggeredBy = run.TriggeredBy,
            StartedUtc = run.StartedUtc,
            FinishedUtc = run.FinishedUtc,
            DurationSeconds = Math.Round(duration, 1),
            Read = run.ReadCount,
            Imported = run.Imported,
            Failed = run.Failed,
            Message = run.Message,
            Url = _publicUrl is null || job.Id == 0 ? null : $"{_publicUrl}/jobs/{job.Id}"
        };
    }

    /// <summary>Posts the payload (up to 3 attempts); returns a short result for the run history.</summary>
    public async Task<string> SendAsync(ImportJobRecord job, JobWebhookPayload payload, CancellationToken cancellationToken = default)
    {
        var header = UnprotectHeader(job.WebhookHeaderProtected) is { } h ? ParseHeader(h) : null;
        var client = _httpClients.CreateClient(HttpClientName);
        string error = "";

        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            if (attempt > 1) await Task.Delay(RetryDelay, cancellationToken);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, job.WebhookUrl!.Trim()) { Content = JsonContent.Create(payload) };
                if (header is { } hv) request.Headers.TryAddWithoutValidation(hv.Name, hv.Value);

                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return $"webhook {(int)response.StatusCode}" + (attempt > 1 ? $" (attempt {attempt})" : "");
                }
                error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                error = ex is TaskCanceledException ? "timeout" : ex.Message;
            }
        }

        _logger.LogWarning("Webhook of import job {Job} failed: {Error}", job.Name, error);
        return $"webhook failed after {Attempts} attempts: {error}";
    }
}
