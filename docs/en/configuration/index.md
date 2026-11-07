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
    The template uses keys with a trailing underscore (`ArchivePath_`, `Authorization_`).
    These are ignored. Remove the underscore to activate a section.

## LuceneServer

| Key | Description |
|---|---|
| `RootPath` | Directory containing the index folders. Required. |
| `ArchivePath` | Optional. If set, mapping, metadata and every indexed document are additionally written here as JSON files, so an index can be rebuilt. |
| `AutoRestoreOnStartup` | On startup, rebuild archived indices that no longer exist in `RootPath` (e.g. after a container restart with non-persistent storage). |
| `AutoRestoreOnStartupCount` | If `> 0`: restore only the last *n* archived documents per index. |
| `AutoRestoreOnStartupSinceSeconds` | If `Count` is `0` and this is `> 0`: restore only documents archived within this period (default `86400` = 1 day). If both are `0`, all documents are restored. |

## Swagger

| Key | Description |
|---|---|
| `useSwagger` | `true` enables the Swagger UI at `/swagger`. Always on in `Development`. |

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
