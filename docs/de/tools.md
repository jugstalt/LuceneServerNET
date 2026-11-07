# Tools

Der Ordner `cmd/` enthält zwei Konsolenanwendungen, die den [.NET-Client](clients/dotnet-client.md) verwenden.
Gebaut werden sie mit `dotnet publish cmd/CreateIndex` bzw. `dotnet publish cmd/ImportDbTable`.

!!! note
    Verlangt der Server Authentifizierung, die Zugangsdaten in der Server-URL übergeben:
    `-s https://importer:my-secret@search.example.com`.

## CreateIndex

Legt einen Index samt Mapping an.

```text
CreateIndex -server[-s] server
            -index[-i] indexname
            -field[-f] fieldname[.fieldtype][.stored|not_stored]   // indiziertes Feld, Standard .text.stored
            -sfield fieldname[.fieldtype]                          // nur gespeichertes Feld, Standard .text
            -primary field1,field2                                 // primäre Suchfelder, Standard: erstes Feld
            -remove                                                // bestehenden Index vorher löschen
```

Feldtypen: `text`, `string`, `int32`, `single`, `double`, `datetime`, `geo`, `guid`.

```powershell
.\CreateIndex.exe -s https://localhost:44393 `
                  -i new-index `
                  -f suggested_text `
                  -f sub_text `
                  -f category `
                  -sfield id
```

## ImportDbTable

Liest Zeilen aus einer Datenbank und indiziert sie (in Blöcken zu 1000).

```text
ImportDbTable -server[-s] {server}
              -index[-i] {indexname}
              -db-type {PostGres|Oracle|SqlServer|Sqlite}
              -db-connectionstring[-db] {connectionString}
              -sql-statement[-sql] {sqlStatement}
              -f {indexfield} {expression}     // expression: "lorem {{DB_FIELD1}} ipsum {{DB_FIELD2}}"
```

Jedes `-f` ordnet einem Indexfeld einen Ausdruck zu; `{{SPALTE}}` wird durch den Spaltenwert ersetzt.

```powershell
.\ImportDbTable.exe -s https://localhost:44393 -i addresses `
    -db-type SqlServer `
    -db "Server=.;Database=gis;Trusted_Connection=True" `
    -sql "SELECT id, street, nr, city FROM addresses" `
    -f suggested_text "{{street}} {{nr}}" `
    -f sub_text "{{city}}" `
    -f id "{{id}}"
```

Der Index muss vorher angelegt und gemappt sein (z. B. mit `CreateIndex`).
