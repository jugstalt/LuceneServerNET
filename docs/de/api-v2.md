# REST-API v2

Die API v2 ist ressourcenorientiert und liegt unter `/api/v2/indices`. Sie läuft **parallel** zur
[API v1](api.md) (`/Lucene/...`): Beide arbeiten auf denselben Indizes mit derselben Engine,
bestehende v1-Clients funktionieren unverändert weiter. v1 gilt als *deprecated*; neue Clients sollten v2 verwenden.

Antworten, Rollen, Query-Syntax, `outFields`-Funktionen, Geo-Filter und `format` sind wie in
[v1](api.md#dokumente). Jede Version hat ein eigenes OpenAPI-Dokument (`/openapi/v1.json`, `/openapi/v2.json`); die API-Referenz-UI (`/scalar`) zeigt beide.

## Überblick

| Methode | Endpunkt | Rolle | Entspricht in v1 |
|---|---|---|---|
| PUT | `/api/v2/indices/{index}` | write | `POST /Lucene/createindex/{index}` |
| DELETE | `/api/v2/indices/{index}` | admin | `DELETE /Lucene/removeindex/{index}` |
| GET / HEAD | `/api/v2/indices/{index}` (404, falls nicht vorhanden) | read | `GET /Lucene/indexexists/{index}` |
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

## Suche und Paging

**[GET] /api/v2/indices/{index}/search?q=&outFields=&from=0&size=20&searchAfter=&sortField=&sortReverse=&filter=&format=**

| Parameter | Beschreibung |
|---|---|
| `from` | Offset des ersten Treffers (Standard `0`). `from + size` darf **10000** nicht überschreiten. |
| `size` | Seitengröße (Standard `20`). |
| `searchAfter` | Cursor für die nächste Seite: Wert von `nextSearchAfter` aus der vorigen Antwort. Nur bei Relevanz-Sortierung (ohne `sortField`), nicht mit `from` kombinierbar. |

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

- `totalHits` ist die Gesamtzahl der Treffer, unabhängig von `from`/`size`.
- `nextSearchAfter` ist `null`, wenn die Seite weniger als `size` Treffer hat oder `sortField` verwendet wird.
- **Offset-Paging** (`from`) ist einfach und passt für Ergebnislisten mit Seitenzahlen.
  Intern werden `from + size` Treffer gesammelt, tiefe Seiten werden daher teuer (deshalb das Limit).
- **Cursor-Paging** (`searchAfter`) ist effizient für tiefes Blättern bzw. Exporte.
  Der Cursor ist nur stabil, solange der Index zwischenzeitlich nicht verändert wird.
- `format={geo-feld}:geojson` liefert GeoJSON ohne Paging-Informationen.

Dieselben Paging-Parameter und Antwortfelder gibt es auch in v1
(`/Lucene/search/{index}` und `/Lucene/searchphonetic/{index}`).
