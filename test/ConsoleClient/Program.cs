using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Parse;
using LuceneServerNET.Parse.Lexer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ConsoleClient
{
    class Program
    {
        static string serverUrl = "https://localhost:44393";
        static string indexName = "allgemein"; //"feedclient-news";
        static LuceneServerClient client = new LuceneServerClient(serverUrl, indexName);

        async static Task<int> Main(string[] args)
        {
            //try
            //{
            //    //await CreateIndex();
            //    //await IndexItems();

            //    await Group();

            //    Console.WriteLine("finished");
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine($"Error: { ex.Message }");
            //    Console.WriteLine("Stacktrace:");
            //    Console.WriteLine(ex.StackTrace);
            //}
            //Console.ReadLine();

            //string text = "f1,f2,f3";

            //string text = @"*;FieldName2.REGEX_REPLACE(""\b[^()]+\(\(.*)\)$"",""xy\""z"",'x').AS(""COUNTER"");FieldName3;";

            //var outFields = new QueryOutFields(text);
            //foreach(var outField in outFields.Fields)
            //{
            //    Console.WriteLine($"{ outField.Name } => { outField.CommandLine() }");

            //    foreach(var method in outField.ApplyMethods)
            //    {
            //        Console.WriteLine($".{ method.MethodInstance.Name }");
            //        foreach(var parameter in method.Parameters)
            //        {
            //            Console.WriteLine($"    ParameterValue: { parameter }");
            //        }
            //    }
            //}

            Console.WriteLine("Metadata names:");
            foreach(var name in await client.GetCustomMetadataNamesAsync())
            {
                Console.WriteLine(name);
            }

            Console.WriteLine("Metadatas");
            var dict = await client.GetCustomMetadatasAsync();
            foreach (var key in dict.Keys)
            {
                Console.WriteLine($"{ key }: { dict[key] }");
            }

            return 0;
        }

        async static Task CreateIndex()
        {
            #region Create Index

            if (await client.IndexExistsAsync())
            {
                await client.RemoveIndexAsync();
            }
            await client.CreateIndexAsync();

            #endregion

            #region Mapping

            var mapping = new IndexMapping()
            {
                PrimaryFields = new string[] { "title", "content" },
                Fields = new FieldMapping[]
                {
                    new DocumentGuidField(),
                    new IndexField("title"),
                    new IndexField("content"),
                    new IndexField("feed_id", FieldTypes.StringType),
                    new IndexField("publish_date", FieldTypes.DateTimeType),
                    new StoredField("url"),
                    new StoredField("image"),
                }
            };

            await client.MapAsync(mapping);

            #endregion
        }

        async static Task IndexItems()
        {
            var rssFeeds = new Dictionary<string, string>()
            {
                { "derstandard","https://www.derstandard.at/rss" },
                { "diepresse", "https://diepresse.com/rss//home" },
                { "kurier", "https://kurier.at/politik/xml/rssd" },
                { "krone", "http://www.krone.at/nachrichten/rss.html" },
                { "futurezone","https://futurezone.at/xml/rss" }
            };

            foreach (var feed in rssFeeds.Keys)
            {
                var items = await new Rss.FeedParser().Parse(
                    rssFeeds[feed],
                    Rss.FeedType.Unknown);

                var docs = items.Select(item =>
                    {
                        var doc = new Dictionary<string, object>();

                        doc["title"] = item.Title;
                        doc["content"] = item.Content;
                        doc["url"] = item.Link;
                        doc["feed_id"] = feed;
                        if (item.PublishDate.HasValue)
                        {
                            doc["publish_date"] = item.PublishDate.Value;
                        }

                        return doc;
                    });

                await client.IndexDocumentsAsync(docs);
            }
        }

        async static Task Group()
        {
            var result = await client.GroupAsync("feed_id");
        }
    }
}
