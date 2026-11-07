using LuceneServerNET;
using LuceneServerNET.Authentication;
using LuceneServerNET.Engine.Services;
using LuceneServerNET.Extensions;
using LuceneServerNET.Extensions.DependencyInjection;
using LuceneServerNET.Services;
using LuceneServerNET.Services.Abstraction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

#region Hash secret => print and exit

int hashArg = Array.FindIndex(args, a => "--hash-secret".Equals(a, StringComparison.OrdinalIgnoreCase));
if (hashArg >= 0)
{
    if (hashArg + 1 >= args.Length)
    {
        Console.Error.WriteLine("Usage: LuceneServerNET --hash-secret <secret>");
        Environment.ExitCode = 1;
        return;
    }
    Console.WriteLine(SecretHasher.Hash(args[hashArg + 1]));
    return;
}

#endregion

#region First Start => init configuration

new Setup().TrySetup(args);

#endregion

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

configuration.AddJsonFile("_config/luceneserver.json", optional: true);

#region Expose Ports

List<string> urls = new List<string>();
for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i].ToLower())
    {
        case "-expose-http":
            urls.Add("http://localhost:" + int.Parse(args[++i]));
            break;
        case "-expose-https":
            urls.Add("https://localhost:" + int.Parse(args[++i]));
            break;
    }
}
if (urls.Count > 0)
{
    builder.WebHost.UseUrls(urls.ToArray());
}

#endregion

#region Request limits

// Max. request body size (e.g. for indexing documents). Default: 30 MB (Kestrel/IIS default)
long maxRequestBodySize = configuration.GetValue<long>("LuceneServer:MaxRequestBodySizeBytes", 30L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodySize);
builder.Services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(options => options.MaxRequestBodySize = maxRequestBodySize);

#endregion

#region Services

builder.Services.AddTransient<IAppVersionService, AppVersionService>();

builder.Services.AddLuceneService(options =>
{
    options.RootPath = configuration.GetStringValue("LuceneServer:RootPath");
    options.ArchivePath = configuration.GetStringValue("LuceneServer:ArchivePath");
});

builder.Services.AddRestoreService(options =>
{
    options.RestoreOnRestart = configuration.GetBoolValue("LuceneServer:AutoRestoreOnStartup");
    options.RestoreOnRestartCount = configuration.GetIntValue("LuceneServer:AutoRestoreOnStartupCount", 0);
    options.RestoreOnRestartSince = configuration.GetIntValue("LuceneServer:AutoRestoreOnStartupSinceSeconds", 86400);
});

builder.Services.AddLuceneServerAuthentication(configuration);

builder.Services.AddControllers();
builder.Services.AddLuceneServerOpenApi(configuration);

#endregion

var app = builder.Build();

#region Pipeline

configuration.LogAuthenticationWarnings(app.Services.GetRequiredService<ILogger<Program>>());

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.Services.GetRequiredService<RestoreService>().TryRestoreIndices();

if (configuration.GetValue("LuceneServer:UseHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

app.UseRouting();

if (configuration.IsBasicAuthenticationEnabled())
{
    app.UseAuthentication();
}
app.UseAuthorization();

app.MapControllers();

// OpenAPI documents + Scalar UI (config "useSwagger" kept for compatibility)
if (app.Environment.IsDevelopment() ||
    "true".Equals(configuration["useSwagger"], StringComparison.OrdinalIgnoreCase))
{
    app.MapLuceneServerOpenApi();

    if (!app.Environment.IsDevelopment())
    {
        app.Logger.LogWarning("OpenAPI/Scalar UI is enabled in a non-development environment (useSwagger=true).");
    }
}

#endregion

app.Run();

public partial class Program { }
