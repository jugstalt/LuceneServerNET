using LuceneServerNET.Admin.Components;
using LuceneServerNET.Admin.Services.Audit;
using LuceneServerNET.Admin.Services.Jobs;
using LuceneServerNET.Admin.Services.Auth;
using LuceneServerNET.Admin.Services.Import;
using LuceneServerNET.Admin.Services.Instances;
using LuceneServerNET.Admin.Services.Repository;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting.WindowsServices;
using MudBlazor.Services;

// LuceneServerNET.Admin --hash-password <password>  => PasswordHash for Admin:Authentication:Users
if (args.Length == 2 && args[0] == "--hash-password")
{
    Console.WriteLine(SecretHasher.Hash(args[1]));
    return;
}

// as Windows service the working directory is system32: use the application folder as content root
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null
});
var configuration = builder.Configuration;

// optional configuration file outside the application folder (survives updates, see lucene-admin-service.bat):
// LUCENE_ADMIN_CONFIG=<path>\admin.json; it overrides appsettings*.json, environment variables and arguments override it
if (Environment.GetEnvironmentVariable("LUCENE_ADMIN_CONFIG") is { Length: > 0 } adminConfig)
{
    var lastJson = configuration.Sources.Select((source, index) => (source, index))
                                        .LastOrDefault(s => s.source is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource).index;
    var source = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource { Path = Path.GetFullPath(adminConfig), Optional = true };
    source.ResolveFileProvider();   // absolute path => file provider of its folder
    configuration.Sources.Insert(lastJson + 1, source);
}

builder.Services.AddWindowsService(options => options.ServiceName = "LuceneServer.NET Admin");
if (OperatingSystem.IsWindows())
{
    // event log source, created by lucene-admin-service.bat
    builder.Services.Configure<Microsoft.Extensions.Logging.EventLog.EventLogSettings>(settings => settings.SourceName = "LuceneServer.NET Admin");
}

#region Data path (SQLite database + data protection keys)

var dataPath = Path.GetFullPath(configuration["Admin:DataPath"] ?? "_data", builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataPath);

builder.Services.AddDataProtection()
    .SetApplicationName("LuceneServerNET.Admin")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));

var connectionString = configuration["Admin:Database:ConnectionString"] ?? $"Data Source={Path.Combine(dataPath, "admin.db")}";

#endregion

#region Services

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddAdminAuthentication(configuration);

builder.Services.AddHttpClient(InstanceRegistry.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(100));

builder.Services.AddSingleton<IAdminRepositoryService>(sp =>
    new SqliteAdminRepositoryService(connectionString, sp.GetRequiredService<ILogger<SqliteAdminRepositoryService>>()));
builder.Services.AddSingleton<InstanceRegistry>();
builder.Services.AddSingleton<ImportService>();
builder.Services.AddSingleton<CopyService>();
builder.Services.AddSingleton<DatabaseSource>();
builder.Services.AddSingleton<AuditLogger>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddHostedService<AuditCleanupService>();
builder.Services.AddHttpClient(JobWebhook.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<JobWebhook>();
builder.Services.AddSingleton<ImportJobRunner>();
builder.Services.AddHostedService<ImportJobScheduler>();

// file uploads (import) are streamed through the SignalR circuit
builder.Services.AddServerSideBlazor().AddHubOptions(options => options.MaximumReceiveMessageSize = 1024 * 1024);

#endregion

var app = builder.Build();

configuration.LogAuthenticationWarnings(app.Logger);
await app.Services.GetRequiredService<IAdminRepositoryService>().InitializeAsync();

#region Pipeline

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapDefaultEndpoints();
app.MapStaticAssets().AllowAnonymous();
app.MapAccountEndpoints();
app.MapBackupEndpoints();
app.MapJobTriggerEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

#endregion

app.Run();
