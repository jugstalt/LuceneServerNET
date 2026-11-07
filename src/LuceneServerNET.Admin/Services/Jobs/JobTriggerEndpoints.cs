using LuceneServerNET.Admin.Services.Audit;
using LuceneServerNET.Admin.Services.Repository;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace LuceneServerNET.Admin.Services.Jobs;

/// <summary>Trigger tokens of import jobs: random, shown once, stored as SHA-256 hash.</summary>
public static class JobTriggerToken
{
    public const string Prefix = "ljt_";

    public static string Create() => Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));

    public static bool Verify(string? token, string? hash)
    {
        if (String.IsNullOrWhiteSpace(token) || String.IsNullOrEmpty(hash)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(hash));
    }

    /// <summary>Token from "Authorization: Bearer ..." (or the header X-Job-Token).</summary>
    public static string? FromRequest(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return authorization[7..].Trim();
        return request.Headers["X-Job-Token"].FirstOrDefault();
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public class JobTriggerRequest
{
    [JsonPropertyName("note")] public string? Note { get; set; }
}

public class JobTriggerResponse
{
    /// <summary>started: a new run; queued: the job is running, it runs once more afterwards.</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("runId")] public long? RunId { get; set; }
    [JsonPropertyName("statusUrl")] public string? StatusUrl { get; set; }
}

public static class JobTriggerEndpoints
{
    public const int MaxNoteLength = 200;

    /// <summary>
    /// POST /api/jobs/{id}/run and GET /api/jobs/{id}/runs/{runId}, authenticated with the trigger token of the job
    /// ("Authorization: Bearer ljt_..."), not with the login of the admin UI.
    /// </summary>
    public static void MapJobTriggerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/jobs/{id:long}").AllowAnonymous().DisableAntiforgery();

        group.MapPost("/run", async (long id, HttpContext context, IAdminRepositoryService repository, ImportJobRunner runner,
                                     AuditLogger audit, string? note) =>
        {
            var job = await repository.GetImportJobAsync(id);
            if (job is null || !JobTriggerToken.Verify(JobTriggerToken.FromRequest(context.Request), job.TriggerTokenHash))
            {
                await audit.LogAsync("trigger", AuditActions.JobTrigger, job?.Instance, job?.Target,
                    $"job {id} · invalid token · {context.Connection.RemoteIpAddress}", success: false);
                return Results.Json(new { error = "invalid job or token" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (note is null && context.Request.HasJsonContentType())
            {
                try
                {
                    note = (await context.Request.ReadFromJsonAsync<JobTriggerRequest>())?.Note;
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
                {
                    return Results.BadRequest(new { error = "invalid JSON body" });
                }
            }
            note = String.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, MaxNoteLength)];

            if (!job.Enabled)
            {
                return Results.Json(new { error = "the job is disabled" }, statusCode: StatusCodes.Status409Conflict);
            }

            var result = await runner.TriggerAsync(job.Id, $"trigger:{job.Name}", note);
            await audit.LogAsync("trigger", AuditActions.JobTrigger, job.Instance, job.Target,
                $"job {job.Name} · {result.Status}" + (result.RunId is null ? "" : $" · run {result.RunId}") +
                (note is null ? "" : $" · {note}") + $" · {context.Connection.RemoteIpAddress}");

            if (result.RunId is { } runId)
            {
                result.StatusUrl = $"{context.Request.PathBase}/api/jobs/{job.Id}/runs/{runId}";
            }
            return Results.Json(result, statusCode: StatusCodes.Status202Accepted);
        });

        group.MapGet("/runs/{runId:long}", async (long id, long runId, HttpContext context, IAdminRepositoryService repository) =>
        {
            var job = await repository.GetImportJobAsync(id);
            if (job is null || !JobTriggerToken.Verify(JobTriggerToken.FromRequest(context.Request), job.TriggerTokenHash))
            {
                return Results.Json(new { error = "invalid job or token" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var run = await repository.GetImportJobRunAsync(runId);
            if (run is null || run.JobId != job.Id)
            {
                return Results.NotFound(new { error = "run not found" });
            }

            return Results.Json(new
            {
                runId = run.Id,
                status = run.Status,
                finished = run.FinishedUtc is not null,
                success = JobWebhook.IsSuccess(run.Status),
                run.StartedUtc,
                run.FinishedUtc,
                index = run.IndexName,
                read = run.ReadCount,
                imported = run.Imported,
                failed = run.Failed,
                message = run.Message,
                note = run.Note,
                webhook = run.WebhookResult
            });
        });
    }
}
