# Admin UI

`LuceneServerNET.Admin` is a separate web application (Blazor Server, MudBlazor) to administrate
**one or more** LuceneServer.NET instances. It talks to the servers only through the
[REST API v2](api-v2.md), so it can run anywhere that reaches the servers.

## Features

| Area | What you can do |
|---|---|
| Dashboard | Health, version, uptime, memory and number of indices of every instance, roles of the API client |
| Instances | Add, edit, remove and test instances (in addition to the ones from the configuration) |
| Indices | List with document count, size on disk and last modification; filter and sort |
| Search | Autocomplete input (search as you type), search with `size`/`from`/`searchAfter`, sort, spatial filter, phonetic search, group by, results as table or JSON, request as `curl` command |
| Create index | Mapping editor: fields, types, indexed/stored, primary fields, phonetics |
| Database connections | SQL Server, PostgreSQL and SQLite connections as import source (stored encrypted), connection test |
| Import | From a database query or upload CSV, JSON or GeoJSON (including geo data) (array or NDJSON), preview, mapping **proposed from the first 200 rows** (new index) or columns assigned to the existing mapping, configurable batch size, cancel, error report |
| Metadata | Show and edit custom metadata |
| Import jobs | Recurring imports from a database query (cron schedule): rebuild and switch an alias, clear and refill, or append; run history |
| Audit log | Who changed what and when (index, metadata, import, instances, connections, logins), with filters; kept for `Admin:Audit:RetentionDays` (default 180) |
| Aliases | Create, switch and remove index aliases (see [API v2](api-v2.md#aliases)); aliases are shown in the index list |
| Backup / restore | Download an index as ZIP (*Backup* on the index page) and restore it on any instance (*Restore* in the index list) |
| Copy | Copy an index (mapping, metadata, documents) to a new name on the same instance or to another instance |
| Maintenance | Refresh, delete documents by query, clear index, delete index (admin role, confirm by name) |

The UI only offers actions the API client of an instance is allowed to do: it asks the server
(`GET /api/v2/admin/whoami`) for the roles `read`, `write` and `admin`.

## Run

=== "Aspire"

    The AppHost starts the server and the admin UI:

    ```bash
    dotnet run --project src/aspire/LuceneServerNET.AppHost
    ```

    Login `admin` / `admin` (parameter `admin-password`, change it in the user secrets of the AppHost).

=== "dotnet"

    ```bash
    dotnet run --project src/LuceneServerNET.Admin
    ```

=== "Docker"

    ```bash
    docker build -f Dockerfile.admin -t luceneserver-net-admin .
    docker run --rm luceneserver-net-admin --hash-password "my password"
    docker run -p 8081:8080 -v luceneserver-admin-data:/data \
      -e Admin__Authentication__Users__0__Name=admin \
      -e Admin__Authentication__Users__0__PasswordHash='pbkdf2-sha256$...' \
      -e Admin__Instances__0__Name=prod \
      -e Admin__Instances__0__Url=http://luceneserver:8080 \
      luceneserver-net-admin
    ```

### Run as Windows service

The release ZIP (`luceneserver-net-win64-{version}.zip`) contains the admin UI in `{version}/admin` and two scripts in
the version folder:

| Script | Purpose |
|---|---|
| `lucene-admin.bat` | starts the admin UI in a console (`http://localhost:5180`) |
| `lucene-admin-service.bat` | installs it as a Windows service |

Import jobs only run while the application runs. A Windows service starts with the server and is restarted after a
crash, so it is the recommended way on Windows. Run the script in a command prompt **as administrator**:

```
lucene-admin-service.bat install [name] [port]
lucene-admin-service.bat uninstall [name]
```

`install` creates the service and starts it:

- Service name `lucene-admin`, or `lucene-admin-<name>` with a name. It starts automatically (delayed) and is restarted after a crash.
- It listens on `http://localhost:<port>` (default `5180`). Put a reverse proxy (IIS with ARR, nginx, ...) with HTTPS in front of it
  for access from other machines.
- It runs as the virtual account `NT SERVICE\lucene-admin[-<name>]`, which may write only to `admin-data` and read `_config`.
- Next to the version folders (they survive updates): `admin-data` (SQLite database `admin.db`, data protection keys) and
  `_config\admin.json`. On the first install the script creates `admin.json` with an empty user list: add a user
  (`admin\LuceneServerNET.Admin.exe --hash-password <password>`) and instances, then restart the service.
  `admin.json` overrides `appsettings.json`; environment variables override `admin.json`.

```
D:\lucene-admin\
  3.0.0\                          version folder from the ZIP
    admin\                        application
    lucene-admin-service.bat
  admin-data\                     admin.db, keys
  _config\admin.json              configuration
```

**Several instances** on one server: one root folder per instance, each with its own name and port.
`install` refuses a name whose service belongs to another root folder.

**Update:** unpack the new version into the same root folder and run its `lucene-admin-service.bat install` with the same
name and port. The script stops the service, points it to the new version and starts it again (database migrations run
on start). To go back, run `install` of the old version (only if the new version did not migrate the database).

**Own account:** for SQL Server with Windows authentication, the virtual account needs a login on the SQL Server
(`NT SERVICE\lucene-admin`), or enter a domain account in the service properties (tab **Log On**) and give it the same rights.

Warnings and errors go to the Windows event log (application log, source `LuceneServer.NET Admin`).

### IIS

The admin UI also runs in IIS (ASP.NET Core Hosting Bundle, in-process). IIS starts the application with the first request
and recycles it (idle timeout, by default every 29 hours), so **scheduled import jobs do not run reliably** in IIS. Either:

- run the UI in IIS with `Admin:Jobs:Enabled = false` and additionally the Windows service on the **same** `DataPath`
  (it runs the jobs; same data protection keys, so both can decrypt the stored secrets), or
- configure the application pool: *Start Mode* `AlwaysRunning`, *Idle Time-out* `0`, no periodic recycling, and
  *Preload Enabled* `true` on the site (feature *Application Initialization*). Missed runs are caught up after a recycle
  (*Catch up missed runs*).

The application pool identity needs write access to `DataPath`. Blazor Server needs WebSockets (IIS feature *WebSocket Protocol*).

## Configuration

```jsonc
{
  "Admin": {
    "DataPath": "_data",            // SQLite database (admin.db) + data protection keys
    "PublicUrl": "https://admin.example.com",  // optional: links in job webhooks
    "Import": { "MaxFileSizeMB": 500, "CommandTimeoutSeconds": 600 },
    "Audit": { "RetentionDays": 180 },
    "Jobs": { "Enabled": true },    // false: no scheduled runs (e.g. a second admin instance on the same database)
    "Authentication": {
      "Type": "Local",              // Local | Oidc | None (None: no login, local development only!)
      "Users": [
        { "Name": "admin", "PasswordHash": "pbkdf2-sha256$..." }
      ],
      "Oidc": {
        "Authority": "https://login.example.com/realms/main",
        "ClientId": "lucene-admin",
        "ClientSecret": "...",
        "Scopes": [ "openid", "profile" ],
        "RequiredRole": "lucene-admin"   // optional, role claim "roles"
      }
    },
    "Instances": [
      { "Name": "prod", "Url": "https://lucene.example.com", "ClientId": "admin-ui", "ClientSecret": "..." }
    ]
  }
}
```

- **Password hashes**: `LuceneServerNET.Admin --hash-password <password>` (same format as the server's `--hash-secret`).
  A plain `Password` works too, but logs a warning.
- **Configuration file outside the application folder**: the environment variable `LUCENE_ADMIN_CONFIG=<path>dmin.json`
  loads an additional file that overrides `appsettings.json` (used by the Windows service scripts).
- **Instances** from the configuration are read only in the UI. Instances added in the UI are stored in
  `admin.db`; their client secrets are encrypted with ASP.NET Core Data Protection (keys in `DataPath/keys`).
  Keep `DataPath` on a persistent volume, otherwise stored secrets can not be decrypted after a restart.
- **API client per instance**: create a client on the server (see [Authentication](configuration/authentication.md))
  with the role you want to allow in the UI, e.g. `admin` for full administration or `read` for search only.

!!! warning
    Whoever can log in to the admin UI can do everything the configured API clients are allowed to do on all
    instances. Do not expose it publicly without OIDC or strong passwords, and use HTTPS.

## Import

1. Select an existing index or enter the name of a new index.
2. **File:** upload a CSV file (header row, delimiter `,` `;` tab or `|` is detected) or JSON
   (array of objects, `{ "items": [...] }` or NDJSON).
   **Database:** choose a connection (menu *Database connections*), enter a SELECT query and click *Preview*.
   The index name is taken from the table after `FROM`.
3. Check the proposed mapping. Rules: integers → `int32`, numbers → `double`, ISO dates → `datetime`,
   short values without spaces → `string`, everything else → `text`. Columns `lat`/`lon` (also
   `latitude`/`longitude`, `lng`) are additionally combined into a `geo` field `location`.
   Text fields become primary fields. Field names must not start with `_`.
4. Choose the batch size (documents per request, default 500) and start. Rows that do not match the
   mapping are skipped and listed in the report; if the server rejects a batch, its documents are sent
   one by one to find the failing rows. The index is refreshed once at the end.

### Database import

- Providers: SQL Server (`Microsoft.Data.SqlClient`), PostgreSQL (`Npgsql`), SQLite (`Microsoft.Data.Sqlite`).
- Only a single `SELECT` (or `WITH ... SELECT`) statement is accepted. It runs in a transaction that is always
  rolled back. This is a safety net, not a permission system: **use a database user with read permissions only**.
- The query is executed twice: for the preview (first 200 rows) and for the import (streamed, all rows).
  Columns with the same name get a suffix (`id`, `id2`), binary columns are skipped, `decimal` becomes `double`.
- Timeout per query: `Admin:Import:CommandTimeoutSeconds` (default 600).

### Geo data

| Source | Recognized as |
|---|---|
| Columns `lat`/`lon` (also `latitude`/`longitude`, `lng`) | Point, EPSG:4326 |
| Columns `x`/`y` (also `rechtswert`/`hochwert`, `easting`/`northing`) | Point, the EPSG code has to be chosen |
| Geometry column: WKB/EWKB (bytes), WKT/EWKT (`SRID=31256;POLYGON(...)`), GeoJSON geometry | Point, line, polygon (also multi) |
| GeoJSON file (`.geojson`, FeatureCollection) | `properties` become fields, `geometry` a geometry column, `id` the field `id` |

Further coordinate columns can be added in the mapping (*Add point from columns*).

- **Coordinate system**: a SRID in the value (EWKB, EWKT, GeoJSON `crs`) wins, otherwise the EPSG code of the geo field
  (default 4326). Everything is converted to WGS84 with [Proj4Net.Core](https://github.com/jugstalt/Proj4Net.Core);
  unknown EPSG codes are reported before the import.
- **Lines and polygons**: the server indexes points, so the `geo` field gets the *point on surface* (always on the line /
  inside the polygon, computed with [SpatialNET](https://github.com/jugstalt/spatialNET.Core) in the source system).
  The bounding box is stored in `<field>_bbox` as `"minx,miny,maxx,maxy"` (WGS84, not indexed); points get none.
- **Template fields** (*Add template field*): a field built from columns, e.g. `{PLZ} {ORT} {STR} {HNR}` or
  `Adresse in {ORT} ({ORTSTEIL})`. Column names are case insensitive, NULL is empty, numbers are invariant (`12.5`), dates ISO;
  whitespace is collapsed and empty brackets `()` / `[]` are removed. *Replacements* (from → to) are applied to all template fields.
- **Original geometry** (optional, default off): `<field>_geojson`, GeoJSON in WGS84 with 7 decimals (RFC 7946).
- **Databases**: PostGIS `geometry` / `geography` columns are read directly (as EWKB with SRID, also with `SELECT *`).
  SQL Server geometry types must be selected as WKB:

```sql
-- PostGIS: directly, or explicitly as EWKB (keeps the SRID)
SELECT id, name, geom FROM places
SELECT id, name, ST_AsEWKB(geom) AS geom FROM places
-- SQL Server geometry / geography (choose the EPSG code in the mapping)
SELECT id, name, geom.STAsBinary() AS geom FROM places
```

Search with the spatial filter on the geo field, e.g. `dist_km(geom:15.44,47.07,5)` or `bbox(geom:15.4,47.0,15.5,47.1)`.

## Copy

*Copy* on the index page copies mapping, metadata and all documents to a new index name or another instance
(the target API client needs the `write` role). Documents are read with `GET /api/v2/indices/{id}/documents/_export`,
which returns the **stored** fields only: fields with `"store": false` are lost in the copy (the page shows them before
copying). An existing target is only overwritten with *Replace the target if it exists*.

## Import jobs

*Import jobs* run database queries (one or more sources) on a schedule, manually or triggered from outside and import the
result. The mapping is detected once with the preview, can be edited and is saved with the job (the columns of the queries
must not change afterwards).

| Mode | What happens |
|---|---|
| Rebuild and switch alias | A new index `{alias}_{yyyyMMddHHmmss}` is created and filled; on success the alias is switched to it and older generations are removed, except the newest *Keep older generations* (default 1, for a rollback via the alias page). Generations used by any alias are never removed. If the run fails, the new generation is removed and the alias stays unchanged. Clients use the alias name. |
| Clear and refill | The index is emptied and filled again (searches see an empty or partial index during the run). |
| Append | Documents are added. |

- **Schedule**: cron expression with 5 fields (`minute hour day month weekday`, e.g. `0 3 * * *`) and a time zone
  (empty = UTC). The page shows the next runs. Without a cron expression (*manual*) the job only runs with *Run now*.
- **Progress** of a running job (job list and job page): rows read, rate and elapsed time; from the second run a
  percentage estimated from the rows of the last successful run.
- **Run now**, **cancel** and a **run history** (status, duration, counts, index, message) per job. Every run is in the audit log.
- **External trigger**: start the job from outside instead of (or in addition to) a schedule, e.g. at the end of a
  Jenkins job that updates the database. See [Trigger import jobs from outside](#trigger-import-jobs-from-outside).
- **Webhook** (optional): after a run, the URL is called with `POST` and a JSON body, either only on failure (failed,
  cancelled, interrupted; default) or after every run. An optional header `Name: value` (e.g. `Authorization: Bearer ...`)
  is stored encrypted. Up to 3 attempts (5 s apart, 30 s timeout); the result is shown in the run history, a failing
  webhook does not change the status of the run. *Test webhook* sends a sample. The field `text` is a one-line summary,
  so Slack and Mattermost incoming webhooks show it directly:

  ```json
  {
    "text": "❌ Import job orte: failed · prod/orte_20261107030000 · read 10, imported 9, failed 1 · 12.5 s · ...",
    "job": "orte", "jobId": 7, "runId": 3, "instance": "prod", "target": "orte", "mode": "replace_alias",
    "index": "orte_20261107030000", "status": "failed", "success": false, "triggeredBy": "job:orte",
    "startedUtc": "2026-11-07T03:00:00Z", "finishedUtc": "2026-11-07T03:00:12.5Z", "durationSeconds": 12.5,
    "read": 10, "imported": 9, "failed": 1, "message": "...", "url": "https://admin.example.com/jobs/7"
  }
  ```

  `status` is `succeeded`, `warning` (rows skipped), `failed`, `cancelled` or `interrupted`; `url` is only set if
  `Admin:PublicUrl` is configured.
- Runs only happen while the admin application runs. With *Catch up missed runs* (default on) a job runs once at
  startup if a scheduled run was missed while the application was stopped (only once, however many were missed).
  Runs that were active during a shutdown are marked *interrupted*. For reliable schedules run the admin application as a
  service (see [Run as Windows service](#run-as-windows-service)). Run only **one** admin instance with `Admin:Jobs:Enabled = true` per database.

### Sources and the webgis template

A job has one or more **sources** (tabs on the job page): each with its own database connection, query, mapping and
metadata. All sources are imported into the same index (generation); with *Rebuild and switch alias* the alias is switched
after the last source. A field used by several sources must have the same type. *Phonetics* and *Encode characters* apply to the index.

**Metadata per source**: stored in the index under the **meta id** of the source (32 hex characters, created once, stable
for every run) as JSON `{"id", "category", "sample", "description", "service", "query", ...}` plus own keys. In mode
*Clear and refill*, entries of sources removed from the job (keys that look like a meta id) are deleted.

**Template variables**: `{@metaid}` (meta id of the source) and `{@category}` (category of the metadata).

**webgis template** (*Apply webgis template* after the preview, choose the ID column): the structure of the webgis search index,
which replaces the gView `FillLuceneServer` tool:

| Field | Content |
|---|---|
| `id` | `{@metaid}.{ID column}` – webgis takes the meta id from it, reads service and query from the metadata and the object by its id |
| `suggested_text` | template, e.g. `{PLZ} {ORT} {STR} {HNR}` (primary field, required) |
| `subtext` | template, e.g. `Adresse in {ORT} ({ORTSTEIL})` (primary field) |
| `category` | `{@category}` (primary field; the category of the metadata is required) |
| `thumbnail_url` | template or fixed URL (stored only) |
| `geo`, `bbox` | the first geo source (point; lines and polygons: point on surface, `bbox` in WGS84) |

The other columns stay visible in the mapping, but are not imported. The ID column is proposed (`objectid`, `oid`, `fid`,
`gid`, `id`, else the first integer column). The meta id stays the same for every run, so document ids are stable
(gView created a new one with every fill).

### Trigger import jobs from outside

*Create trigger token* on the job page creates a token for this job. It is shown only once (stored as SHA-256 hash), may
only start this job and can be renewed or revoked. Every call is in the audit log (`job.trigger`, with IP address).

```bash
# Jenkins (sh), at the end of the job that updates the database
curl --fail -X POST "https://admin.example.com/api/jobs/7/run" \
     -H "Authorization: Bearer $LUCENE_JOB_TOKEN" \
     -H "Content-Type: application/json" -d "{\"note\":\"jenkins #$BUILD_NUMBER\"}"
```

```bat
rem Jenkins (bat)
curl --fail -X POST "https://admin.example.com/api/jobs/7/run" -H "Authorization: Bearer %LUCENE_JOB_TOKEN%" -H "Content-Type: application/json" -d "{\"note\":\"jenkins #%BUILD_NUMBER%\"}"
```

Keep the token in a Jenkins credential (*Secret text*), not in the job definition.

| Call | Response |
|---|---|
| `POST /api/jobs/{id}/run` | `202 {"status":"started","runId":12,"statusUrl":"/api/jobs/7/runs/12"}`; if the job is running: `202 {"status":"queued"}` – it runs once more right after the current run (several triggers during a run: one more run) |
| `GET /api/jobs/{id}/runs/{runId}` | `200` with `status`, `finished`, `success`, times, counts, `message`, `note`, `webhook` |
| invalid job or token | `401` |
| job disabled | `409` |

- The token is sent as `Authorization: Bearer <token>` (or header `X-Job-Token`). The optional `note` (JSON body or
  `?note=`, max. 200 characters) is shown in the run history and sent with the webhook.
- The call returns immediately. To fail the Jenkins build on a failed import, poll the `statusUrl` until `finished` is
  `true` and check `success`; for notifications the job webhook is usually enough.
- A schedule can stay as a safety net (e.g. `0 6 * * *`): it runs independently of the trigger.
- The admin UI must be reachable from Jenkins (HTTPS, see [Run as Windows service](#run-as-windows-service) / [IIS](#iis)).

## Backup and restore

*Backup* on the index page downloads a ZIP:

| File | Content |
|---|---|
| `manifest.json` | format, index, instance, server version, time, document count, fields that are not stored |
| `mapping.json` | the mapping |
| `metadata.json` | the custom metadata |
| `documents.ndjson` | all documents (stored fields, one JSON object per line) |

*Restore* in the index list uploads such a ZIP (max. `Admin:Import:MaxFileSizeMB`) and creates the index under the original or
another name, on any instance; an existing index is only replaced with *Replace the target if it exists*. Like copying, a backup
contains only stored fields. For a complete, continuous backup use the server's archive (`LuceneServer:ArchivePath`).
