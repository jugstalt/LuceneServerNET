# ChangeLog

All notable changes to LuceneServerNET are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased] – v3 (3.0.0)

### Migration from 2.x

| Area | What changes | What to do |
|---|---|---|
| Index ids / metadata names | Only `a-z`, `0-9`, `_`, `-`, max. 64 chars | Rename index folders that do not match before upgrading |
| HTTP methods | `createindex`, `refresh`, `releaseall` → POST; `removeindex`, `remove` → DELETE | Update REST clients; old GET routes work while `LuceneServer:AllowLegacyGetMutations` is `true` (default) – set it to `false` once all clients are migrated |
| NuGet `LuceneServerNET.Client` 3.0.0 | Uses POST/DELETE | Requires a v3 server – upgrade the server first |
| Authentication | Roles `read`/`write`/`admin`, hashed secrets (`SecretHash`) | Not breaking: clients without `Roles` get `admin`, plain `Secret` still works. Recommended: assign roles and switch to `SecretHash` (`--hash-secret`) |
| API v2 errors | HTTP 4xx/5xx instead of 200 | Only relevant for new v2 clients; v1 is unchanged |
| Swagger | Replaced by `/openapi/{v}.json` and `/scalar` | Old `/swagger` URLs redirect |
| Phonetic search | Fixed Cologne phonetics and Soundex codes (see *Fixed*) | **Recommended: re-index indices with `primaryFieldsPhonetics`**, otherwise affected words do not match in `searchphonetic` until re-indexed. Normal search is not affected |
| .NET client dispose | Console output replaced by optional `Logger`; `IAsyncDisposable` | Not breaking. Optionally use `await using`; set `Logger` to keep the refresh log lines |
| `datetime` fields | Values without time zone are taken as **UTC** (before: server local time); results contain `Z` | Only relevant if the server does not run in UTC and clients send dates without time zone: send `Z`/an offset or re-index. Docker (UTC) is not affected |
| Versions | Server, Engine, Core and Client share one version (`src/Directory.Build.props`) | – |


### Added

- **REST API v2** (`/api/v2/indices/{id}/...`), resource oriented, in parallel to v1 (`/Lucene/...`).
  v1 stays fully supported (not breaking) but is marked as deprecated in Swagger.
  Separate OpenAPI documents for `v1` and `v2`.
- **Paging and total hit count for search** (v1 and v2, additive):
  new optional parameters `from` (offset, `from + size` ≤ 10000) and `searchAfter` (cursor);
  responses contain `totalHits`, `from`, `size` and `nextSearchAfter`.
- `LuceneServerNET.Client`: `SearchPageAsync` / `SearchPhoneticPageAsync` (existing methods unchanged).
- **Index aliases** (additive): an alias points to one index and can be used instead of the index id in all
  v1 and v2 endpoints (search, documents, mapping, metadata, stats, export), except creating and removing indices.
  Switching is atomic, e.g. rebuild into `orte_20261107` and switch the alias `orte` without downtime.
  `GET /api/v2/aliases`, `PUT /api/v2/aliases/{alias}` (`{ "index": "..." }`, role `write`), `DELETE /api/v2/aliases/{alias}`.
  Stored in `{RootPath}/aliases.json` (copy in `{ArchivePath}`). An index used by an alias can not be removed (409),
  an alias can not have the name of an index. Client: `GetAliasesAsync`, `SetAliasAsync`, `RemoveAliasAsync`.
