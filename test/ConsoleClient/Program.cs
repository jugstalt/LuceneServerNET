using LuceneServerNET.Client;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace ConsoleClient
{
    class Program
    {
        async static Task<int> Main(string[] args)
        {
            string serverUrl = "https://localhost:44393";
            string indexName = "TestIndex";
            var client = new LuceneServerClient(serverUrl);

            await client.CreateIndex(indexName);

            return 0;
        }
    }
}
