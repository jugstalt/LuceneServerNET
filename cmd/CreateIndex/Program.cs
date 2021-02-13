using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CreateIndex
{
    class Program
    {
        async static Task<int> Main(string[] args)
        {
            try
            {
                string serverUrl = null, indexName = null;
                IndexMapping indexMapping = new IndexMapping();
                bool removeIndex = false;

                for (int i = 0; i < args.Length; i++)
                {
                    switch(args[i].ToLower())
                    {
                        case "-remove":
                            removeIndex = true;
                            break;
                    }
                    if (i < args.Length - 1)
                    {
                        switch (args[i].ToLower())
                        {
                            case "-server":
                            case "-s":
                                serverUrl = args[++i];
                                break;
                            case "-index":
                            case "-i":
                                indexName = args[++i];
                                break;
                            case "-field":
                            case "-f":
                                indexMapping.AddField(args[++i].ToFieldMapping(stored: true, index: true));
                                break;
                            case "-sfield":
                                indexMapping.AddField(args[++i].ToFieldMapping(stored: true, index: false));
                                break;
                            case "-primary":
                                indexMapping.PrimaryFields =
                                    new List<string>(
                                        args[++i].Split(',').Select(s => s.Trim()));
                                break;
                        }
                    }
                }

                if (String.IsNullOrEmpty(serverUrl) ||
                    String.IsNullOrEmpty(indexName) ||
                    !indexMapping.IsValid())
                {
                    Console.WriteLine("Usage:");
                    Console.WriteLine("CreateIndex.exe -server[-s] server");
                    Console.WriteLine("                -index[-i] indexname");
                    Console.WriteLine("                -field[-f] fieldname[.fieldtype][.stored|not_stored] // add indexed field - defaults .TextType.stored");
                    Console.WriteLine("                -field ...");
                    Console.WriteLine("                -storedfield[-sfield] fieldname[.fieldtype]  // add stored field - defaults .TextType");
                    Console.WriteLine("                -storedfield ...");
                    Console.WriteLine("                -primary primary-search-fieldname  // default: first field");
                    Console.WriteLine("                -remove  // remove existing index first");
                    Console.WriteLine();
                    Console.WriteLine($"FieldTypes: { String.Join(", ", FieldTypes.Values()) }");

                    return 1;
                }

                var client = new LuceneServerClient(serverUrl, indexName);

                #region Create Index

                if (removeIndex)
                {
                    Console.WriteLine($"Delete index { indexName }...");
                    if (!await client.RemoveIndexAsync())
                    {
                        throw new Exception("Can't deleting index");
                    }
                }

                Console.WriteLine($"Create index { indexName }...");
                if(!await client.CreateIndexAsync())
                {
                    throw new Exception("Can't creating index");
                }

                #endregion

                #region Mapping

                Console.WriteLine($"Map index { indexName }...");
                if(!await client.MapAsync(indexMapping))
                {
                    throw new Exception("Can't map index");
                }

                #endregion

                Console.WriteLine("finished successfully");

                return 0;
            }
            catch(Exception ex)
            {
                Console.WriteLine("Exception:");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);

                return 1;
            }
        }
    }
}
