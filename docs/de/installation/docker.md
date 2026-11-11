# Docker

Das Repository enthält ein mehrstufiges `Dockerfile`: Es baut den Server aus dem Quellcode mit dem
.NET-10-SDK und startet ihn im ASP.NET-Core-10-Runtime-Image als Benutzer `app` (nicht root).

## Image bauen

```bash
docker build -t luceneserver-net .
```

Oder über den NUKE-Build, der das Image mit der Version taggt (und `latest`, dazu die Tags
`ghcr.io/jugstalt/luceneserver-net`):

```bash
./build.cmd Docker
```

## Starten

```bash
docker run -d --name luceneserver -p 8080:8080 -v luceneserver-data:/data luceneserver-net
```

Der Server lauscht im Container auf Port **8080**.

## Volumes

| Pfad im Container | Einstellung | Inhalt |
|---|---|---|
| `/data/indices` | `LuceneServer:RootPath` | die Lucene-Indizes |
| `/data/archive` | `LuceneServer:ArchivePath` | optional, standardmäßig nicht gesetzt: das Archiv (`-e LuceneServer__ArchivePath=/data/archive`), siehe [Konfiguration](../configuration/index.md) |

Ein Volume (oder Host-Verzeichnis) auf `/data` einhängen. Ein Host-Verzeichnis muss für den Benutzer
`app` (UID `1654`) beschreibbar sein.

!!! danger "Nur eine schreibende Instanz pro Index"
    Ein Lucene-Index darf nur von **einem** Prozess geschrieben werden. Lucene schützt einen Index
    mit einer `write.lock`-Datei; ein zweiter Container auf demselben Volume scheitert mit Lock-Fehlern
    oder kann auf Netzwerk-Dateisystemen mit unzuverlässigen Locks den Index beschädigen. Den Server
    **nicht** durch mehrere Replikas auf einem gemeinsamen Volume skalieren, sondern eine Instanz pro Volume betreiben.

## Konfiguration

Das Image braucht keine Konfigurationsdatei: Alle Einstellungen werden aus Umgebungsvariablen gelesen
(`:` wird zu `__`, siehe [Konfiguration](../configuration/index.md)).

```bash
docker run -d -p 8080:8080 -v luceneserver-data:/data \
  -e Authorization__Type=basic \
  -e Authorization__Clients__0__Id=admin \
  -e Authorization__Clients__0__SecretHash='<hash>' \
  -e Authorization__Clients__0__Roles__0=admin \
  luceneserver-net
```

Den Hash erzeugt `docker run --rm luceneserver-net --hash-secret <secret>`.

Vom Image gesetzte Standardwerte:

| Variable | Wert |
|---|---|
| `ASPNETCORE_HTTP_PORTS` | `8080` |
| `LUCENESERVER__ROOTPATH` | `/data/indices` |
| `LUCENESERVER__ARCHIVEPATH` | `/data/archive` |

## Health-Check

Das Image hat einen Docker-`HEALTHCHECK` auf [`/health`](operations.md#health-endpoints); `docker ps` zeigt
den Container als `healthy`, sobald die Indexverzeichnisse beschreibbar sind.

## GitHub Container Registry

`./build.cmd DockerPush` pusht `ghcr.io/jugstalt/luceneserver-net:{version}` und `:latest`.
Vorher mit einem Token mit dem Scope `write:packages` anmelden:

```bash
docker login ghcr.io -u <user>
```