- **Admin UI** (`LuceneServerNET.Admin`, Blazor Server + MudBlazor), a separate web app that
  administrates several server instances through the v2 API:
  dashboard (health, version, uptime, roles), index list with document count and size,
  search playground (autocomplete, search with paging/sort/spatial filter, phonetic search,
  group by, curl command), create index with mapping editor, import from CSV/JSON
  (mapping proposed from a sample, configurable batch size, error report, cancel),
  metadata editor and maintenance (refresh, delete documents by query, clear, delete index).
  Login: local users (hashed passwords, `--hash-password`) or OpenID Connect.
  Instances from the configuration and added in the UI (secrets encrypted with DataProtection, SQLite/Dapper).
  Docker image: `Dockerfile.admin`; started by the Aspire AppHost.
  Import from a database (SQL Server, PostgreSQL, SQLite): stored connections (encrypted),
  a SELECT query with preview, then the same mapping proposal and batch import as for files.
  Geo data import: geometry columns (WKB/EWKB, WKT/EWKT, GeoJSON; PostGIS `ST_AsEWKB`, SQL Server `STAsBinary()`),
  GeoJSON files (FeatureCollection) and coordinate columns (lat/lon, x/y), converted to WGS84 with Proj4Net
  (source EPSG from the SRID of the value or chosen in the UI). Lines/polygons are indexed as point on surface
  plus a bounding box field (`<field>_bbox`), the original geometry can be stored as GeoJSON (`<field>_geojson`).
  Copy an index (mapping, metadata, documents) to another name or another instance. Alias management.
  Scheduled import jobs (cron, time zone) from a database query: rebuild a new index generation and switch an
  alias (no downtime, keeps N older generations, a failed run leaves the alias unchanged), clear and refill or append;
  run history, run now, cancel.
  Audit log of all changing actions (index, mapping/metadata, import, instances, connections, login),
  with filter page and retention (`Admin:Audit:RetentionDays`, default 180).
- API v2 endpoints for administration: `GET /api/v2/indices` (index names),
  `GET /api/v2/indices/{id}/stats` (documents, size on disk, last modification),
  `GET /api/v2/admin/info` (version, uptime, platform; role `admin`) and
  `GET /api/v2/admin/whoami` (roles of the calling client) and
  `GET /api/v2/indices/{id}/documents/_export` (all documents as NDJSON, stored fields; role `read`).
- `LuceneServerNET.Client`: new `LuceneServerV2Client` for the v2 API (not bound to one index;
  `LuceneServerClient` is unchanged), incl. `ExportDocumentsAsync` (streamed).

### Changed

- Engine: errors of the background refresh/commit and of releasing indices are logged via `ILogger`
  (before: `Console.Error`), so they show up in structured logs / OpenTelemetry.
- New config `LuceneServer:BackgroundCommitSeconds` (default `20`): interval of the background
  refresh + commit of all open indices.

- **`LuceneServerNET.Client`: refresh on dispose made configurable and safer** (not breaking):
  - New property `RefreshChangedIndicesOnDispose` (default `true` = previous behavior).
  - Implements `IAsyncDisposable`; use `await using`. `Dispose()` still works and no longer blocks
    on async code in the caller's synchronization context; it is idempotent and never throws.
  - No more `Console.WriteLine` output; optional `Logger` property (`ILogger`) instead.
    `cmd/ImportDbTable` passes a console logger.
  - Without a passed `HttpClient`, all instances share one internal `HttpClient` instead of creating
    (and never disposing) one per instance.
  - New dependencies: `Microsoft.Bcl.AsyncInterfaces`, `Microsoft.Extensions.Logging.Abstractions`.

- Error handling: errors are logged only via `ILogger` (no more `Console.WriteLine`), `milliSeconds` is measured with `Stopwatch`.
- New config `LuceneServer:MaxRequestBodySizeBytes` (default 30 MB, Kestrel and IIS) limits request bodies, e.g. for indexing.
- New config `LuceneServer:UseHttpsRedirection` (default `false`) enables HTTP → HTTPS redirection.
- A warning is logged if the OpenAPI/Scalar UI is enabled outside `Development` (`useSwagger: true`).

