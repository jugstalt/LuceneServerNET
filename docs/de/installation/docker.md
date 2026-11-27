# Docker

Das Image `ghcr.io/jugstalt/luceneserver-net` basiert auf dem Alpine-Runtime-Image von ASP.NET Core 11 und startet den Server als
Benutzer `app` (nicht root). Der NUKE-Build veröffentlicht den Server und kopiert ihn ins Image
(`deploy/docker/server/Dockerfile`); Releases pushen es in die GitHub Container Registry.

## Image holen oder bauen

```bash
docker pull ghcr.io/jugstalt/luceneserver-net
```

Oder lokal bauen (Tags `{version}` und `latest`; mit `--container-cli wslc` oder `podman` statt Docker):

```bash
./build.cmd DockerBuild
```

## Starten

```bash
docker run -d --name luceneserver -p 8080:8080 -v luceneserver-data:/data ghcr.io/jugstalt/luceneserver-net
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
  ghcr.io/jugstalt/luceneserver-net
```

Den Hash erzeugt `docker run --rm ghcr.io/jugstalt/luceneserver-net --hash-secret <secret>`.

Vom Image gesetzte Standardwerte:

| Variable | Wert |
|---|---|
| `ASPNETCORE_HTTP_PORTS` | `8080` |
| `LUCENESERVER__ROOTPATH` | `/data/indices` |
| `LUCENESERVER__ARCHIVEPATH` | `/data/archive` |

## Docker Compose (Server + Admin-UI)

`deploy/docker/docker-compose.yml` startet den Server (Port `8080`) mit Basic-Authentifizierung und die
[Admin-UI](../admin.md) (Port `8081`), die als Instanz `local` mit dem Server verbunden ist.

1. `deploy/docker/.env.example` nach `deploy/docker/.env` kopieren (von git ignoriert) und die Secrets eintragen:

    ```bash
    docker run --rm ghcr.io/jugstalt/luceneserver-net --hash-secret 'client-secret'      # LUCENE_ADMIN_CLIENT_SECRET_HASH
    docker run --rm ghcr.io/jugstalt/luceneserver-net-admin --hash-password 'passwort'   # ADMIN_PASSWORD_HASH
    ```

    `LUCENE_ADMIN_CLIENT_SECRET` ist dasselbe Client-Secret im Klartext. Die Hashes in einfachen
    Anführungszeichen lassen: Sie enthalten `$`.

2. Starten:

    ```bash
    docker compose -f deploy/docker/docker-compose.yml up -d
    ```

`LUCENE_TAG` wählt die Image-Version (Standard `latest`). Lokal mit `--container-cli wslc` gebaute Images
sieht Docker Compose nicht.

## Health-Check

Das Image hat einen Docker-`HEALTHCHECK` auf [`/health`](operations.md#health-endpoints); `docker ps` zeigt
den Container als `healthy`, sobald die Indexverzeichnisse beschreibbar sind.

## GitHub Container Registry

Releases (Git-Tag `v*`) pushen `ghcr.io/jugstalt/luceneserver-net:{version}`, `:{major.minor}` und `:latest` aus dem GitHub-Workflow
`release.yml`; Pre-Releases bekommen nur `:{version}`. Zum Pushen vom eigenen Rechner mit einem Token mit dem Scope
`write:packages` anmelden und `./build.cmd DockerPush` ausführen:

```bash
docker login ghcr.io -u <user>
```
