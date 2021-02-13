# LuceneServer.NET

Hinweis: Diese Projekt befinded sich noch in einen sehr frühen Entwicklungsstadium. Es kann noch zu größeren Umbauten der Schnittstelle kommen.

## Ein REST Server auf Basis von Lucene.NET

LuceneServer bietet eine REST Schnittstelle, mit der Suchindices auf Basis von [Apache Lucene. NET](https://github.com/apache/lucenenet) erstellt werden können.
Der Sourcecode ist vollständig in *Dotnet Core* (C#) umgesetzt. Die REST Schnittstelle kann über einfache GET/POST Request angesprochen. 
LuceneServer.NET bietet zusätzlich einee Client Library [LucenceServerNET.Client](https://www.nuget.org/packages/LuceneServerNET.Client/), mit die Request abstrahiert werden können.
Der Sourcecode für den Client befindet sich ebenfalls in diesem Repository.

## Motivation

Ziel ist es eine schnelle und einfache Suche zu implementieren. Die Performance sollte hoch genug sein, um auch in großen Datenmengen in Echtzeit mit *Autocomplete* Eingabefeldern zu suchen.
Beispiele für einen Suchindex sind beispielsweise:

* Suchen in Produktlisten
* Suchen in großen Geo Daten Beständen wie Adressen, Grundstücken, usw.
* Indizieren von Dokumenten

Der Suchindex sollte dabei in erster Line für eine schnelle Suche verwendet werden. Über die gefunden Element kann dann auf die tatsächlichen Daten verlinkt werden. Der Suchindex ist vorrangig kein Ersatz für eine Datenbank,
sondern trägt lediglich zur einer performanten und bedienbaren Suche bei. 

Ebenso im diesem Repository findet man Tools (Konsolenanwendungen), mit denen ein Index über bestehende (rationale) Datenbanken befüllt werden kann. Dies kann beispielsweise regelmäßig erfolgen. Über Datenbank Schlüssel kann dann 
wieder eine Verbindung zum ursprünglichen Datenbankobjekt hergestellt werden. 

[REST Schnittstelle](./rest/interface_de.md)

[LuceneServerClient](./clients/luceneserverclient_de.md)