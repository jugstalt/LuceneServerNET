using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ConsoleClient
{
    class Program
    {
        static string serverUrl = "https://localhost:44393";
        static string indexName = "TestIndex";
        static LuceneServerClient client = new LuceneServerClient(serverUrl);

        async static Task<int> Main(string[] args)
        {
            try
            {
                await CreateIndex();
                await IndexItems();

                Console.WriteLine("finished");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: { ex.Message }");
            }
            Console.ReadLine();

            return 0;
        }

        async static Task CreateIndex()
        {
            #region Create Index

            await client.RemoveIndex(indexName);
            await client.CreateIndex(indexName);

            #endregion

            #region Mapping

            var mapping = new IndexMapping()
            {
                PrimaryField = "title",
                Fields = new FieldMapping[]
                {
                    new FieldMapping()
                    {
                         FieldType = FieldTypes.TextType,
                         Name = "title"
                    },
                    new FieldMapping()
                    {
                         FieldType = FieldTypes.TextType,
                         Name = "content", Store = Store.YES
                    },
                    new FieldMapping()
                    {
                         FieldType = FieldTypes.StringType,
                         Name = "feed"
                    },
                    new FieldMapping()
                    {
                         FieldType = FieldTypes.StringType,
                         Name = "url"
                    },
                    new FieldMapping()
                    {
                         FieldType = FieldTypes.StringType,
                         Name = "image"
                    }
                }
            };

            await client.Map(indexName, mapping);

            #endregion
        }

        async static Task IndexItems()
        {
            var rssFeeds = new Dictionary<string, string>()
            {
                { "DerStandard","https://www.derstandard.at/rss" },
                { "DiePresse", "https://diepresse.com/rss//home" },
                { "Kurier", "https://kurier.at/politik/xml/rssd" },
                { "Krone", "http://www.krone.at/nachrichten/rss.html" },
                { "Futurezone","https://futurezone.at/xml/rss" }
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
                        doc["feed"] = feed;

                        return doc;
                    });

                await client.IndexItems(indexName, docs);
            }
        }
    }
}
