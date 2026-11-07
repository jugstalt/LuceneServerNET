# REST API v2

API v2 is resource oriented and lives below `/api/v2/indices`. It runs **in parallel** to the
[v1 API](api.md) (`/Lucene/...`): both use the same indices and the same engine, existing v1 clients
keep working unchanged. v1 is considered *deprecated*; new clients should use v2.

Responses, roles, query syntax, `outFields` functions, geo filters and `format` are the same as in
[v1](api.md#documents). Each version has its own OpenAPI document (`/openapi/v1.json`, `/openapi/v2.json`); the API reference UI (`/scalar`) shows both.

## Errors and HTTP status codes

Unlike v1 (always HTTP 200), v2 returns errors with a matching status code. The body is the same in both cases:

```json
{ "success": false, "message": "Cannot parse 'title:(': ..." }
```

| Status | Cause |
|---|---|
| `400` | Invalid query (Lucene syntax), invalid spatial filter, invalid parameters (e.g. `from + size` > 10000, invalid index name) |
| `404` | Index does not exist |
| `409` | Index already exists or is being unloaded |
| `413` | Request body too large (`LuceneServer:MaxRequestBodySizeBytes`) |
| `500` | Other errors (outside `Development` the message contains only the error type) |

## Overview

| Method | Endpoint | Role | v1 equivalent |
|---|---|---|---|
| PUT | `/api/v2/indices/{index}` | write | `POST /Lucene/createindex/{index}` |
| DELETE | `/api/v2/indices/{index}` | admin | `DELETE /Lucene/removeindex/{index}` |
| GET / HEAD | `/api/v2/indices/{index}` (404 if missing) | read | `GET /Lucene/indexexists/{index}` |
| POST | `/api/v2/indices/{index}/refresh` | write | `POST /Lucene/refresh/{index}` |
| POST | `/api/v2/indices/_release` | admin | `POST /Lucene/releaseall` |
| GET | `/api/v2/indices/{index}/mapping` | read | `GET /Lucene/mapping/{index}` |
| PUT | `/api/v2/indices/{index}/mapping` | write | `POST /Lucene/map/{index}` |
| GET | `/api/v2/indices/{index}/meta` | read | `GET /Lucene/getmetas/{index}` |
| GET | `/api/v2/indices/{index}/meta/_names` | read | `GET /Lucene/getmetanames/{index}` |
| GET | `/api/v2/indices/{index}/meta/{name}` | read | `GET /Lucene/getmeta/{index}?name=` |
| PUT | `/api/v2/indices/{index}/meta/{name}` | write | `POST /Lucene/addmeta/{index}?name=` |
| POST | `/api/v2/indices/{index}/documents` | write | `POST /Lucene/index/{index}` |
| DELETE | `/api/v2/indices/{index}/documents?term=&field=_guid` | write | `DELETE /Lucene/remove/{index}?term=&termField=` |
| GET | `/api/v2/indices/{index}/search` | read | `GET /Lucene/search/{index}` |
| GET | `/api/v2/indices/{index}/search/phonetic` | read | `GET /Lucene/searchphonetic/{index}` |
| GET | `/api/v2/indices/{index}/groups?field=&q=` | read | `GET /Lucene/group/{index}?groupField=&q=` |
| GET | `/api/v2/indices` (index names) | read | – |
| GET | `/api/v2/indices/{index}/stats` | read | – |
| GET | `/api/v2/indices/{index}/documents/_export` (NDJSON, header `X-Total-Count`) | read | – |
| GET | `/api/v2/aliases` (alias => index) | read | – |
| PUT | `/api/v2/aliases/{alias}` body `{ "index": "..." }` | write | – |
| DELETE | `/api/v2/aliases/{alias}` | write | – |
| GET | `/api/v2/admin/info` | admin | – |
| GET | `/api/v2/admin/whoami` | read | – |

## Search and paging

**[GET] /api/v2/indices/{index}/search?q=&outFields=&from=0&size=20&searchAfter=&sortField=&sortReverse=&filter=&format=**

| Parameter | Description |
|---|---|
| `from` | Offset of the first hit (default `0`). `from + size` must not exceed **10000**. |
| `size` | Page size (default `20`). |
| `searchAfter` | Cursor for the next page: value of `nextSearchAfter` of the previous response. Only for relevance sorting (no `sortField`), not combinable with `from`. |

```javascript
{
  "hits": [ ... ],
  "totalHits": 1234,
  "from": 20,
  "size": 20,
  "nextSearchAfter": "MTIzOjEuNDI3Mjg0OA",
  "success": true,
  "milliSeconds": 2.1
}
```

- `totalHits` is the total number of matching documents, independent of `from`/`size`.
- `nextSearchAfter` is `null` if the page has fewer than `size` hits or if `sortField` is used.
- **Offset paging** (`from`) is simple and suitable for result lists with page numbers.
  Internally `from + size` hits are collected, therefore deep pages get expensive (hence the limit).
- **Cursor paging** (`searchAfter`) is efficient for deep paging / exports.
  The cursor is only stable as long as the index is not changed in the meantime.
- `format={geo-field}:geojson` returns GeoJSON without paging information.

The same paging parameters and response fields are also available in v1
(`/Lucene/search/{index}` and `/Lucene/searchphonetic/{index}`).

## Aliases

An alias points to exactly one index. Wherever an index id is expected (v1 and v2: search, groups, documents,
mapping, metadata, stats, export, refresh), an alias can be used instead. Creating and removing indices always
use the real index name.

```bash
# build a new generation, then switch the alias atomically
curl -X PUT  http://localhost:8080/api/v2/indices/orte_20261107
# ... mapping, documents, refresh ...
curl -X PUT  http://localhost:8080/api/v2/aliases/orte -H "Content-Type: application/json" -d '{"index":"orte_20261107"}'
curl         "http://localhost:8080/api/v2/indices/orte/search?q=graz"
```

Rules: an alias can not have the name of an existing index (409) and must point to an index, not to another alias;
an index used by an alias can not be removed (409, remove or switch the alias first); `DELETE /api/v2/indices/{alias}`
returns 409. Aliases are stored in `{RootPath}/aliases.json` and copied to `{ArchivePath}/aliases.json`.
