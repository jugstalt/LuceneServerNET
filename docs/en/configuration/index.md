# Configuration

The server reads the standard ASP.NET Core configuration sources (`appsettings.json`,
environment variables, command line) plus the file **`_config/luceneserver.json`**.
The file is created on the first start (see [Installation](../installation/index.md)).

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
    The template uses keys with a trailing underscore (`ArchivePath_`, `Authorization_`).
    These are ignored. Remove the underscore to activate a section.

## LuceneServer

| Key | Description |
|---|---|
| `RootPath` | Directory containing the index folders. Required. |
| `ArchivePath` | Optional, not set by default. If set, mapping, metadata and every indexed document are additionally written here as JSON files (one file per document), so an index can be rebuilt. This slows down indexing considerably (measured: about 26 times on Windows); only use it for indices that have no other source. Indices created with `?archive=false` (e.g. by import jobs) are never archived. |
| `BackgroundCommitSeconds` | Optional, default `20`. Interval (seconds) in which the server refreshes and commits all open indices in the background. Changes become visible to searches at the latest after this time, even without an explicit `refresh`. |
| `AutoRestoreOnStartup` | On startup, rebuild archived indices that no longer exist in `RootPath` (e.g. after a container restart with non-persistent storage). |
| `AutoRestoreOnStartupCount` | If `> 0`: restore only the last *n* archived documents per index. |
| `AutoRestoreOnStartupSinceSeconds` | If `Count` is `0` and this is `> 0`: restore only documents archived within this period (default `86400` = 1 day). If both are `0`, all documents are restored. |
| `AllowLegacyGetMutations` | Default `true`. Temporarily allows the deprecated **GET** calls of `createindex`, `removeindex`, `remove`, `refresh` and `releaseall` (response carries `Deprecation`/`Warning` headers and a warning is logged). If `false`, these GET calls return `405 Method Not Allowed`; only POST/DELETE are accepted. Will default to `false` in a future version. |
| `MaxRequestBodySizeBytes` | Max. request body size in bytes (e.g. when indexing), applies to Kestrel and IIS. Default `31457280` (30 MB). Larger requests are rejected; index large data sets in several calls. |
| `UseHttpsRedirection` | Default `false`. If `true`, HTTP requests are redirected to HTTPS. Only useful if the server itself serves HTTPS (not behind a TLS terminating proxy). |

## API documentation (OpenAPI)

| Key | Description |
|---|---|
| `useSwagger` | `true` publishes the OpenAPI documents (`/openapi/v1.json`, `/openapi/v2.json`) and the API reference UI ([Scalar](https://scalar.com)) at `/scalar`. Always on in `Development`. The old URLs `/swagger` and `/swagger/{v}/swagger.json` redirect to the new ones. These endpoints are accessible without authentication; outside `Development` a warning is logged at startup. |

## Authentication

The `Authorization` section is described in [Authentication](authentication.md).

## Environment variables

Every setting can be set as an environment variable, using `__` as separator:

```bash
LUCENESERVER__ROOTPATH=/var/lib/luceneserver/indices
LUCENESERVER__ARCHIVEPATH=/var/lib/luceneserver/archive
AUTHORIZATION__TYPE=Basic
AUTHORIZATION__CLIENTS__0__ID=admin
AUTHORIZATION__CLIENTS__0__SECRETHASH=pbkdf2-sha256$...
AUTHORIZATION__CLIENTS__0__ROLES__0=admin
```

On Linux, if `LUCENESERVER__ROOTPATH` is set, no `_config/luceneserver.json` is created on first start.

## Index names

Index names (and metadata names) must match `^[a-z0-9_-]{1,64}$`. Names are lower-cased first.
Other names are rejected with `success: false`.
