# Authentication

By default the server is **open**: every request is allowed. Set `Authorization:Type` to `Basic`
to require HTTP Basic authentication.

```json
"Authorization": {
  "Type": "Basic",
  "Clients": [
    {
      "Id": "reader",
      "SecretHash": "pbkdf2-sha256$...",
      "Roles": [ "read" ]
    },
    {
      "Id": "importer",
      "SecretHash": "pbkdf2-sha256$...",
      "Roles": [ "write" ]
    },
    {
      "Id": "admin",
      "SecretHash": "pbkdf2-sha256$...",
      "Roles": [ "admin" ]
    }
  ]
}
```

!!! warning
    Basic authentication sends credentials with every request. Always use **HTTPS**.

## Secrets

Store hashed secrets (`SecretHash`). Create a hash with:

```bash
dotnet LuceneServerNET.dll --hash-secret "my-secret"
```

The command prints the hash and exits. Plain text secrets (`Secret`) are still supported for
backward compatibility; the server logs a warning on startup.

## Roles

| Role | Allowed |
|---|---|
| `read` | `search`, `searchphonetic`, `group`, `indexexists`, `mapping`, `getmeta`, `getmetas`, `getmetanames`, `/info` |
| `write` | `read` + `createindex`, `map`, `index`, `remove`, `refresh`, `addmeta` |
| `admin` | `write` + `removeindex`, `releaseall` |

Higher roles include lower ones. A client **without roles** gets `admin` (backward compatible);
the server logs a warning. If `Basic` is enabled but no clients are configured, all requests are rejected.

## Calling the server

```bash
curl -u reader:my-secret "https://search.example.com/Lucene/search/products?q=bat*"
```

The [.NET client](../clients/dotnet-client.md) accepts the credentials as constructor parameters
or in the URL (`https://id:secret@host`).
