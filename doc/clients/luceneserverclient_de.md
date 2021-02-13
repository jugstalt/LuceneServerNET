# LuceneServerClient

Die `LuceneServerClient` Klasse ist Teil einer .NET Standard Bibliothek und bietet eine einfache Abstration für den Zugriff auf die REST Schnittstelle der **LuceneServer.NET**.

Die Klasse befindet sich in der `LuceneServerNET.Client.dll` und kann über [nuget.org](https://www.nuget.org/packages/LuceneServerNET.Client/) in bestehende Projekte hinzugefügt werden. Der Sourcecode befindet sich ebenfalls in 
diesem Repository.

## Instanzierung

Ein Client kann folgendermaßen instanziert werden:

```csharp
using(var client = new LuceneServerClient(serverUrl, indexName)) 
{
    // client operations
}
```

Der Client sollte in Verbindung mit `using` Verwendet werden,  wenn innerhalb der Client Änderungen am Index vornimmt (Dokument erzeugen oder löschen).
Beim `Dispose()` wird gewährleistet, dass der Index am Server *refreshed* wird und alle Änderungen sichtbar werden. Wird der Client nur zum Abfragen verwendet,
kann ein `using` auch entfallen.

Ein Client bezieht sich immer auf einen Index. Der entsprechende Index kann über den `indexName` übergeben werden.

Für die Kommunikation mit den LuceneServerNET verwendet der Client eine Instanz von `System.Net.Http.HttpClient`. Bei jeder Instanzierung wird ein neuer `HttpClient` erzeugt.
Sollte ein bestehender `HttpClient` verwendet werden, kann dieser als zusätzlicher Parameter bei der Instanzierung übergeben werden:

```csharp
using(var client = new LuceneServerClient(serverUrl, indexName, myHttpClient)) 
{
    // client operations
}
```

Damit kann gewährleistet werden, dass beispielsweise *Authoriation-Header* an den Server übergeben werden.

## Methoden

Folgende Methoden dienen der Index Verwaltung

```csharp
// Überprüfen, ob Index existiert
if (await client.IndexExistsAsync())
{
    // wenn ja: bestehenden Index entfernen
    await client.RemoveIndexAsync();
}
// Index anlegen
await client.CreateIndexAsync();
```

Für das Mapping eines Index muss zuerst ein `MappingIndex` Objekt erzeugt werden. In diesem werden dann die Felder und primären Suchfelder angegeben. Mit der Funktion ``MapAsync`` wird das *Mapping" für den Index festgelegt:

```csharp
var mapping = new IndexMapping()
{
    // In diesen Feldern wird standardmäßig gesucht
    PrimaryFields = new string[] { "title", "content" },
    Fields = new FieldMapping[]
    {
        // _guid Feld anlegen, damit Dokument einzeln abgefragt oder gelöscht werden können
        new DocumentGuidField(),
        // Indizierte (suchbare) Felder, default-type: FieldTypes.TextType
        new IndexField("title"),
        new IndexField("content"),
        new IndexField("feed_id", FieldTypes.StringType),
        new IndexField("publish_date", FieldTypes.DateTimeType),
        // Gespeicherte (nicht suchbare) Feldere
        new StoredField("url"),
        new StoredField("image"),
    }
};

// Map
await client.MapAsync(mapping);
```

Zum Indexieren von Document müssen diese als `IEnumerable<IDictionary<string, object>>` an den Client übergeben werden.
Ein Wert für das *DocumentGuid* Feld `_guid` wird hier nicht übergeben, sondern wird vom Server automatisch gesetzt.

```csharp
var doc = new Dictionary<string, object>();

doc["title"] = "My Documents Title";
doc["content"] = "Lorem Ipsum...";
doc["feed_id"] = "an_id_for_my_feed";
doc["publish_date"] = DateTime.Now;
doc["url"] = "https://my-article.com";
doc["image"] = "https://my-article.com/preview.jpg";

await client.IndexDocumentsAsync(new IDictionary<string, object>[] {
    doc
});
```

Für das Löschen von Dokumenten können folgende Methoden verwendet werden.

```csharp
// Löschen über die _guid der Dokument: Task<bool> RemoveDocumentsAsync(IEnumerable<Guid> guids)
await client.RemoveDocumentsAsync(new Guid[] { guid_doc1, guid_doc2 /*, ...*/  });

// Löschen über Query-Term: Task<bool> RemoveDocumentsAsync(string field, string term)
await client.RemoveDocumentsAsync("title", "Batman");  // title contains Batman
```

Für die Suche steht die Funktion ``Task<LuceneSearchResult> SearchAsync(string query, IEnumerable<string> outFields = null)`` zur Verfügung.
Die *Query* Syntax entspricht der von Lucene. Beispiele finden sich [hier](https://lucene.apache.org/core/2_9_4/queryparsersyntax.html)

Primär wird in den beim Mapping angeführten Felder gesucht. Über die *Query* Syntax kann allerdings auch in allen anderen indexierten Feldern gesucht werden.

Für einfache Volltextsuchen, bei denen der Anwender Schlagwörter eingibt, empfiehlt es sich, die Eingabe noch anzupassen:

Eingabe: `Batm Robi`
Query: `+Batm* +Robi*` => Beide Suchbegriffe müssen vorkommen (+), Suche mit Wildcard => alle Dokumente mit `Batman` und `Robin` werden gefunden    

Für die Umwandlung in diese Syntax kann die `TermParser` Klasse verwendet werden:

```csharp
var termParser = new TermParser();
var result = await _client.SearchAsync($"{ termParser.Parse(term) }");

return result.Hits;
```

