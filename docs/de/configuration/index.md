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
    "AutoRestoreOnStartupSinceSeconds": 86400,
    "MaxRequestBodySizeBytes": 31457280,
    "UseHttpsRedirection": false
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
| `ArchivePath` | Optional, standardmäßig nicht gesetzt. Ist er gesetzt, werden Mapping, Metadaten und jedes indizierte Dokument zusätzlich als JSON-Dateien hier abgelegt (eine Datei pro Dokument), damit ein Index neu aufgebaut werden kann. Das bremst das Indizieren deutlich (gemessen: etwa Faktor 26 unter Windows); nur für Indizes ohne andere Quelle verwenden. Mit `?archive=false` angelegte Indizes (z. B. von Import-Jobs) werden nie archiviert. |
| `BackgroundCommitSeconds` | Optional, Standard `20`. Intervall (Sekunden), in dem der Server alle offenen Indizes im Hintergrund refresht und committet. Änderungen sind spätestens nach dieser Zeit in der Suche sichtbar, auch ohne explizites `refresh`. |
| `AutoRestoreOnStartup` | Beim Start archivierte Indizes neu aufbauen, die in `RootPath` nicht mehr existieren (z. B. nach einem Container-Neustart ohne persistenten Speicher). |
| `AutoRestoreOnStartupCount` | Wenn `> 0`: nur die letzten *n* archivierten Dokumente je Index wiederherstellen. |
| `AutoRestoreOnStartupSinceSeconds` | Wenn `Count` `0` ist und dieser Wert `> 0`: nur Dokumente aus diesem Zeitraum wiederherstellen (Standard `86400` = 1 Tag). Sind beide `0`, werden alle Dokumente wiederhergestellt. |
| `AllowLegacyGetMutations` | Standard `true`. Erlaubt übergangsweise die veralteten **GET**-Aufrufe von `createindex`, `removeindex`, `remove`, `refresh` und `releaseall` (Antwort mit `Deprecation`/`Warning`-Header und Log-Warnung). Bei `false` liefern diese GET-Aufrufe `405 Method Not Allowed`; nur noch POST/DELETE sind erlaubt. Wird in einer künftigen Version standardmäßig `false`. |
| `MaxRequestBodySizeBytes` | Maximale Größe eines Request-Bodys in Bytes (z. B. beim Indizieren), gilt für Kestrel und IIS. Standard `31457280` (30 MB). Größere Requests werden abgewiesen; große Datenmengen in mehreren Aufrufen indizieren. |
| `UseHttpsRedirection` | Standard `false`. Bei `true` werden HTTP-Aufrufe auf HTTPS umgeleitet. Nur sinnvoll, wenn der Server selbst HTTPS anbietet (nicht hinter einem TLS-terminierenden Proxy). |

## API-Dokumentation (OpenAPI)

| Schlüssel | Beschreibung |
|---|---|
| `useSwagger` | `true` veröffentlicht die OpenAPI-Dokumente (`/openapi/v1.json`, `/openapi/v2.json`) und die API-Referenz-UI ([Scalar](https://scalar.com)) unter `/scalar`. In `Development` immer aktiv. Die alten URLs `/swagger` und `/swagger/{v}/swagger.json` leiten auf die neuen weiter. Diese Endpunkte sind ohne Anmeldung erreichbar; außerhalb von `Development` wird beim Start eine Warnung geloggt. |

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
