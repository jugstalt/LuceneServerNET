# Konfiguration

Der Server liest die üblichen ASP.NET-Core-Konfigurationsquellen (`appsettings.json`,
Umgebungsvariablen, Kommandozeile) sowie die Datei **`_config/luceneserver.json`**.
Die Datei wird beim ersten Start erzeugt (siehe [Installation](../installation/index.md)).

```json
{
  "LuceneServer": {
    "RootPath": "c:\\data\\lucene\\indices",
    "ArchivePath": "c:\\data\\lucene\\archive",
    "AutoRestoreOnStartup": true,
    "AutoRestoreOnStartupCount": 0,
    "AutoRestoreOnStartupSinceSeconds": 86400
  },
  "useSwagger": false,
  "Authorization": {
    "Type": "Basic",
    "Clients": [ ]
  }
}
```

!!! tip
    Die Vorlage verwendet Schlüssel mit abschließendem Unterstrich (`ArchivePath_`, `Authorization_`).
    Diese werden ignoriert. Zum Aktivieren den Unterstrich entfernen.

## LuceneServer

| Schlüssel | Beschreibung |
|---|---|
| `RootPath` | Verzeichnis mit den Index-Ordnern. Pflicht. |
| `ArchivePath` | Optional. Ist er gesetzt, werden Mapping, Metadaten und jedes indizierte Dokument zusätzlich als JSON-Dateien hier abgelegt, damit ein Index neu aufgebaut werden kann. |
| `AutoRestoreOnStartup` | Beim Start archivierte Indizes neu aufbauen, die in `RootPath` nicht mehr existieren (z. B. nach einem Container-Neustart ohne persistenten Speicher). |
| `AutoRestoreOnStartupCount` | Wenn `> 0`: nur die letzten *n* archivierten Dokumente je Index wiederherstellen. |
| `AutoRestoreOnStartupSinceSeconds` | Wenn `Count` `0` ist und dieser Wert `> 0`: nur Dokumente aus diesem Zeitraum wiederherstellen (Standard `86400` = 1 Tag). Sind beide `0`, werden alle Dokumente wiederhergestellt. |

## Swagger

| Schlüssel | Beschreibung |
|---|---|
| `useSwagger` | `true` aktiviert die Swagger-UI unter `/swagger`. In `Development` immer aktiv. |

## Authentifizierung

Der Abschnitt `Authorization` ist unter [Authentifizierung](authentication.md) beschrieben.

## Umgebungsvariablen

Jede Einstellung kann als Umgebungsvariable gesetzt werden, mit `__` als Trenner:

```bash
LUCENESERVER__ROOTPATH=/var/lib/luceneserver/indices
LUCENESERVER__ARCHIVEPATH=/var/lib/luceneserver/archive
AUTHORIZATION__TYPE=Basic
AUTHORIZATION__CLIENTS__0__ID=admin
AUTHORIZATION__CLIENTS__0__SECRETHASH=pbkdf2-sha256$...
AUTHORIZATION__CLIENTS__0__ROLES__0=admin
```

Ist unter Linux `LUCENESERVER__ROOTPATH` gesetzt, wird beim ersten Start keine `_config/luceneserver.json` erzeugt.

## Indexnamen

Indexnamen (und Metadaten-Namen) müssen `^[a-z0-9_-]{1,64}$` entsprechen. Namen werden zuvor in
Kleinbuchstaben umgewandelt. Andere Namen werden mit `success: false` abgelehnt.