- **OpenAPI via `Microsoft.AspNetCore.OpenApi` and [Scalar](https://scalar.com) instead of Swashbuckle.**
  ASP.NET Core no longer ships Swagger by default; the documents are now generated by the
  framework (OpenAPI 3.1). Swashbuckle is removed.
  - Documents: `/openapi/v1.json`, `/openapi/v2.json`; UI: `/scalar` (Basic auth supported in "Try it").
  - Old URLs `/swagger` and `/swagger/{v}/swagger.json` redirect to the new ones.
  - Config key `useSwagger` is unchanged.

### Changed (breaking)

- **API v2 returns errors with HTTP status codes** instead of HTTP 200:
  `400` (invalid query/`ParseException`, invalid filter, invalid parameters such as `from + size` > 10000),
  `404` (index not found), `409` (index already exists / is unloading), `500` (other errors).
  The body is still `{"success": false, "message": "..."}`. **v1 (`/Lucene/...`) is unchanged and keeps HTTP 200.**
  For client errors (4xx) the message is now returned also in production (before: only the exception type).


- **State-changing endpoints no longer use HTTP GET.** GET requests must be safe; prefetchers,
  link previews, crawlers and CSRF (`<img src=…>`) could create or delete indices.
  - `POST /Lucene/createindex/{id}`, `DELETE /Lucene/removeindex/{id}`,
    `DELETE /Lucene/remove/{id}?term=…&termField=…`, `POST /Lucene/refresh/{id}`, `POST /Lucene/releaseall`.
  - Transition: the old GET routes still work while `LuceneServer:AllowLegacyGetMutations` is `true`
    (default). They are marked `[Obsolete]`, return `Deprecation: true` and `Warning: 299` headers and
    log a warning. With `false` they return `405 Method Not Allowed`.
  - `LuceneServerNET.Client` 3.0.0 uses the new methods and therefore requires a v3 server.

### Security

- **Path traversal protection for index and metadata names.** Index ids from the route
  (e.g. `removeindex/..`) and custom metadata names (e.g. `getmeta/{id}?name=../../x`) were
  passed unchecked into `Path.Combine` and could read, create or delete files and directories
  outside of `RootPath` / `ArchivePath`.
  - Index ids and metadata names must now match `^[a-z0-9_-]{1,64}$` (ids are lower-cased first).
  - Validation happens centrally in the controller and again in the engine
    (`LuceneService`, `LuceneSharedResourcesService`, `ArchiveService`).
  - All file system paths are built via the new `PathGuard.Combine`, which ensures the resolved
    path stays below the configured root directory.
  - `releaseall` no longer uses the `"*"` placeholder id; every route id is validated without exception.

### Breaking Changes

- Index ids and metadata names containing characters other than `a-z`, `0-9`, `_`, `-`
  (e.g. `.`, spaces, umlauts) or longer than 64 characters are now rejected.
  - **REST clients:** such requests return an error result (`success: false`).
  - **NuGet client `LuceneServerNET.Client`:** no API change; affected calls receive the error result.
  - **Existing indices on disk:** files are not touched, but indices whose folder name does not
    match the rule are no longer reachable via the API until renamed.

### Fixed

- **Numbers independent of the server culture**: stored `double`/`single`/`int32` values were returned
  formatted with the server culture (`"47,0707"` on a german server) and numeric strings in documents were
  parsed with it (`"47.0707"` was indexed as `470707`). Values are now parsed and returned culture invariant
  (`"47.0707"`; still as string, like before). Stored-only numeric fields (`index: false`) also accept JSON values now.
  Indices with numeric strings indexed on a non-english server should be re-indexed.
- **`datetime` independent of the server time zone**: dates without time zone were interpreted as server
  local time and shifted to UTC (`1128-01-01` became `1127-12-31T23:00:00` on a server in CET) and were returned
  without `Z`. Now: values without time zone are UTC, values with `Z`/offset are converted, results are UTC
  with `Z`. Stored-only date fields are written as ISO 8601 (older culture formatted values are still read).
- `group`: group values are decoded with `BytesRef.Utf8ToString()` (respects offset/length) instead of
  decoding the whole underlying byte buffer. No visible effect with the current Lucene.NET version
  (it returns exact copies), but no longer relies on that implementation detail.

- **Cologne phonetics:** a `c` at the end of a word was ignored; it is now encoded as `8`
  (`Novac` → `638`, before `63`).
- **Soundex:** the code of the first letter is now used to suppress an identical following code
  (`Pfister` → `P236`, before `P123`), and vowels now separate identical codes while `h`/`w` do not
  (`Tymczak` → `T522`, before `T520`).
- ⚠️ Both fixes change the stored phonetic codes. Existing indices keep the old codes until they are
  re-indexed: e.g. a misspelled search for `Nowac` no longer finds `Novac` via `searchphonetic`
  until the index is rebuilt. Only the phonetic fields are affected, not the normal search.

- `GeoPoint(latitude, longitude)` constructor set `Latitude` twice; `Longidute` stayed `0`.
- `GeoType.Parse` never parsed coordinate strings (inverted `IsNullOrEmpty` check). Strings are now
  read as `"lon lat"` or `"lon,lat"` (same order as `GeoPoint.ToString()`); invalid strings are ignored
  instead of throwing.

- The reserved metadata name `mapping` is now actually blocked (the old check compared against
  `"mapping "` with a trailing space).
