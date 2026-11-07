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
| Import | From a database query or upload CSV or JSON (array or NDJSON), preview, mapping **proposed from the first 200 rows** (new index) or columns assigned to the existing mapping, configurable batch size, cancel, error report |
| Metadata | Show and edit custom metadata |
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

## Configuration

```jsonc
{
  "Admin": {
    "DataPath": "_data",            // SQLite database (admin.db) + data protection keys
    "Import": { "MaxFileSizeMB": 500, "CommandTimeoutSeconds": 600 },
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
