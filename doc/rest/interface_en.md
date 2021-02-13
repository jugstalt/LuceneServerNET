# REST Interface

The server provides the following endpoints via REST. {index} always is the name of the index.
The return value is always of type `IApiResult`:

```javascript
IApiResult {
    success	      boolean,
    milliSeconds	number($double)
}
```

Depending on the context, there are additional attributes, e.g. a  `hits'[]` for queries.

## Index Managment

**[GET] /Lucene/createindex/{index}**

Creates an index named {index}

**[GET] /Lucene/removeindex/{index}**

Permanently deletes the index

**[GET] /Lucene/indexexists/{index}**

Checks whether an index exists.

**[GET] /Lucene/refresh/{index}**

Leads to a refresh of the listed index. This ensures that after adding documents, you can search for the newly indexed documents.
Using the `LuceneServerClient` this call is made automatically when the client is unloaded (`Dispose()`).

**[GET] /Lucene/releaseall**

Releases all shared resources within the server. For example, the `IndexReader` or `IndexWriter`, which are shared across multiple queries in the server for optimal performance.
After releasing, the resources are reinitialized the next time you access an index.
This call is mainly necessary for development and should not be necessary in a productive environment.

## Mapping

This determines the structure of the documents for this index. The *Mapping* specifies which fields the documents can have for this index and whether they are indexed or are only additional information (without search options).

**[POST] /Lucene/map/{index}**

Request Body

```javascript
{
  "fields": [
    {
      "fieldType": "string",
      "name": "string",
      "store": true,
      "index": true
    }
  ],
  "primaryFields": [
    "string"
  ]
}
```

A Json File is passed in which each field is defined. Values for 'fieldType' are:

* `guid`:   creates a field (_guid) for which an id is automatically generated. this id can be used to explicitly search for or delete a document

* `text`: The field contains text that is indexed. Fields of this type can search for keywords or word fragments.

* `string`: A string to search for.

* `int32`, `single`, `double`: Integer or floating point numbers

* `datetime`: A date with time

For each field, you can specify whether it should be indexed and/or saved.
You can search for indexed fields. For *saved* fields, the content is also returned 1:n on a hit.

`primaryFields` must specify an array of column names in which a search should be searched by default. All other fields are searched only if explicitly stated in the query.


**[GET] /Lucene/mapping/{index}**

Here you can query the *mapping* for this index. The result is again an `IApiResult` object with an additional property `mapping`:

```javascript
{
  "mapping": {
    "fields": [
      {
        "fieldType": "guid",
        "name": "_guid",
        "store": true,
        "index": true
      },
      ...
    ],
    "primaryFields": [
      "title",
      "content"
    ]
  },
  "success": true,
  "milliSeconds": 12.2342
}
```

## Documents

An entry in the index is called a document. Documents can be indexed, queried, and deleted using the following interfaces.
If new documents are added to or deleted to an index, this will only become visible after a refresh (see above).

**[POST] /Lucene/index/{index}**

This allows individual documents to be transferred to the index. The fields of the documents passed must correspond to the *Mapping*. Unmapped fields are ignored.

Request Body

```javascript
[
  {   
      // any kind of properties
      "title": "my book title",
      "summary": "lorem ispum..."
  }
]
```

**[GET] /Lucene/remove/{index}?term={query-term}&termField={optinal:field-used-for-term}**

A query can be passed as `term`. The criteria from the query are searched in `termField` and the corresponding objects are deleted.

The *Default* value for termField is `_guid`. Ideally, in the *Mapping* you specify a field with the type `guid`. This is the only way to delete individual documents.

**[GET] /Lucene/search/{index}?q={query-term}&outFields={optional:fields-for-the-result-comma-separated}**

This method can be used to search for documents. The result is of type `IApiResult` with an additional `hits` attributes:

Example: 
/Lucene/search/{index}?q=rules
/Lucene/search/{index}?q=rules&outFields=_guid,title,publish_date

```javascript
{
  "hits": [
    {
      "_score": 1.4272848,
      "_guid": "256c3f501ef64327b81f4b8141a6f5ef",
      "title": "LuceneServerNET rules!",
      "publish_date": "2021-02-12T17:09:17"
    }
  ],
  "success": true,
  "milliSeconds": 1.8725
}
```

The *Query-Term* corresponds to the *Lucene* syntax, see also [here](https://lucene.apache.org/core/2_9_4/queryparsersyntax.html) 

**[GET] /Lucene/group/{index}?groupField={field}&q={optional:query-term}**

This method can be used to determine possible values of a field that optionally correspond to a *query term*. The method is similar to a `DISTINCT` in a database.
The `hits` are a *string array*.

Example: /Lucene/search/{index}?groupField=feed

```javascript
{
  "hits": [
    "derstandard",
    "diepresse",
    "kurier",
    "krone",
    "futurezone"
  ],
  "success": true,
  "milliSeconds": 81.4196
}
```