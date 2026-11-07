using System.Net;
using System.Text;
using LuceneServerNET.Client;

namespace LuceneServerNET.Tests.Client;

public class ClientDisposeTests
{
    /// <summary>Answers every request with success and records method + path.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        public bool Fail { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // complete asynchronously, like a real network call
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);

            lock (Requests)
            {
                Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            }

            if (Fail)
            {
                throw new HttpRequestException("server down");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
            };
        }
    }

    private static (LuceneServerClient client, RecordingHandler handler) Create()
    {
        var handler = new RecordingHandler();
        return (new LuceneServerClient("http://localhost", "idx", new HttpClient(handler)), handler);
    }

    private static int Refreshes(RecordingHandler handler)
        => handler.Requests.Count(r => r == "POST /lucene/refresh/idx");

    [Fact]
    public void Dispose_WithoutChanges_DoesNotRefresh()
    {
        var (client, handler) = Create();
        client.Dispose();
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Dispose_AfterChange_RefreshesOnce()
    {
        var (client, handler) = Create();
        await client.RemoveDocumentsAsync("_guid", "x");

        client.Dispose();
        client.Dispose();  // idempotent

        Assert.Equal(1, Refreshes(handler));
    }

    [Fact]
    public async Task DisposeAsync_AfterChange_Refreshes()
    {
        var (client, handler) = Create();
        await client.RemoveDocumentsAsync("_guid", "x");

        await client.DisposeAsync();

        Assert.Equal(1, Refreshes(handler));
    }

    [Fact]
    public async Task Dispose_AfterExplicitRefresh_DoesNotRefreshAgain()
    {
        var (client, handler) = Create();
        await client.RemoveDocumentsAsync("_guid", "x");
        await client.RefreshIndex();

        await client.DisposeAsync();

        Assert.Equal(1, Refreshes(handler));
    }

    [Fact]
    public async Task Dispose_SwitchOff_DoesNotRefresh()
    {
        var (client, handler) = Create();
        client.RefreshChangedIndicesOnDispose = false;
        await client.RemoveDocumentsAsync("_guid", "x");

        await client.DisposeAsync();

        Assert.Equal(0, Refreshes(handler));
    }

    [Fact]
    public async Task Dispose_RefreshFails_DoesNotThrow()
    {
        var (client, handler) = Create();
        await client.RemoveDocumentsAsync("_guid", "x");
        handler.Fail = true;

        client.Dispose();

        Assert.Equal(1, Refreshes(handler));
    }

    [Fact]
    public async Task Dispose_InSingleThreadedSynchronizationContext_DoesNotDeadlock()
    {
        var (client, _) = Create();
        await client.RemoveDocumentsAsync("_guid", "x");

        // simulates a UI thread: a synchronization context whose posted work never runs
        // because the thread is blocked in Dispose (the classic sync-over-async deadlock)
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NeverPumpedContext());
            client.Dispose();
        });
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Dispose dead-locked");
    }

    private sealed class NeverPumpedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { /* never executed */ }
    }
}
