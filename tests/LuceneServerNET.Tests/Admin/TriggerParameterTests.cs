#nullable enable
using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Admin.Services.Jobs;
using LuceneServerNET.Admin.Services.Repository;
using System.Text.Json;

namespace LuceneServerNET.Tests.Admin;

public class TriggerParameterTests
{
    [Theory]
    [InlineData("select * from a where OID=@oid", new[] { "oid" })]
    [InlineData("select * from a where GEMEINDE = @Gem and typ = @typ and x = @gem", new[] { "gem", "typ" })]
    [InlineData("select '@notaparam' as a, @@ROWCOUNT as b -- @comment\n from t /* @x */ where id=@id", new[] { "id" })]
    [InlineData("select * from a where mail = 'a@b.at'", new string[0])]
    public void Names(string sql, string[] expected) => Assert.Equal(expected, QueryParameters.Names(sql));

    [Fact]
    public void FromJson_Types()
    {
        var values = QueryParameters.FromJson(JsonDocument.Parse("""{"oid": 4711, "x": 1.5, "gem": "60101", "b": true, "n": null, "@p": 1}""").RootElement);
        Assert.Equal(4711L, values["OID"]);
        Assert.Equal(1.5, values["x"]);
        Assert.Equal("60101", values["gem"]);
        Assert.Equal(true, values["b"]);
        Assert.Null(values["n"]);
        Assert.Equal(1L, values["p"]);
        Assert.Throws<FormatException>(() => QueryParameters.FromJson(JsonDocument.Parse("""{"a": [1]}""").RootElement));
        Assert.Throws<FormatException>(() => QueryParameters.FromJson(JsonDocument.Parse("[1]").RootElement));

        Assert.Equal(["oid"], QueryParameters.Missing("select * from a where g=@gem and o=@oid", new Dictionary<string, object?> { ["GEM"] = 1L }));
        Assert.Equal("b=True&gem=60101", QueryParameters.Key(new Dictionary<string, object?> { ["gem"] = "60101", ["B"] = true }));
        Assert.Equal(12L, QueryParameters.FromText(" 12 "));
        Assert.Equal("Graz", QueryParameters.FromText("Graz"));
    }

