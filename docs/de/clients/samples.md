# Beispielanwendungen

Das Repository enthält im Ordner `test/` zwei kleine Beispielanwendungen. Beide erwarten einen
Server unter `https://localhost:44393`; die URL im Quellcode anpassen.

## ConsoleClient

`test/ConsoleClient` liest RSS-Feeds und zeigt, wie man

- einen Index anlegt und mappt (`CreateIndex()`),
- Dokumente in Blöcken indiziert (`IndexItems()`),
- Werte eines Feldes gruppiert (`Group()`).

```bash
dotnet run --project test/ConsoleClient
```

## WebClient

`test/WebClient` ist eine ASP.NET-Core-Razor-Pages-Anwendung mit einem *Autocomplete*-Suchfeld.
Der Page-Handler wandelt die Eingabe mit `QueryBuilder.ParseTerm()` um und ruft `SearchAsync()` auf:

```csharp
var result = await _client.SearchAsync($"{ new QueryBuilder().ParseTerm(term) }");
```

```bash
dotnet run --project test/WebClient
```
