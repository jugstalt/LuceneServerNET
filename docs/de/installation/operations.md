# Betrieb

## Health-Endpoints

| Endpoint | Prüft | Verwendung |
|---|---|---|
| `/health` | der Prozess antwortet **und** `RootPath` (und `ArchivePath`, falls gesetzt) existiert und ist beschreibbar | Readiness-Probe, Load Balancer, Docker-`HEALTHCHECK` |
| `/alive` | der Prozess antwortet | Liveness-Probe |

Beide antworten mit `200 Healthy` oder `503 Unhealthy` als Text, ohne Details (keine Pfade, keine
Fehlermeldungen). Sie brauchen **keine Anmeldung**, auch nicht bei `Authorization:Type = basic`:
Probes von Docker, Kubernetes oder einem Load Balancer senden keine Zugangsdaten.

## Metriken und Traces (OpenTelemetry)

Der Server erfasst Logs, Metriken und Traces mit OpenTelemetry. Sie werden per OTLP exportiert, sobald
`OTEL_EXPORTER_OTLP_ENDPOINT` gesetzt ist (z. B. OpenTelemetry Collector, Aspire-Dashboard,
Grafana Alloy):

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_SERVICE_NAME=luceneserver
```

Neben den Metriken von ASP.NET Core und der .NET-Runtime (z. B. `http.server.request.duration` pro
Route) liefert der Meter `LuceneServerNET`:

| Metrik | Typ | Tags |
|---|---|---|
| `luceneserver.search.duration` | Histogramm (s) | `index`, `operation` (`search`, `phonetic`, `groupby`) |
| `luceneserver.search.requests` | Zähler | `index`, `operation`, `outcome` (`success`, `error`) |
| `luceneserver.documents.indexed` | Zähler | `index` |

## Skalierung

!!! danger "Nur eine schreibende Instanz pro Index"
    Ein Lucene-Index darf immer nur von einem Prozess geschrieben werden (`write.lock`). Genau einen
    Server pro Indexverzeichnis betreiben; ein Indexverzeichnis nicht zwischen mehreren Instanzen,
    Containern oder Replikas teilen.

## Lokale Entwicklung mit Aspire

`src/aspire/LuceneServerNET.AppHost` startet den Server zusammen mit dem Aspire-Dashboard (Logs,
Traces, Metriken); die Indexdaten liegen in `artifacts/aspire-data`:

```bash
dotnet run --project src/aspire/LuceneServerNET.AppHost
```

Weitere Web-Apps (z. B. eine Admin-Oberfläche) werden im AppHost ergänzt und referenzieren den Server.

## Build und Deploy (NUKE)

| Befehl | Ergebnis |
|---|---|
| `./build.cmd` | Tests, ZIP-Dateien und Docker-Image |
| `./build.cmd Zip [--platform win64\|linux64]` | `c:\deploy\luceneserver-net\download\luceneserver-net-{platform}-{version}.zip` (framework-abhängig) |
| `./build.cmd Docker` | Image `luceneserver-net:{version}` / `:latest` |
| `./build.cmd DockerPush` | Push nach `ghcr.io/jugstalt/luceneserver-net` |
| `./build.cmd Docs` | diese Dokumentation in `artifacts/docs/site` |
| `./build.cmd DocsPublish [--docs-repository <pfad>]` | kopiert die Seite nach `docs-gviewonline/app/other/luceneserver-net` (es wird nichts committet) |

Die Version ist die `<Version>` von `src/LuceneServerNET.Core`; `--version` überschreibt sie.
