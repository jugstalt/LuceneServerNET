# LuceneServer.NET

**LuceneServer.NET** ist ein REST-Server auf Basis von [Apache Lucene.NET](https://github.com/apache/lucenenet).
Er verwaltet beliebig viele Suchindizes und stellt sie über einfache GET/POST-Requests bereit.

- Umgesetzt in C# auf **.NET 10**
- Volltextsuche mit Lucene-Query-Syntax
- **Phonetische Suche** (Soundex, Kölner Phonetik)
- **Geo-Filter** (Bounding Box, Distanz, Distanz zu einer Linie) und Ausgabe als GeoJSON
- Gruppierung (ähnlich `DISTINCT`) von Feldwerten
- Optionale **Basic-Authentifizierung** mit Rollen (`read`, `write`, `admin`)
- .NET-Client-Bibliothek [LuceneServerNET.Client](https://www.nuget.org/packages/LuceneServerNET.Client/)
- Kommandozeilen-Tools zum Befüllen eines Index aus bestehenden Datenbanken

## Motivation

Ziel ist eine schnelle und einfache Suche. Die Performance soll hoch genug sein, um auch in großen
Datenmengen in Echtzeit aus *Autocomplete*-Eingabefeldern zu suchen.

Typische Anwendungsfälle:

- Suchen in Produktlisten
- Suchen in großen Geodatenbeständen wie Adressen, Grundstücken usw.
- Indizieren von Dokumenten

Der Suchindex dient in erster Linie dem schnellen Auffinden. Über die Treffer wird auf die
eigentlichen Daten verlinkt. Er ist **kein Ersatz für eine Datenbank**, sondern sorgt für eine
performante und bedienbare Suche. Mit den enthaltenen [Tools](tools.md) kann ein Index aus bestehenden
(relationalen) Datenbanken befüllt werden, z. B. regelmäßig; über im Index gespeicherte
Datenbankschlüssel gelangt man zurück zum Originaldatensatz.

## Weiterlesen

| | |
|---|---|
| [Installation](installation/index.md) | Server bauen, starten und hosten |
| [Konfiguration](configuration/index.md) | Indexpfade, Wiederherstellung, Swagger |
| [Authentifizierung](configuration/authentication.md) | Clients, Secrets und Rollen |
| [REST-API](api.md) | Alle Endpunkte |
| [.NET-Client](clients/dotnet-client.md) | Server aus C# verwenden |
