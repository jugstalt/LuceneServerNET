#nullable enable
using LuceneServerNET.Admin.Services.Jobs;
using LuceneServerNET.Admin.Services.Repository;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;

namespace LuceneServerNET.Tests.Admin;

public class JobWebhookTests
{
    private class FakeHandler : HttpMessageHandler
    {
        public Queue<HttpStatusCode> Responses { get; } = new();
        public List<(string Url, string Body, string? Authorization)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.RequestUri!.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken),
                       request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null));
            return new HttpResponseMessage(Responses.Count > 0 ? Responses.Dequeue() : HttpStatusCode.OK);
        }
    }

    private class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (JobWebhook Webhook, FakeHandler Handler) Create(string? publicUrl = null)
    {
        var handler = new FakeHandler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:PublicUrl"] = publicUrl }).Build();
        var webhook = new JobWebhook(new Factory(handler), new EphemeralDataProtectionProvider(), config, NullLogger<JobWebhook>.Instance)
        {
            RetryDelay = TimeSpan.Zero
        };
        return (webhook, handler);
    }

    private static ImportJobRecord Job(string on = WebhookTriggers.Failure) => new()
    {
        Id = 7, Name = "orte", Instance = "prod", Target = "orte", Mode = ImportJobModes.ReplaceAlias,
        WebhookUrl = "https://hooks.example.test/x", WebhookOn = on
    };

    private static ImportJobRunRecord Run(string status) => new()
    {
        Id = 3, JobId = 7, Status = status, TriggeredBy = "job:orte", IndexName = "orte_20261107030000",
        StartedUtc = new DateTime(2026, 11, 7, 3, 0, 0, DateTimeKind.Utc), FinishedUtc = new DateTime(2026, 11, 7, 3, 0, 12, 500, DateTimeKind.Utc),
        ReadCount = 10, Imported = 9, Failed = 1, Message = "1 rows skipped"
    };

    [Theory]
    [InlineData(WebhookTriggers.Failure, ImportJobStatus.Succeeded, false)]
    [InlineData(WebhookTriggers.Failure, ImportJobStatus.Warning, false)]
    [InlineData(WebhookTriggers.Failure, ImportJobStatus.Failed, true)]
    [InlineData(WebhookTriggers.Failure, ImportJobStatus.Cancelled, true)]
    [InlineData(WebhookTriggers.Always, ImportJobStatus.Succeeded, true)]
    public void ShouldCall(string on, string status, bool expected)
    {
        Assert.Equal(expected, JobWebhook.ShouldCall(Job(on), status));
        Assert.False(JobWebhook.ShouldCall(Job(on).With(j => j.WebhookUrl = null), ImportJobStatus.Failed));
    }

    [Fact]
    public void Validation()
    {
        Assert.Null(JobWebhook.ValidateUrl(null));
        Assert.Null(JobWebhook.ValidateUrl("https://x.test/hook"));
        Assert.NotNull(JobWebhook.ValidateUrl("ftp://x.test"));
        Assert.NotNull(JobWebhook.ValidateUrl("no url"));

        Assert.Null(JobWebhook.ValidateHeader("Authorization: Bearer abc"));
        Assert.Equal(("X-Key", "a:b"), JobWebhook.ParseHeader("X-Key: a:b"));
        Assert.NotNull(JobWebhook.ValidateHeader("no colon"));
        Assert.NotNull(JobWebhook.ValidateHeader("Bad Name: x"));
    }

    [Fact]
    public async Task Send_PostsPayloadWithHeader()
    {
        var (webhook, handler) = Create("https://admin.example.test/");
        var job = Job();
        job.WebhookHeaderProtected = webhook.ProtectHeader("Authorization: Bearer secret");

        var result = await webhook.SendAsync(job, webhook.CreatePayload(job, Run(ImportJobStatus.Failed)));

        Assert.Equal("webhook 200", result);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://hooks.example.test/x", call.Url);
        Assert.Equal("Bearer secret", call.Authorization);

        var json = JsonDocument.Parse(call.Body).RootElement;
        Assert.Equal("failed", json.GetProperty("status").GetString());
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(9, json.GetProperty("imported").GetInt64());
        Assert.Equal(12.5, json.GetProperty("durationSeconds").GetDouble());
        Assert.Equal("https://admin.example.test/jobs/7", json.GetProperty("url").GetString());
        Assert.StartsWith("❌ Import job orte: failed · prod/orte_20261107030000 · read 10, imported 9, failed 1 · 12.5 s · 1 rows skipped",
                          json.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Send_RetriesThreeTimes()
    {
        var (webhook, handler) = Create();
        handler.Responses.Enqueue(HttpStatusCode.InternalServerError);
        handler.Responses.Enqueue(HttpStatusCode.OK);

        Assert.Equal("webhook 200 (attempt 2)", await webhook.SendAsync(Job(), webhook.CreatePayload(Job(), Run(ImportJobStatus.Failed))));

        handler.Calls.Clear();
        for (int i = 0; i < 3; i++) handler.Responses.Enqueue(HttpStatusCode.BadGateway);
        var result = await webhook.SendAsync(Job(), webhook.CreatePayload(Job(), Run(ImportJobStatus.Failed)));
        Assert.StartsWith("webhook failed after 3 attempts: HTTP 502", result);
        Assert.Equal(3, handler.Calls.Count);
        Assert.Null(webhook.CreatePayload(Job(), Run(ImportJobStatus.Failed)).Url);   // no Admin:PublicUrl
    }
}
