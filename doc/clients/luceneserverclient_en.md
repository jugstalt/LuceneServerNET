# LuceneServerClient

The `LuceneServerClient` class is part of a .NET Standard library and provides easy access to the REST interface of the **LuceneServer.NET**.

The class is in the `LuceneServerNET.Client.dll` and can be added to existing projects via [nuget.org](https://www.nuget.org/packages/LuceneServerNET.Client/). The source code is also included in 
this repository.

## Instantiation

A client can be instantiated as follows:

```csharp
using(var client = new LuceneServerClient(serverUrl, indexName)) 
{
    // client operations
}
```

The client should be created with 'using' when changes are made to the index within the client (create or delete document).
`Dispose()` ensures that the index on the server is *refreshed* and that all changes become visible. If the client is used only for querying,
`using` can also be omitted.

A client always refers to one index. The corresponding index can be specified via the `indexName`.

To communicate with the LuceneServerNET, the client uses an instance of `System.Net.Http.HttpClient`. Each instance creates a new `HttpClient`.
If an existing `HttpClient` is used, it can be passed as an additional parameter during instantiation:


```csharp
using(var client = new LuceneServerClient(serverUrl, indexName, myHttpClient)) 
{
    // client operations
}
```

This ensures that, for example, *Authorize headers* are passed to the server.

## Methods

The following methods are used for index management:

```csharp
// Check if index exists
if (await client.IndexExistsAsync())
{
    // if true: remove existsing index
    await client.RemoveIndexAsync();
}
// create a new index
await client.CreateIndexAsync();
```

To map an index, a `MappingIndex` object must first be created. This then specifies the fields and primary search fields. The `MapAsync` function sets the *mapping' for the index:

```csharp
var mapping = new IndexMapping()
{
    // primary search field for the index
    PrimaryFields = new string[] { "title", "content" },
    Fields = new FieldMapping[]
    {
        //  create _guid field, so that documents can be queried or deleted individually
        new DocumentGuidField(),
        // Inxeded (queryable) fields, default-type: FieldTypes.TextType
        new IndexField("title"),
        new IndexField("content"),
        new IndexField("feed_id", FieldTypes.StringType),
        new IndexField("publish_date", FieldTypes.DateTimeType),
        // stored (not queryable) fields
        new StoredField("url"),
        new StoredField("image"),
    }
};

// Map
await client.MapAsync(mapping);
```

To index documents, they must be passed to the client as `IEnumerable<IDictionary<string, object>>`.
A value for the *DocumentGuid* field `_guid` is not passed here, but is set automatically by the server.

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

The following methods can be used to delete documents:

```csharp
// Löschen über die _guid der Dokument: Task<bool> RemoveDocumentsAsync(IEnumerable<Guid> guids)
await client.RemoveDocumentsAsync(new Guid[] { guid_doc1, guid_doc2 /*, ...*/  });

// Löschen über Query-Term: Task<bool> RemoveDocumentsAsync(string field, string term)
await client.RemoveDocumentsAsync("title", "Batman");  // title contains Batman
```

The search is implemented in the function `Task<LuceneSearchResult> SearchAsync(string query, IEnumerable<string> outFields = null)`.
The *Query* syntax is the same as that of Lucene. Examples can be found [here](https://lucene.apache.org/core/2_9_4/queryparsersyntax.html)

Primary searches in the fields listed in the mapping. However, the *Query* syntax can also be used to search in all other indexed fields.

For simple full-text searches where the user enters keywords, it is recommended to adjust the input:

Input: `Batm Robi`

Query: `+Batm* +Robi*` => both search terms must occur (+), search with wildcard => alle Dokumente includes `Batman` and `Robin` will be listet.    

The `TermParser` class can be used to convert to this syntax:

```csharp
var termParser = new TermParser();
var result = await client.SearchAsync($"{ termParser.Parse(term) }");

return result.Hits;
```

In addition to the term, you can specify the fields that should be returned.
For the ``outfields`` also special functions are aveilable, see [REST](./.. /rest/interface_de.md) interface description.

```csharp
var result = await client.SearchAsync(term,
                                      outFields: new[]
                                      {
                                        "*",  // all fields
                                        "content".SentencesWith("batman robin",1,1),
                                        "content".IncludedTerms("batman robin").As("incl")
                                      },
                                      size: 50,
                                      sortField: "publish_date",
                                      sortReverse: true);

```