    [Fact]
    public async Task Sqlite_QueryWithParameters()
    {
        var file = Path.Combine(Path.GetTempPath(), $"params-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE t (oid INTEGER, gem TEXT); INSERT INTO t VALUES (1, 'a'), (2, 'b'), (3, 'b');";
                await command.ExecuteNonQueryAsync();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            async Task<List<long>> Read(string sql, Dictionary<string, object?>? p)
            {
                var result = new List<long>();
                await foreach (var row in DatabaseSource.ReadAsync(DbProviders.Sqlite, $"Data Source={file}", sql, parameters: p)) result.Add((long)row["oid"]!);
                return result;
            }

            Assert.Equal([2L, 3L], await Read("select oid from t where gem = @gem order by oid", new() { ["gem"] = "b" }));
            Assert.Equal([1L], await Read("select oid from t where oid = @oid", new() { ["OID"] = 1L }));
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => Read("select oid from t where oid = @oid", null));
            Assert.Contains("@oid", ex.Message);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(file);
        }
    }

    private static (ImportJobRecord Job, ImportJobMapping Settings) AppendJob()
    {
        var adressen = new ImportJobSource { Name = "Adressen", ConnectionId = 1, Sql = "select * from adressen where OID = @oid", DeleteTemplate = "{@metaid}.{@oid}" };
        var gemeinde = new ImportJobSource { Name = "Gemeinde", ConnectionId = 1, Sql = "select * from adressen where GEMEINDE = @gem" };
        var strassen = new ImportJobSource { Name = "Strassen", ConnectionId = 1, Sql = "select * from strassen where OID = @oid" };
        var settings = new ImportJobMapping { KeyField = "id", Sources = [adressen, gemeinde, strassen] };
        return (new ImportJobRecord { Id = 3, Name = "edit", Mode = ImportJobModes.Append, Target = "adressen", Cron = "" }, settings);
    }

    private static List<JobTriggerQueueRecord> Queue(string body, ImportJobRecord? job = null)
    {
        var (defaultJob, settings) = AppendJob();
        return JobTriggerRequest.ToQueue(JsonDocument.Parse(body).RootElement, job ?? defaultJob, settings, "n", DateTime.UtcNow);
    }

    [Fact]
    public void TriggerBody_ToQueue()
    {
        Assert.Empty(Queue("""{"note": "full run"}"""));

        var single = Assert.Single(Queue("""{"params": {"oid": 4711}}"""));
        Assert.Equal((JobTriggerKinds.Params, (string?)null, 3L, "n"), (single.Kind, single.Source, single.JobId, single.Note));

        Assert.Equal(3, Queue("""{"params": [{"oid": 1}, {"oid": 2}], "delete": {"oid": 5}}""").Count);
        Assert.Equal("strassen", Queue("""{"source": "strassen", "params": {"oid": 1}}""").Single().Source);
        Assert.Single(Queue("""{"params": {"gem": 60101}}"""));

        Assert.Contains("No source queries", Assert.Throws<FormatException>(() => Queue("""{"params": {"x": 1}}""")).Message);
        Assert.Contains("No source deletes", Assert.Throws<FormatException>(() => Queue("""{"delete": {"gem": 1}}""")).Message);
        Assert.Contains("Unknown source", Assert.Throws<FormatException>(() => Queue("""{"source": "x", "params": {"oid": 1}}""")).Message);
        Assert.Contains("append or update", Assert.Throws<FormatException>(() => Queue("""{"params": {"oid": 1}}""",
            new ImportJobRecord { Id = 3, Mode = ImportJobModes.ReplaceAlias })).Message);
    }

    [Fact]
    public void Source_Accepts_And_DeleteKey()
    {
        var (_, settings) = AppendJob();
        var adressen = settings.Sources[0];
        Assert.True(adressen.Accepts(["OID"], null, delete: false));
        Assert.True(adressen.Accepts(["oid", "other"], null, delete: false));
        Assert.False(adressen.Accepts(["gem"], null, delete: false));
        Assert.False(adressen.Accepts(["oid"], "Strassen", delete: false));
        Assert.True(adressen.Accepts(["oid"], null, delete: true));
        Assert.False(settings.Sources[1].Accepts(["gem"], null, delete: true));     // no delete template

        var key = FieldTemplate.Render(adressen.DeleteTemplate!, new Dictionary<string, object?>(), adressen.VariablesWith(new Dictionary<string, object?> { ["oid"] = 5L }));
        Assert.Equal($"{adressen.MetaId}.5", key);
    }

    [Fact]
    public void Validate_ParametersAndKey()
    {
        var (job, settings) = AppendJob();
        foreach (var source in settings.Sources)
        {
            source.Mapping = MappingInference.Infer([new Dictionary<string, object?> { ["OID"] = 1L, ["NAME"] = "a b c d e f g h i j" }]);
            source.Mapping.Fields.Insert(0, new InferredField { Name = "id", Template = "{@metaid}.{OID}", FieldType = LuceneServerNET.Core.Models.Mapping.FieldTypes.StringType });
        }
        Assert.Null(ImportJobRunner.ValidateSources(settings, job));

        job.Cron = "0 3 * * *";
        Assert.Contains("remove the schedule", ImportJobRunner.ValidateSources(settings, job));
        job.Cron = "";
        job.Mode = ImportJobModes.Clear;
        Assert.Contains("append or update", ImportJobRunner.ValidateSources(settings, job));
        job.Mode = ImportJobModes.Append;

        settings.KeyField = "OID";      // int32 field
        Assert.Contains("indexed string field", ImportJobRunner.ValidateSources(settings, job));
        settings.KeyField = null;
        Assert.Contains("needs a key field", ImportJobRunner.ValidateSources(settings, job));
    }

    [Fact]
    public void QueueProcessor_DueAndRebuild()
    {
        var now = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
        Assert.False(JobQueueProcessor.IsDue(new JobTriggerQueueState { OldestUtc = now.AddSeconds(-5), NewestUtc = now.AddSeconds(-1) }, now));   // still collecting
        Assert.True(JobQueueProcessor.IsDue(new JobTriggerQueueState { OldestUtc = now.AddSeconds(-5), NewestUtc = now.AddSeconds(-3) }, now));
        Assert.True(JobQueueProcessor.IsDue(new JobTriggerQueueState { OldestUtc = now.AddSeconds(-40), NewestUtc = now }, now));               // max wait

        var update = new ImportJobRecord { Id = 1, Mode = ImportJobModes.Append, Instance = "prod", Target = "adressen" };
        var rebuild = new ImportJobRecord { Id = 2, Mode = ImportJobModes.ReplaceAlias, Instance = "prod", Target = "adressen" };
        var other = new ImportJobRecord { Id = 3, Mode = ImportJobModes.ReplaceAlias, Instance = "prod", Target = "strassen" };
        Assert.True(JobQueueProcessor.IsBlockedByRebuild(update, [update, rebuild, other], id => id == 2));
        Assert.False(JobQueueProcessor.IsBlockedByRebuild(update, [update, rebuild, other], id => id == 3));
        Assert.False(JobQueueProcessor.IsBlockedByRebuild(update, [update, rebuild], _ => false));
    }
}
