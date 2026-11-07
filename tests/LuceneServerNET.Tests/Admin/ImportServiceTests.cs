using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Mapping;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LuceneServerNET.Tests.Admin;

public class ImportServiceTests
{
    /// <summary>Fake server: records the document batches, rejects batches that contain a document with title "bad".</summary>
    private class FakeServer : HttpMessageHandler
    {
        public List<int> BatchSizes { get; } = [];
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");

            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/documents"))
            {
                var documents = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
                BatchSizes.Add(documents.GetArrayLength());

                if (documents.EnumerateArray().Any(d => d.TryGetProperty("title", out var t) && t.GetString() == "bad"))
                {
                    return Json(HttpStatusCode.BadRequest, """{"success":false,"message":"bad document"}""");
                }
            }

            return Json(HttpStatusCode.OK, """{"success":true}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
            => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static InferredMapping Mapping() => new()
    {
        Fields =
        [
            new InferredField { Column = "Title", Name = "title", FieldType = FieldTypes.TextType },
            new InferredField { Column = "Pop", Name = "pop", FieldType = FieldTypes.Int32Type },
        ]
    };

    private static async IAsyncEnumerable<IDictionary<string, object>> Rows(IEnumerable<(string Title, string Pop)> rows)
    {
        foreach (var (title, pop) in rows)
        {
            await Task.Yield();
            yield return new Dictionary<string, object> { ["Title"] = title, ["Pop"] = pop };
        }
    }

    private static (ImportService, LuceneServerV2Client, FakeServer) Create()
    {
        var server = new FakeServer();
        var client = new LuceneServerV2Client("http://fake", httpClient: new HttpClient(server));
        return (new ImportService(NullLogger<ImportService>.Instance), client, server);
    }

    [Fact]
    public async Task Import_UsesBatchSize_AndRefreshesOnce()
    {
        var (service, client, server) = Create();
        var rows = Enumerable.Range(0, 25).Select(i => ($"t{i}", i.ToString()));

        var report = await service.ImportAsync(client, "idx", Rows(rows), Mapping(), new ImportOptions { BatchSize = 10 });

        Assert.Equal(25, report.Read);
        Assert.Equal(25, report.Imported);
        Assert.Empty(report.Errors);
        Assert.Equal([10, 10, 5], server.BatchSizes);
        Assert.Single(server.Requests, r => r == "POST /api/v2/indices/idx/refresh");
    }

    [Fact]
    public async Task Import_SkipsRowsThatDoNotMatchTheMapping()
    {
        var (service, client, _) = Create();

        var report = await service.ImportAsync(client, "idx", Rows([("a", "1"), ("b", "not a number"), ("c", "")]), Mapping(), new ImportOptions());

        Assert.Equal(3, report.Read);
        Assert.Equal(2, report.Imported);
        var error = Assert.Single(report.Errors);
        Assert.Equal(2, error.Row);
        Assert.Contains("Pop", error.Message);
    }

    [Fact]
    public async Task Import_RejectedBatch_IsRetriedDocumentByDocument()
    {
        var (service, client, server) = Create();

        var report = await service.ImportAsync(client, "idx", Rows([("a", "1"), ("bad", "2"), ("c", "3")]), Mapping(), new ImportOptions { BatchSize = 3 });

        Assert.Equal(2, report.Imported);
        Assert.Equal(2, Assert.Single(report.Errors).Row);
        Assert.Equal([3, 1, 1, 1], server.BatchSizes);
    }

    [Fact]
    public async Task Import_StopsAfterMaxErrors()
    {
        var (service, client, _) = Create();
        var rows = Enumerable.Range(0, 100).Select(i => ($"t{i}", "x"));

        var report = await service.ImportAsync(client, "idx", Rows(rows), Mapping(), new ImportOptions { MaxErrors = 5 });

        Assert.True(report.StoppedBecauseOfErrors);
        Assert.Equal(5, report.Errors.Count);
        Assert.Equal(5, report.Read);
    }

    [Fact]
    public async Task Import_ClearIndexFirst_DeletesAllDocuments()
    {
        var (service, client, server) = Create();

        await service.ImportAsync(client, "idx", Rows([("a", "1")]), Mapping(), new ImportOptions { ClearIndexFirst = true });

        Assert.Equal("DELETE /api/v2/indices/idx/documents", server.Requests[0]);
    }

    [Fact]
    public async Task Import_Cancelled_IsReported()
    {
        var (service, client, _) = Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var report = await service.ImportAsync(client, "idx", Rows([("a", "1")]), Mapping(), new ImportOptions(), cancellationToken: cancellation.Token);

        Assert.True(report.Cancelled);
        Assert.Equal(0, report.Imported);
    }

    [Fact]
    public void TryConvert_CombinesGeoField()
    {
        var mapping = new InferredMapping { Geo = new GeoCombination("lat", "lon", "location") };

        Assert.True(ImportService.TryConvert(new Dictionary<string, object> { ["lat"] = "47.5", ["lon"] = 15.25 }, mapping, out var document, out _));
        Assert.Equal("15.25 47.5", document["location"]);
    }

    [Fact]
    public void FromExistingMapping_AssignsColumnsByName()
    {
        var existing = new IndexMapping
        {
            Fields = new List<FieldMapping> { new("Title"), new("pop", FieldTypes.Int32Type) },
            PrimaryFields = new List<string> { "Title" }
        };

        var mapping = ImportService.FromExistingMapping(existing, ["title", "POP", "other"]);

        Assert.Equal("Title", mapping.Fields[0].Name);
        Assert.True(mapping.Fields[0].Primary);
        Assert.Equal(FieldTypes.Int32Type, mapping.Fields[1].FieldType);
        Assert.True(mapping.Fields[2].Skip);
    }
}
