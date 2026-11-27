# Operations

## Health endpoints

| Endpoint | Checks | Use |
|---|---|---|
| `/health` | the process answers **and** `RootPath` (and `ArchivePath`, if set) exists and is writable | readiness probe, load balancer, Docker `HEALTHCHECK` |
| `/alive` | the process answers | liveness probe |

Both answer `200 Healthy` or `503 Unhealthy` as plain text, without details (no paths, no exception
messages). They need **no authentication**, also with `Authorization:Type = basic`: probes of Docker,
Kubernetes or a load balancer send no credentials.

## Metrics and traces (OpenTelemetry)

The server records logs, metrics and traces with OpenTelemetry. They are exported via OTLP as soon as
`OTEL_EXPORTER_OTLP_ENDPOINT` is set (e.g. an OpenTelemetry Collector, the Aspire dashboard,
Grafana Alloy):

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_SERVICE_NAME=luceneserver
```

Besides the ASP.NET Core and .NET runtime metrics (e.g. `http.server.request.duration` per route) the
meter `LuceneServerNET` provides:

| Metric | Type | Tags |
|---|---|---|
| `luceneserver.search.duration` | histogram (s) | `index`, `operation` (`search`, `phonetic`, `groupby`) |
| `luceneserver.search.requests` | counter | `index`, `operation`, `outcome` (`success`, `error`) |
| `luceneserver.documents.indexed` | counter | `index` |

## Scaling

!!! danger "One writing instance per index"
    A Lucene index may only be written by one process at a time (`write.lock`). Run exactly one
    server per index directory; don't share an index directory between several instances, containers or
    replicas.

## Local development with Aspire

`src/aspire/LuceneServerNET.AppHost` starts the server together with the Aspire dashboard (logs,
traces, metrics); the index data goes to `artifacts/aspire-data`:

```bash
dotnet run --project src/aspire/LuceneServerNET.AppHost
```

Further web apps (e.g. an admin UI) are added to the AppHost and reference the server.

## Build and deploy (NUKE)

| Command | Result |
|---|---|
| `./build.cmd` | build and test |
| `./build.cmd PrintVersion` | the version (MinVer, from the git tags `v*`) |
| `./build.cmd Test Pack Zip` | NuGet packages in `artifacts/packages`, ZIP files `artifacts/zip/luceneserver_net_{version}_{win-x64\|linux-x64}.zip` (framework dependent) |
| `./build.cmd DockerBuild [--container-cli wslc\|podman]` | images `ghcr.io/jugstalt/luceneserver-net` and `ghcr.io/jugstalt/luceneserver-net-admin` (`{version}`, `{major.minor}`, `latest`) |
| `./build.cmd Docs` | this documentation in `artifacts/docs/site` |
| `./build.cmd DocsPublish [--docs-repository <path>]` | copies the site to `docs-gviewonline/app/other/luceneserver-net` (nothing is committed) |

The version comes from the git tags (`v3.0.0`, `v3.1.0-preview.1`, ...). Pushing a tag runs the GitHub workflow
`release.yml`: it publishes the NuGet packages (`LuceneServerNET.Core`, `LuceneServerNET.Client`), pushes the images to
`ghcr.io/jugstalt` and creates the GitHub release with the ZIP files. The release notes are the section of the version
in `CHANGELOG.md`.

