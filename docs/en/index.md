# LuceneServer.NET

**LuceneServer.NET** is a REST server on top of [Apache Lucene.NET](https://github.com/apache/lucenenet).
It hosts any number of search indices and makes them available through simple GET/POST requests.

- Written in C# on **.NET 10**
- Full-text search with Lucene query syntax
- **Phonetic search** (Soundex, Cologne phonetics)
- **Geo filters** (bounding box, distance, distance to line) and GeoJSON output
- Grouping (`DISTINCT`-like) of field values
- Optional **Basic authentication** with roles (`read`, `write`, `admin`)
- .NET client library [LuceneServerNET.Client](https://www.nuget.org/packages/LuceneServerNET.Client/)
- Command line tools to fill an index from existing databases

## Motivation

The goal is a quick and easy search. Performance should be high enough to search in real time
from *autocomplete* input fields, even in large amounts of data.

Typical use cases:

- Search in product lists
- Search in large geo data sets such as addresses, plots, etc.
- Indexing documents

The search index is meant for fast lookups. A hit can then be linked to the actual data.
It is **not a replacement for a database**, it only provides a performant and usable search.
The included [tools](tools.md) fill an index from existing (relational) databases, e.g. periodically;
database keys stored in the index link back to the original records.

## Where to go next

| | |
|---|---|
| [Installation](installation/index.md) | Build, run and host the server |
| [Configuration](configuration/index.md) | Index paths, restore, Swagger |
| [Authentication](configuration/authentication.md) | Clients, secrets and roles |
| [REST API](api.md) | All endpoints |
| [.NET client](clients/dotnet-client.md) | Use the server from C# |
