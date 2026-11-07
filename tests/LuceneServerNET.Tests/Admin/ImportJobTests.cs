using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Admin.Services.Jobs;
using LuceneServerNET.Admin.Services.Repository;
using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Mapping;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace LuceneServerNET.Tests.Admin;

public class ImportJobTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lucene-admin-jobs-{Guid.NewGuid():N}.db");

    private async Task<SqliteAdminRepositoryService> CreateRepositoryAsync()
    {
        var repository = new SqliteAdminRepositoryService($"Data Source={_path};Pooling=False", NullLogger<SqliteAdminRepositoryService>.Instance);
        await repository.InitializeAsync();
        return repository;
    }

    private static ImportJobRecord ValidJob() => new()
    {
        Name = "orte nightly",
        Instance = "prod",
        ConnectionId = 1,
        Sql = "SELECT * FROM orte",
        Target = "orte",
        Mode = ImportJobModes.ReplaceAlias,
        MappingJson = new ImportJobMapping().ToJson(),
        Cron = "0 3 * * *",
        TimeZone = null
    };

    [Fact]
    public async Task Repository_JobsAndRuns()
    {
        var repository = await CreateRepositoryAsync();
        var id = await repository.InsertImportJobAsync(ValidJob());

        var job = await repository.GetImportJobAsync(id);
        Assert.Equal("orte nightly", job!.Name);
        job.Enabled = false;
        await repository.UpdateImportJobAsync(job);
        Assert.False((await repository.GetImportJobsAsync()).Single().Enabled);

        var run1 = new ImportJobRunRecord { JobId = id, StartedUtc = DateTime.UtcNow.AddMinutes(-10), Status = ImportJobStatus.Succeeded, TriggeredBy = "job:x" };
        await repository.InsertImportJobRunAsync(run1);
        var run2 = new ImportJobRunRecord { JobId = id, StartedUtc = DateTime.UtcNow, Status = ImportJobStatus.Running, TriggeredBy = "alice" };
        await repository.InsertImportJobRunAsync(run2);

        Assert.Equal(run2.Id, (await repository.GetLastImportJobRunsAsync())[id].Id);
        Assert.Equal(1, await repository.MarkRunningImportJobRunsInterruptedAsync());
        Assert.Equal(ImportJobStatus.Interrupted, (await repository.GetImportJobRunsAsync(id))[0].Status);

        run1.Imported = 42;
        run1.FinishedUtc = DateTime.UtcNow;
        await repository.UpdateImportJobRunAsync(run1);
        Assert.Equal(42, (await repository.GetImportJobRunsAsync(id))[1].Imported);

        await repository.DeleteImportJobAsync(id);
        Assert.Empty(await repository.GetImportJobsAsync());
        Assert.Empty(await repository.GetImportJobRunsAsync(id));
    }

    [Fact]
    public void Validate()
    {
        Assert.Null(ImportJobRunner.Validate(ValidJob()));

        Assert.NotNull(ImportJobRunner.Validate(ValidJob().With(j => j.Name = " ")));
        Assert.NotNull(ImportJobRunner.Validate(ValidJob().With(j => j.Target = "Bad Name")));
        Assert.NotNull(ImportJobRunner.Validate(ValidJob().With(j => j.Target = new string('a', 50))));          // too long for generations
        Assert.Null(ImportJobRunner.Validate(ValidJob().With(j => { j.Target = new string('a', 50); j.Mode = ImportJobModes.Clear; })));
        Assert.NotNull(ImportJobRunner.Validate(ValidJob().With(j => j.Sql = "DELETE FROM orte")));
        Assert.NotNull(ImportJobRunner.Validate(ValidJob().With(j => j.Cron = "not a cron")));
        Assert.NotNull(ImportJobRunner.Validate(ValidJob().With(j => j.TimeZone = "Nowhere/City")));
        Assert.NotNull(ImportJobRunner.Validate(ValidJob().With(j => j.Mode = "other")));
    }

    [Fact]
    public void Generations()
    {
        var name = ImportJobRunner.GenerationName("orte", new DateTime(2026, 11, 7, 3, 0, 5, DateTimeKind.Utc));
        Assert.Equal("orte_20261107030005", name);
        Assert.True(ImportJobRunner.IsGenerationOf(name, "orte"));
        Assert.False(ImportJobRunner.IsGenerationOf("orte_backup", "orte"));
        Assert.False(ImportJobRunner.IsGenerationOf("orte2_20261107030005", "orte"));
        Assert.False(ImportJobRunner.IsGenerationOf("orte", "orte"));
    }

    [Fact]
    public void Schedule_IsDue_And_NextOccurrences()
    {
        var job = ValidJob();   // 03:00 UTC
        var day = new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(ImportJobScheduler.IsDue(job, day.AddHours(3).AddSeconds(-20), day.AddHours(3).AddSeconds(10)));
        Assert.False(ImportJobScheduler.IsDue(job, day.AddHours(3).AddSeconds(10), day.AddHours(3).AddSeconds(40)));   // already passed
        Assert.False(ImportJobScheduler.IsDue(job.With(j => j.Cron = "invalid"), day, day.AddDays(2)));

        Assert.Equal([day.AddHours(3), day.AddDays(1).AddHours(3)], ImportJobRunner.NextOccurrences(job.Cron, null, day, 2));

        // time zone: 03:00 Vienna (CET, UTC+1 in November) = 02:00 UTC
        var vienna = TimeZoneInfo.GetSystemTimeZones().Any(z => z.Id == "Europe/Vienna") ? "Europe/Vienna" : "W. Europe Standard Time";
        Assert.Equal(day.AddHours(2), ImportJobRunner.NextOccurrences(job.Cron, vienna, day, 1).Single());
    }

    [Fact]
    public void JobMapping_JsonRoundtrip()
    {
        var mapping = new ImportJobMapping
        {
            Phonetics = LuceneServerNET.Core.Phonetics.Algorithm.ColognePhonetics,
            Mapping = new InferredMapping
            {
                Fields = [new InferredField { Column = "Name", Name = "name", FieldType = FieldTypes.TextType, Primary = true }],
                GeoFields = [GeoFieldMapping.ForGeometry("geom", "geom", 31256)]
            }
        };

        var copy = ImportJobMapping.FromJson(mapping.ToJson());

        Assert.Equal(LuceneServerNET.Core.Phonetics.Algorithm.ColognePhonetics, copy.Phonetics);
        Assert.Equal("Name", copy.Mapping.Fields.Single().Column);
        Assert.Equal((31256, "geom_bbox"), (copy.Mapping.GeoFields.Single().Epsg, copy.Mapping.GeoFields.Single().BBoxFieldName));
    }

    /// <summary>Fake server with indices and aliases; records removed indices.</summary>
    private class GenerationServer : HttpMessageHandler
    {
        public List<string> Removed { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string json = path switch
            {
                "/api/v2/aliases" => """{"success":true,"aliases":{"orte":"orte_20261107030000","pinned":"orte_20261101030000"}}""",
                "/api/v2/indices" => """{"success":true,"result":["orte","orte_backup","orte_20261101030000","orte_20261103030000","orte_20261104030000","orte_20261105030000","orte_20261106030000","orte_20261107030000","other_20261105030000"]}""",
                _ => """{"success":true}"""
            };
            if (request.Method == HttpMethod.Delete) Removed.Add(path.Split('/').Last());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    [Theory]
    [InlineData(1, new[] { "orte_20261105030000", "orte_20261104030000", "orte_20261103030000" })]
    [InlineData(0, new[] { "orte_20261106030000", "orte_20261105030000", "orte_20261104030000", "orte_20261103030000" })]
    [InlineData(5, new string[0])]
    public async Task RemoveOldGenerations_KeepsNewestAndAliased(int keep, string[] expectedRemoved)
    {
        var server = new GenerationServer();
        var client = new LuceneServerV2Client("http://fake", httpClient: new HttpClient(server));

        var removed = await ImportJobRunner.RemoveOldGenerationsAsync(client, "orte", "orte_20261107030000", keep);

        // current + aliased ("pinned") + non-generations are never removed
        Assert.Equal(expectedRemoved, removed);
        Assert.Equal(expectedRemoved, server.Removed);
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }
}

internal static class RecordExtensions
{
    /// <summary>Copy of a job with a change (ImportJobRecord is a class).</summary>
    public static ImportJobRecord With(this ImportJobRecord job, Action<ImportJobRecord> change)
    {
        var copy = new ImportJobRecord
        {
            Name = job.Name, Instance = job.Instance, ConnectionId = job.ConnectionId, Sql = job.Sql, Target = job.Target,
            Mode = job.Mode, MappingJson = job.MappingJson, BatchSize = job.BatchSize, MaxErrors = job.MaxErrors,
            KeepGenerations = job.KeepGenerations, Cron = job.Cron, TimeZone = job.TimeZone, Enabled = job.Enabled
        };
        change(copy);
        return copy;
    }
}
