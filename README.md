# LuceneServerNET

[German](./README_de.md)

Note: This project is still at a very early stage of development. There may be even more major conversions of the interface.

## A REST Server on top of Lucene.NET

LuceneServer provides a REST interface, with the search indexes based on [Apache Lucene. NET](https://github.com/apache/lucenenet).
The source code is fully implemented in *Dotnet Core* (C#) The REST interface can be addressed via simple GET/POST request.
LuceneServer.NET also provides a client library [LucenceServerNET.Client](https://www.nuget.org/packages/LuceneServerNET.Client/) that can be used to abstract request.
The source code for the client is also in this repository.

## Motivation

The goal is to implement a quick and easy search. The performance should be high enough to search in real-time with *Autocomplete* input fields, even in large amounts of data.

Examples of a search index include:

* Search in product lists
* Search in large geo data stocks such as addresses, plots, etc.
* Indexing documents

The search index should be used in the first line for a quick search. The found item can then be linked to the actual data. The search index is primarily not a substitute for a database,
but only contributes to a performant and operable search. 

Also in this repository you will find tools (console applications) that can be used to populate an index through existing (rational) databases. This can be done periodically, for example. Database keys can then be used to 
link to the original database object. 

## REST Interface

The server provides the following endpoints via REST. {index} always is the name of the index.
The return value is always of type `IApiResult`:

<pre>
IApiResult {
    success	        boolean,
    milliSeconds	number($double)
}
</pre>

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

### Mapping

This determines the structure of the documents for this index. The *Mapping* specifies which fields the documents can have for this index and whether they are indexed or are only additional information (without search options).

**[POST] /Lucene/map/{index}**

Request Body

<pre>
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
</pre>

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

<pre>
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
</pre>