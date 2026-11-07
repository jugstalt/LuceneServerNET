using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LuceneServerNET.IntegrationTests;

/// <summary>
/// Hosts the real server in memory. Every factory instance uses its own temporary RootPath,
/// which is deleted on dispose. Authentication is disabled unless configured via <see cref="ExtraSettings"/>.
/// </summary>
public class LuceneServerFactory : WebApplicationFactory<Program>
{
    public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "luceneservernet-tests", Guid.NewGuid().ToString("N"));

    protected virtual IDictionary<string, string> ExtraSettings => new Dictionary<string, string>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string>
            {
                ["LuceneServer:RootPath"] = RootPath,
                ["LuceneServer:ArchivePath"] = "",
                ["LuceneServer:AutoRestoreOnStartup"] = "false",
                ["useSwagger"] = "false",
                ["Authorization:Type"] = "none",
            };
            foreach (var kv in ExtraSettings)
            {
                settings[kv.Key] = kv.Value;
            }
            config.AddInMemoryCollection(settings);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch { /* best effort: Lucene may still hold file handles */ }
    }
}
