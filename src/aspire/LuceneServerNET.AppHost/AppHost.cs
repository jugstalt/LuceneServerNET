// Local development host: starts LuceneServer.NET with the Aspire dashboard (logs, traces, metrics).
//   dotnet run --project src/aspire/LuceneServerNET.AppHost
// The index data lives in artifacts/aspire-data next to the repository root (not in the source tree).

var builder = DistributedApplication.CreateBuilder(args);

var dataRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "..", "artifacts", "aspire-data"));

var luceneServer = builder.AddProject<Projects.LuceneServerNET>("luceneserver")
    .WithEnvironment("LuceneServer__RootPath", Path.Combine(dataRoot, "indices"))
    .WithHttpHealthCheck("/health");

// Admin UI (Blazor): gets the server address via service discovery.
// Login: user "admin", password from the parameter "admin-password" (default "admin", change it in the dashboard / user secrets).
var adminPassword = builder.AddParameter("admin-password", "admin", secret: true);

builder.AddProject<Projects.LuceneServerNET_Admin>("admin")
       .WithEnvironment("Admin__DataPath", Path.Combine(dataRoot, "admin"))
       .WithEnvironment("Admin__Authentication__Type", "Local")
       .WithEnvironment("Admin__Authentication__Users__0__Name", "admin")
       .WithEnvironment("Admin__Authentication__Users__0__Password", adminPassword)
       .WithEnvironment("Admin__Instances__0__Name", "aspire")
       .WithEnvironment("Admin__Instances__0__Url", "http://luceneserver")
       .WithReference(luceneServer)
       .WaitFor(luceneServer)
       .WithHttpHealthCheck("/health");

builder.Build().Run();
