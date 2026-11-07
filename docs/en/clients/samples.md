# Sample applications

The repository contains two small sample applications in the `test/` folder. Both expect a
server at `https://localhost:44393`; adjust the URL in the source code.

## ConsoleClient

`test/ConsoleClient` reads RSS feeds and shows how to

- create and map an index (`CreateIndex()`),
- index documents in batches (`IndexItems()`),
- group values of a field (`Group()`).

```bash
dotnet run --project test/ConsoleClient
```

## WebClient

`test/WebClient` is an ASP.NET Core Razor Pages app with an *autocomplete* search box.
The page handler converts the user input with `QueryBuilder.ParseTerm()` and calls `SearchAsync()`:

```csharp
var result = await _client.SearchAsync($"{ new QueryBuilder().ParseTerm(term) }");
```

```bash
dotnet run --project test/WebClient
```
