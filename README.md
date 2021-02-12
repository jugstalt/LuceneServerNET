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

The server provides the following endpoints via REST:
