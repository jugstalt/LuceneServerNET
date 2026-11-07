# Installation

LuceneServer.NET ist eine ASP.NET-Core-Anwendung (.NET 10). Fertige Binaries gibt es noch nicht,
der Server wird aus dem Quellcode gebaut.

## Voraussetzungen

- [.NET 10 SDK](https://dotnet.microsoft.com/download) zum Bauen, ASP.NET Core 10 Runtime zum Ausführen
- Windows oder Linux
- Ein Verzeichnis für die Indexdateien (siehe [`RootPath`](../configuration/index.md))

## Bauen

```bash
git clone https://github.com/jugstalt/LuceneServerNET.git
cd LuceneServerNET
dotnet publish src/LuceneServerNET -c Release -o ./publish
```

## Erster Start

```bash
cd publish
dotnet LuceneServerNET.dll
```

Beim ersten Start erzeugt der Server `_config/luceneserver.json` aus der Vorlage
`_config/proto/_luceneserver.json` und gibt sie auf der Konsole aus. Diese Datei an die eigene
Umgebung anpassen, siehe [Konfiguration](../configuration/index.md).

- **Windows:** Das Indexverzeichnis ist standardmäßig ein `data`-Ordner einige Ebenen oberhalb der Anwendung.
- **Linux:** Das Datenverzeichnis kommt aus der Umgebungsvariable `LUCENESERVER_DATA_PATH`
  (Standard `/etc/luceneserver/data`). Ist `LUCENESERVER__ROOTPATH` gesetzt, wird keine Konfigurationsdatei
  erzeugt und die Einstellungen werden nur aus Umgebungsvariablen gelesen.

## Ports

Standardmäßig gelten die üblichen ASP.NET-Core-Einstellungen (`ASPNETCORE_URLS`, `--urls`). Für schnelle
Tests akzeptiert der Server zusätzlich:

```bash
dotnet LuceneServerNET.dll -expose-http 5000
dotnet LuceneServerNET.dll -expose-https 5001
```

!!! note
    `-expose-http` / `-expose-https` binden nur an `localhost`. Um auf allen Interfaces zu lauschen,
    `ASPNETCORE_URLS` verwenden (z. B. `http://0.0.0.0:5000`).

Prüfen, ob der Server läuft:

```bash
curl http://localhost:5000/info
```

## Hosting

### IIS (Windows)

Die Publish-Ausgabe enthält eine `web.config`. Das
[ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) installieren, eine Website oder
Anwendung auf den Publish-Ordner anlegen und einen Anwendungspool mit *Kein verwalteter Code* verwenden.
Die Identität des Anwendungspools braucht Schreibrechte auf `RootPath` (und `ArchivePath`).

### systemd (Linux)

```ini
# /etc/systemd/system/luceneserver.service
[Unit]
Description=LuceneServer.NET
After=network.target

[Service]
WorkingDirectory=/opt/luceneserver
ExecStart=/usr/bin/dotnet /opt/luceneserver/LuceneServerNET.dll
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=LUCENESERVER_DATA_PATH=/var/lib/luceneserver
Restart=always
User=luceneserver

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now luceneserver
```

### Windows-Dienst

Der Server registriert sich nicht selbst als Windows-Dienst. Unter Windows wird das Hosting im
**IIS** empfohlen.

### Docker

Siehe [Docker](docker.md).

## Swagger

Mit `"useSwagger": true` (oder in der Umgebung `Development`) ist die Swagger-UI unter `/swagger` erreichbar.
