using LuceneServerNET.Admin.Services.Audit;
using LuceneServerNET.Admin.Services.Repository;
using System.Security.Cryptography;
using LuceneServerNET.Admin.Services.Import;
using System.Text;
using System.Text.Json;
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

/// <summary>
/// Trigger body: {"note": "...", "source": "Adressen", "params": {"oid": 4711} | [{...}, ...], "delete": {"oid": 5} | [...]}.
/// Without params / delete the job runs completely (as before).
/// </summary>
public static class JobTriggerRequest
{
    /// <summary>Calls of the body for the queue; throws FormatException with a message for the caller.</summary>
    public static List<JobTriggerQueueRecord> ToQueue(JsonElement body, ImportJobRecord job, ImportJobMapping settings, string? note, DateTime nowUtc)
    {
        var records = new List<JobTriggerQueueRecord>();
        if (body.ValueKind != JsonValueKind.Object) return records;

        string? source = body.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        if (source is not null && !settings.Sources.Any(x => x.DisplayName.Equals(source, StringComparison.OrdinalIgnoreCase)))
        {
            throw new FormatException($"Unknown source '{source}'");
        }

        foreach (var (property, kind) in new[] { ("params", JobTriggerKinds.Params), ("delete", JobTriggerKinds.Delete) })
        {
            if (!body.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) continue;

            var sets = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [value];
            foreach (var set in sets)
            {
                var parameters = QueryParameters.FromJson(set);
                var delete = kind == JobTriggerKinds.Delete;
                if (!settings.Sources.Any(x => x.Accepts(parameters.Keys, source, delete)))
                {
                    var known = delete
                        ? settings.Sources.Where(x => x.DeleteTemplate is not null).Select(x => $"{x.DisplayName}: {String.Join(", ", x.DeleteParameters)}")
                        : settings.Sources.Where(x => x.Parameters.Count > 0).Select(x => $"{x.DisplayName}: {String.Join(", ", x.Parameters)}");
                    throw new FormatException($"No source {(delete ? "deletes" : "queries")} with the parameters {String.Join(", ", parameters.Keys)}" +
                                              $" (sources: {String.Join("; ", known.DefaultIfEmpty("none"))})");
                }
                records.Add(new JobTriggerQueueRecord
                {
                    JobId = job.Id,
                    Kind = kind,
                    Source = source,
                    ParamsJson = set.GetRawText(),
                    Note = note,
                    ReceivedUtc = nowUtc
                });
            }
        }

        if (records.Count > 0 && job.Mode != ImportJobModes.Append)
        {
            throw new FormatException("Parameters and deletes are only possible in mode 'append or update'");
        }
        if (records.Count > MaxCalls) throw new FormatException($"Max. {MaxCalls} parameter sets per call");
        return records;
    }

    public const int MaxCalls = 10_000;
}

public class JobTriggerResponse
{
    /// <summary>started: a new run; queued: the job is running, it runs once more afterwards.</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("runId")] public long? RunId { get; set; }
    [JsonPropertyName("statusUrl")] public string? StatusUrl { get; set; }

    /// <summary>Number of queued parameter sets / deletes.</summary>
    [JsonPropertyName("queued")] public int? Queued { get; set; }
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

            JsonElement body = default;
            if (context.Request.HasJsonContentType())
            {
                try
                {
                    body = (await JsonDocument.ParseAsync(context.Request.Body)).RootElement;
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { error = "invalid JSON body" });
                }
                if (note is null && body.ValueKind == JsonValueKind.Object && body.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String)
                {
                    note = n.GetString();
                }
            }
            note = String.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, MaxNoteLength)];

            if (!job.Enabled)
            {
                return Results.Json(new { error = "the job is disabled" }, statusCode: StatusCodes.Status409Conflict);
            }

            // parameters / deletes: queued, processed by JobQueueProcessor (many calls => few runs)
            var settings = ImportJobMapping.ForJob(job);
            List<JobTriggerQueueRecord> queue;
            try
            {
                queue = JobTriggerRequest.ToQueue(body, job, settings, note, DateTime.UtcNow);
            }
            catch (FormatException ex)
            {
                await audit.LogAsync("trigger", AuditActions.JobTrigger, job.Instance, job.Target,
                    $"job {job.Name} · rejected: {ex.Message} · {context.Connection.RemoteIpAddress}", success: false);
                return Results.BadRequest(new { error = ex.Message });
            }
            if (queue.Count > 0)
            {
                await repository.InsertJobTriggerQueueAsync(queue);
                return Results.Json(new JobTriggerResponse { Status = "queued", Queued = queue.Count }, statusCode: StatusCodes.Status202Accepted);
            }
            if (settings.HasParameters)
            {
                return Results.BadRequest(new { error = $"The queries have parameters ({String.Join(", ", settings.Sources.SelectMany(x => x.Parameters).Distinct().Select(p => "@" + p))}): send them as {{\"params\": {{...}}}}" });
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
