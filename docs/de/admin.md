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
| Import | Aus einer Datenbank-Abfrage oder CSV bzw. JSON (Array oder NDJSON) hochladen, Vorschau, Mapping **aus den ersten 200 Zeilen vorgeschlagen** (neuer Index) bzw. Spalten dem bestehenden Mapping zugeordnet, einstellbare Batchgröße, Abbruch, Fehlerreport |
| Metadaten | Eigene Metadaten anzeigen und bearbeiten |
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
