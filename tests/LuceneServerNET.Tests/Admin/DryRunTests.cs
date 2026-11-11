#nullable enable
using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Client;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuceneServerNET.Tests.Admin;

public class DryRunTests
{
    private class NoServer : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("the dry run must not call the server");
        }
    }

    private static async IAsyncEnumerable<IDictionary<string, object?>> Rows(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.Delay(i % 100 == 0 ? 5 : 0);   // a slow source
            yield return new Dictionary<string, object?> { ["id"] = (long)i, ["name"] = $"name {i}" };
        }
    }

    [Fact]
    public async Task DryRun_SendsNothing_MeasuresTimes()
    {
        var server = new NoServer();
        var client = new LuceneServerV2Client("http://server", httpClient: new HttpClient(server));
        var mapping = MappingInference.Infer([new Dictionary<string, object?> { ["id"] = 1L, ["name"] = "a" }]);

        var report = await new ImportService(NullLogger<ImportService>.Instance).ImportAsync(client, "idx", Rows(1000), mapping,
            new ImportOptions { BatchSize = 100, DryRun = true, ClearIndexFirst = true, RefreshAtEnd = true });

        Assert.Null(report.FatalError);
        Assert.Equal(0, server.Calls);
        Assert.Equal((1000L, 1000L), (report.Read, report.Imported));
        Assert.True(report.ReadTime > TimeSpan.Zero);
        Assert.True(report.ReadTime + report.ConvertTime + report.SendTime <= report.Duration);
        Assert.Equal("read 1.0 s · convert 0.5 s", ImportReport.FormatTimes(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(3), dryRun: true));
    }
}
