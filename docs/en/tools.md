# Tools

The `cmd/` folder contains two console applications that use the [.NET client](clients/dotnet-client.md).
Build them with `dotnet publish cmd/CreateIndex` or `dotnet publish cmd/ImportDbTable`.

!!! note
    If the server requires authentication, pass the credentials in the server URL:
    `-s https://importer:my-secret@search.example.com`.

## CreateIndex

Creates an index and its mapping.

```text
CreateIndex -server[-s] server
            -index[-i] indexname
            -field[-f] fieldname[.fieldtype][.stored|not_stored]   // indexed field, default .text.stored
            -sfield fieldname[.fieldtype]                          // stored only field, default .text
            -primary field1,field2                                 // primary search fields, default: first field
            -remove                                                // remove an existing index first
```

Field types: `text`, `string`, `int32`, `single`, `double`, `datetime`, `geo`, `guid`.

```powershell
.\CreateIndex.exe -s https://localhost:44393 `
                  -i new-index `
                  -f suggested_text `
                  -f sub_text `
                  -f category `
                  -sfield id
```

## ImportDbTable

Reads rows from a database and indexes them (in batches of 1000).

```text
ImportDbTable -server[-s] {server}
              -index[-i] {indexname}
              -db-type {PostGres|Oracle|SqlServer|Sqlite}
              -db-connectionstring[-db] {connectionString}
              -sql-statement[-sql] {sqlStatement}
              -f {indexfield} {expression}     // expression: "lorem {{DB_FIELD1}} ipsum {{DB_FIELD2}}"
```

Each `-f` maps an index field to an expression; `{{COLUMN}}` is replaced with the column value.

```powershell
.\ImportDbTable.exe -s https://localhost:44393 -i addresses `
    -db-type SqlServer `
    -db "Server=.;Database=gis;Trusted_Connection=True" `
    -sql "SELECT id, street, nr, city FROM addresses" `
    -f suggested_text "{{street}} {{nr}}" `
    -f sub_text "{{city}}" `
    -f id "{{id}}"
```

The index must exist and be mapped first (e.g. with `CreateIndex`).
