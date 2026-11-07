using LuceneServerNET.Admin.Services.Audit;
using LuceneServerNET.Admin.Services.Instances;

namespace LuceneServerNET.Admin.Services.Import;

public static class BackupEndpoints
{
    /// <summary>GET /download/backup/{instance}/{index}: index backup as ZIP (requires a logged in admin user).</summary>
    public static void MapBackupEndpoints(this WebApplication app)
    {
        app.MapGet("/download/backup/{instance}/{index}", async (string instance, string index, HttpContext context,
                                                                 InstanceRegistry registry, CopyService copier, AuditLogger audit,
                                                                 CancellationToken cancellationToken) =>
        {
            var user = context.User.Identity?.Name ?? "";
            var server = await registry.GetAsync(instance, cancellationToken);
            if (server is null)
            {
                return Results.NotFound("Instance not found");
            }

            var client = registry.CreateClient(server);
            if (!await client.IndexExistsAsync(index, cancellationToken))
            {
                return Results.NotFound("Index not found");
            }

            // temp file (deleted when the response stream is closed): no large backups in memory
            var path = Path.Combine(Path.GetTempPath(), $"lucene-admin-backup-{Guid.NewGuid():N}.zip");
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                var manifest = await copier.WriteBackupAsync(client, index, server.Name, file, cancellationToken);
                file.Position = 0;

                await audit.LogAsync(user, AuditActions.Export, server.Name, index, $"backup · {manifest.DocumentCount} documents · {file.Length} bytes");
                return Results.File(file, "application/zip", $"{index}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");
            }
            catch (Exception ex)
            {
                await file.DisposeAsync();
                await audit.LogAsync(user, AuditActions.Export, server.Name, index, $"backup · error: {ex.Message}", success: false);
                return Results.Problem(ex.Message);
            }
        });
    }
}
