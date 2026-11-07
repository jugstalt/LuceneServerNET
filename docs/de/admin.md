# Admin-Oberfläche

`LuceneServerNET.Admin` ist eine eigene Webanwendung (Blazor Server, MudBlazor), mit der **eine oder
mehrere** LuceneServer.NET-Instanzen administriert werden. Sie spricht die Server ausschließlich über die
[REST-API v2](api-v2.md) an und kann daher überall laufen, wo die Server erreichbar sind.

## Funktionen

| Bereich | Möglichkeiten |
|---|---|
| Dashboard | Health, Version, Uptime, Speicher und Anzahl der Indizes jeder Instanz, Rollen des API-Clients |
| Instanzen | Instanzen anlegen, bearbeiten, entfernen und testen (zusätzlich zu denen aus der Konfiguration) |
| Indizes | Liste mit Dokumentanzahl, Größe auf der Platte und letzter Änderung; filtern und sortieren |
| Suche | Autocomplete-Eingabe (Search as you type), Suche mit `size`/`from`/`searchAfter`, Sortierung, räumlicher Filter, phonetische Suche, Gruppierung, Ergebnis als Tabelle oder JSON, Request als `curl`-Befehl |
| Index anlegen | Mapping-Editor: Felder, Typen, indiziert/gespeichert, Primärfelder, Phonetik |
| Datenbank-Verbindungen | SQL Server-, PostgreSQL- und SQLite-Verbindungen als Importquelle (verschlüsselt gespeichert), Verbindungstest |
| Import | Aus einer Datenbank-Abfrage oder CSV, JSON bzw. GeoJSON (inkl. Geodaten) (Array oder NDJSON) hochladen, Vorschau, Mapping **aus den ersten 200 Zeilen vorgeschlagen** (neuer Index) bzw. Spalten dem bestehenden Mapping zugeordnet, einstellbare Batchgröße, Abbruch, Fehlerreport |
| Metadaten | Eigene Metadaten anzeigen und bearbeiten |
| Import-Jobs | Wiederkehrende Importe aus einer Datenbank-Abfrage (Cron-Zeitplan): neu aufbauen und Alias umschalten, leeren und neu befüllen oder anhängen; Lauf-Verlauf |
| Audit-Log | Wer wann was geändert hat (Index, Metadaten, Import, Instanzen, Verbindungen, Logins), mit Filtern; Aufbewahrung `Admin:Audit:RetentionDays` (Standard 180) |
| Aliases | Index-Aliases anlegen, umschalten und entfernen (siehe [API v2](api-v2.md#aliases)); Aliases stehen in der Indexliste |
| Backup / Restore | Index als ZIP herunterladen (*Backup* auf der Index-Seite) und auf einer beliebigen Instanz wiederherstellen (*Restore* in der Indexliste) |
| Kopieren | Index (Mapping, Metadaten, Dokumente) unter neuem Namen auf derselben oder auf eine andere Instanz kopieren |
| Wartung | Refresh, Dokumente per Query löschen, Index leeren, Index löschen (Rolle admin, Bestätigung per Name) |

Die Oberfläche bietet nur an, was der API-Client der Instanz darf: Sie fragt den Server
(`GET /api/v2/admin/whoami`) nach den Rollen `read`, `write` und `admin`.

## Starten

=== "Aspire"

    Der AppHost startet Server und Admin-Oberfläche:

    ```bash
    dotnet run --project src/aspire/LuceneServerNET.AppHost
    ```

    Login `admin` / `admin` (Parameter `admin-password`, in den User Secrets des AppHost änderbar).

=== "dotnet"

    ```bash
    dotnet run --project src/LuceneServerNET.Admin
    ```

=== "Docker"

    ```bash
    docker build -f Dockerfile.admin -t luceneserver-net-admin .
    docker run --rm luceneserver-net-admin --hash-password "mein passwort"
    docker run -p 8081:8080 -v luceneserver-admin-data:/data \
      -e Admin__Authentication__Users__0__Name=admin \
      -e Admin__Authentication__Users__0__PasswordHash='pbkdf2-sha256$...' \
      -e Admin__Instances__0__Name=prod \
      -e Admin__Instances__0__Url=http://luceneserver:8080 \
      luceneserver-net-admin
    ```

## Konfiguration

```jsonc
{
  "Admin": {
    "DataPath": "_data",            // SQLite-Datenbank (admin.db) + Data-Protection-Schlüssel
    "Import": { "MaxFileSizeMB": 500, "CommandTimeoutSeconds": 600 },
    "Audit": { "RetentionDays": 180 },
    "Jobs": { "Enabled": true },    // false: keine geplanten Läufe (z. B. eine zweite Admin-Instanz auf derselben Datenbank)
    "Authentication": {
      "Type": "Local",              // Local | Oidc | None (None: kein Login, nur lokale Entwicklung!)
      "Users": [
        { "Name": "admin", "PasswordHash": "pbkdf2-sha256$..." }
      ],
      "Oidc": {
        "Authority": "https://login.example.com/realms/main",
        "ClientId": "lucene-admin",
        "ClientSecret": "...",
        "Scopes": [ "openid", "profile" ],
        "RequiredRole": "lucene-admin"   // optional, Rollen-Claim "roles"
      }
    },
    "Instances": [
      { "Name": "prod", "Url": "https://lucene.example.com", "ClientId": "admin-ui", "ClientSecret": "..." }
    ]
  }
}
```

- **Passwort-Hashes**: `LuceneServerNET.Admin --hash-password <passwort>` (gleiches Format wie `--hash-secret` des Servers).
  Ein Klartext-`Password` funktioniert ebenfalls, erzeugt aber eine Warnung im Log.
- **Instanzen** aus der Konfiguration sind in der Oberfläche schreibgeschützt. In der Oberfläche angelegte Instanzen
  liegen in `admin.db`; ihre Client-Secrets werden mit ASP.NET Core Data Protection verschlüsselt (Schlüssel in `DataPath/keys`).
  `DataPath` muss auf einem persistenten Volume liegen, sonst lassen sich gespeicherte Secrets nach einem Neustart nicht mehr entschlüsseln.
- **API-Client pro Instanz**: Am Server einen Client anlegen (siehe [Authentifizierung](configuration/authentication.md)),
  mit der Rolle, die in der Oberfläche erlaubt sein soll, z. B. `admin` für volle Administration oder `read` nur für die Suche.

!!! warning
    Wer sich an der Admin-Oberfläche anmelden kann, darf auf allen Instanzen alles, was die konfigurierten API-Clients dürfen.
    Nicht ohne OIDC oder starke Passwörter öffentlich erreichbar machen und HTTPS verwenden.

## Import

1. Bestehenden Index auswählen oder den Namen eines neuen Index eingeben.
2. **Datei:** CSV-Datei (Kopfzeile, Trennzeichen `,` `;` Tab oder `|` wird erkannt) oder JSON
   (Array von Objekten, `{ "items": [...] }` oder NDJSON) hochladen.
   **Datenbank:** Verbindung wählen (Menü *Database connections*), SELECT-Abfrage eingeben und *Preview* klicken.
   Der Indexname wird aus der Tabelle nach `FROM` übernommen.
3. Vorgeschlagenes Mapping prüfen. Regeln: Ganzzahlen → `int32`, Zahlen → `double`, ISO-Datum → `datetime`,
   kurze Werte ohne Leerzeichen → `string`, alles andere → `text`. Spalten `lat`/`lon` (auch
   `latitude`/`longitude`, `lng`) werden zusätzlich zu einem `geo`-Feld `location` kombiniert.
   Textfelder werden Primärfelder. Feldnamen dürfen nicht mit `_` beginnen.
4. Batchgröße wählen (Dokumente pro Request, Standard 500) und starten. Zeilen, die nicht zum Mapping passen,
   werden übersprungen und im Report aufgelistet; lehnt der Server einen Batch ab, werden dessen Dokumente
   einzeln gesendet, um die fehlerhaften Zeilen zu finden. Am Ende wird der Index einmal refresht.

### Import aus einer Datenbank

- Provider: SQL Server (`Microsoft.Data.SqlClient`), PostgreSQL (`Npgsql`), SQLite (`Microsoft.Data.Sqlite`).
- Erlaubt ist nur eine einzelne `SELECT`-Anweisung (oder `WITH ... SELECT`). Sie läuft in einer Transaktion, die immer
  zurückgerollt wird. Das ist ein Sicherheitsnetz, kein Berechtigungskonzept: **einen Datenbank-User mit reinen Leserechten verwenden**.
- Die Abfrage läuft zweimal: für die Vorschau (erste 200 Zeilen) und für den Import (gestreamt, alle Zeilen).
  Gleichnamige Spalten bekommen ein Suffix (`id`, `id2`), Binärspalten werden übersprungen, `decimal` wird zu `double`.
- Timeout pro Abfrage: `Admin:Import:CommandTimeoutSeconds` (Standard 600).

### Geodaten

| Quelle | Erkannt als |
|---|---|
| Spalten `lat`/`lon` (auch `latitude`/`longitude`, `lng`) | Punkt, EPSG:4326 |
| Spalten `x`/`y` (auch `rechtswert`/`hochwert`, `easting`/`northing`) | Punkt, der EPSG-Code muss gewählt werden |
| Geometrie-Spalte: WKB/EWKB (Bytes), WKT/EWKT (`SRID=31256;POLYGON(...)`), GeoJSON-Geometrie | Punkt, Linie, Polygon (auch Multi) |
| GeoJSON-Datei (`.geojson`, FeatureCollection) | `properties` werden Felder, `geometry` eine Geometrie-Spalte, `id` das Feld `id` |

Weitere Koordinatenspalten lassen sich im Mapping hinzufügen (*Add point from columns*).

- **Koordinatensystem**: Eine SRID im Wert (EWKB, EWKT, GeoJSON `crs`) hat Vorrang, sonst gilt der EPSG-Code des Geo-Felds
  (Standard 4326). Alles wird mit [Proj4Net.Core](https://github.com/jugstalt/Proj4Net.Core) nach WGS84 umgerechnet;
  unbekannte EPSG-Codes werden vor dem Import gemeldet.
- **Linien und Polygone**: Der Server indiziert Punkte, das `geo`-Feld bekommt daher den *Point on Surface* (liegt immer auf
  der Linie bzw. im Polygon, berechnet mit [SpatialNET](https://github.com/jugstalt/spatialNET.Core) im Quellsystem).
  Die Bounding-Box steht in `<feld>_bbox` als `"minx,miny,maxx,maxy"` (WGS84, nicht indiziert); Punkte bekommen keine.
- **Original-Geometrie** (optional, standardmäßig aus): `<feld>_geojson`, GeoJSON in WGS84 mit 7 Nachkommastellen (RFC 7946).
- **Datenbanken**: Die Treiber können Geometrie-Typen nicht lesen, daher als WKB abfragen:

```sql
-- PostGIS (EWKB behält die SRID)
SELECT id, name, ST_AsEWKB(geom) AS geom FROM places
-- SQL Server geometry / geography (EPSG-Code im Mapping wählen)
SELECT id, name, geom.STAsBinary() AS geom FROM places
```

Gesucht wird mit dem räumlichen Filter auf das Geo-Feld, z. B. `dist_km(geom:15.44,47.07,5)` oder `bbox(geom:15.4,47.0,15.5,47.1)`.

## Kopieren

*Copy* auf der Index-Seite kopiert Mapping, Metadaten und alle Dokumente unter einen neuen Indexnamen oder auf eine andere
Instanz (der API-Client des Ziels braucht die Rolle `write`). Die Dokumente kommen aus `GET /api/v2/indices/{id}/documents/_export`,
der nur **gespeicherte** Felder liefert: Felder mit `"store": false` gehen beim Kopieren verloren (die Seite zeigt sie vorher an).
Ein bestehendes Ziel wird nur mit *Replace the target if it exists* überschrieben.

## Import-Jobs

*Import jobs* führen eine Datenbank-Abfrage nach Zeitplan aus und importieren das Ergebnis. Das Mapping wird einmal über die Vorschau
erkannt, kann angepasst werden und wird mit dem Job gespeichert (die Spalten der Abfrage dürfen sich danach nicht ändern).

| Modus | Ablauf |
|---|---|
| Neu aufbauen und Alias umschalten | Ein neuer Index `{alias}_{yyyyMMddHHmmss}` wird angelegt und befüllt; bei Erfolg wird der Alias umgeschaltet und ältere Generationen werden gelöscht, bis auf die neuesten *Keep older generations* (Standard 1, für ein Rollback über die Alias-Seite). Generationen, auf die irgendein Alias zeigt, werden nie gelöscht. Schlägt der Lauf fehl, wird die neue Generation entfernt und der Alias bleibt unverändert. Clients verwenden den Alias-Namen. |
| Leeren und neu befüllen | Der Index wird geleert und neu befüllt (Suchen sehen währenddessen einen leeren bzw. halben Index). |
| Anhängen | Dokumente werden hinzugefügt. |

- **Zeitplan**: Cron-Ausdruck mit 5 Feldern (`Minute Stunde Tag Monat Wochentag`, z. B. `0 3 * * *`) und Zeitzone
  (leer = UTC). Die Seite zeigt die nächsten Läufe.
- **Jetzt ausführen**, **Abbrechen** und ein **Lauf-Verlauf** (Status, Dauer, Zahlen, Index, Meldung) pro Job. Jeder Lauf steht im Audit-Log.
- Läufe finden nur statt, solange die Admin-App läuft; verpasste Läufe werden nicht nachgeholt, Läufe, die bei einem Herunterfahren
  aktiv waren, werden als *interrupted* markiert. Pro Datenbank nur **eine** Admin-Instanz mit `Admin:Jobs:Enabled = true` betreiben.

## Backup und Restore

*Backup* auf der Index-Seite lädt ein ZIP herunter:

| Datei | Inhalt |
|---|---|
| `manifest.json` | Format, Index, Instanz, Server-Version, Zeitpunkt, Anzahl Dokumente, nicht gespeicherte Felder |
| `mapping.json` | das Mapping |
| `metadata.json` | die eigenen Metadaten |
| `documents.ndjson` | alle Dokumente (gespeicherte Felder, ein JSON-Objekt pro Zeile) |

*Restore* in der Indexliste lädt so ein ZIP hoch (max. `Admin:Import:MaxFileSizeMB`) und legt den Index unter dem ursprünglichen oder
einem anderen Namen an, auf einer beliebigen Instanz; ein bestehender Index wird nur mit *Replace the target if it exists* ersetzt. Wie beim
Kopieren enthält ein Backup nur gespeicherte Felder. Für eine vollständige, laufende Sicherung dient das Archiv des Servers (`LuceneServer:ArchivePath`).
