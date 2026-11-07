// Local development host: starts LuceneServer.NET with the Aspire dashboard (logs, traces, metrics).
//   dotnet run --project src/aspire/LuceneServerNET.AppHost
// The index data lives in artifacts/aspire-data next to the repository root (not in the source tree).

var builder = DistributedApplication.CreateBuilder(args);

var dataRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "..", "artifacts", "aspire-data"));

var luceneServer = builder.AddProject<Projects.LuceneServerNET>("luceneserver")
    .WithEnvironment("LuceneServer__RootPath", Path.Combine(dataRoot, "indices"))
    .WithEnvironment("LuceneServer__ArchivePath", Path.Combine(dataRoot, "archive"))
    .WithHttpHealthCheck("/health");

// Further web apps (e.g. an admin UI) are added here and get the server address via service discovery:
//
// builder.AddProject<Projects.LuceneServerNET_Admin>("admin")
//        .WithReference(luceneServer)
//        .WaitFor(luceneServer);

builder.Build().Run();
