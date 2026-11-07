# LuceneServer.NET

[![NuGet](https://img.shields.io/nuget/v/LuceneServerNET.Client?label=LuceneServerNET.Client&logo=nuget)](https://www.nuget.org/packages/LuceneServerNET.Client/)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/jugstalt/LuceneServerNET)](LICENSE)
[![Docs](https://img.shields.io/badge/docs-docs.gviewonline.com-blue)](https://docs.gviewonline.com/other/luceneserver-net/index.html)

**A REST server on top of [Apache Lucene.NET](https://github.com/apache/lucenenet)** – fast full-text,
phonetic and geo search for autocomplete fields, product lists, addresses and documents.

- Full-text search with Lucene query syntax, phonetic search (Soundex, Cologne phonetics)
- Geo filters (bbox, distance) and GeoJSON output
- Basic authentication with `read` / `write` / `admin` roles
- .NET client library and import tools for relational databases

## Quickstart

```bash
git clone https://github.com/jugstalt/LuceneServerNET.git
cd LuceneServerNET
dotnet publish src/LuceneServerNET -c Release -o ./publish
cd publish
ASPNETCORE_URLS=http://localhost:5000 dotnet LuceneServerNET.dll
```

Create an index, map it, add a document and search:

```bash
curl http://localhost:5000/Lucene/createindex/books
curl -X POST http://localhost:5000/Lucene/map/books -H "Content-Type: application/json" \
     -d '{"fields":[{"name":"title","fieldType":"text","index":true,"store":true}],"primaryFields":["title"]}'
curl -X POST http://localhost:5000/Lucene/index/books -H "Content-Type: application/json" \
     -d '[{"title":"LuceneServerNET rules!"}]'
curl http://localhost:5000/Lucene/refresh/books
curl "http://localhost:5000/Lucene/search/books?q=rul*"
```

From .NET:

```bash
dotnet add package LuceneServerNET.Client
```

```csharp
using var client = new LuceneServerClient("http://localhost:5000", "books");
var result = await client.SearchAsync("rul*");
```

## Documentation

📖 **[docs.gviewonline.com](https://docs.gviewonline.com/other/luceneserver-net/index.html)** – English and German

[Installation](docs/en/installation/index.md) ·
[Configuration](docs/en/configuration/index.md) ·
[Authentication](docs/en/configuration/authentication.md) ·
[REST API](docs/en/api.md) ·
[.NET client](docs/en/clients/dotnet-client.md) ·
[Tools](docs/en/tools.md) ·
[Changelog](ChangeLog.md)

Build the docs locally: `./build-docs.ps1 -NoCopy` (output in `artifacts/docs/site`).

## License

See [LICENSE](LICENSE).
