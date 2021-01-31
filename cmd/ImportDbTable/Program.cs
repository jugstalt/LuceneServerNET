using Dapper;
using LuceneServerNET.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ImportDbTable
{
    class Program
    {
        async static Task<int> Main(string[] args)
        {
            try
            {
                string serverUrl = null, indexName = null;
                DbTypes dbType = DbTypes.Unknown;
                string connectionString = null, sqlStatement = null;
                Dictionary<string, string> fields = new Dictionary<string, string>();

                #region Parse Arguments

                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLower())
                    {
                        //case "-remove":
                        //    removeIndex = true;
                        //    break;
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
                            case "-db-type":
                                if (!Enum.TryParse<DbTypes>(args[++i], true, out dbType))
                                {
                                    throw new Exception($"Unknown Db-Type { args[i] }");
                                }

                                break;
                            case "-db":
                            case "-db-connectionstring":
                                connectionString = args[++i];
                                break;
                            case "-sql":
                            case "-sql-statement":
                                sqlStatement = args[++i];
                                break;
                        }
                    }
                    if (i < args.Length - 2)
                    {
                        switch (args[i].ToLower())
                        {
                            case "-f":
                                fields[args[++i]] = args[++i];
                                break;
                        }
                    }
                }

                if (String.IsNullOrEmpty(serverUrl) ||
                    String.IsNullOrEmpty(indexName) ||
                    dbType == DbTypes.Unknown ||
                    String.IsNullOrEmpty(connectionString) ||
                    String.IsNullOrEmpty(sqlStatement) ||
                    fields.Count == 0)
                {
                    Console.WriteLine("Usage:");
                    Console.WriteLine("ImportDbTable.exe -server[-s] {server}");
                    Console.WriteLine("                  -index[-i] {indexname}");
                    Console.WriteLine("                  -db-type {dbType}");
                    Console.WriteLine("                  -db-connectionstring[-db] {connectionString}");
                    Console.WriteLine("                  -sql-statement[-sql] {sqlStatement}");
                    Console.WriteLine("                  -f {indexfield} {expression}   // {expression}: \"lorem {{DB_FIELD1}} ipsum {{DB_FIELD2}}\"");
                    Console.WriteLine();
                    //Console.WriteLine($"FieldTypes: { String.Join(", ", FieldTypes.Values()) }");

                    return 1;
                }

                #endregion

                var startTime = DateTime.Now;
                int counter = 0;

                using (var client = new LuceneServerClient(serverUrl))
                {
                    var items = new List<IDictionary<string, object>>();

                    string regexDbFieldsPattern = @"{{(.*?)}}";
                    using (var connection = DbConnectionFactory.CreateInstance(dbType, connectionString))
                    {
                        foreach (IDictionary<string, object> row in connection.Query(sqlStatement, buffered: false))
                        {
                            if (row != null)
                            {
                                var item = new Dictionary<string, object>();

                                foreach (var indexField in fields.Keys)
                                {
                                    string expression = fields[indexField];
                                    var matches = Regex.Matches(expression, regexDbFieldsPattern).Select(m => m.ToString().Substring(2, m.ToString().Length - 4)).ToArray();

                                    foreach (var match in matches)
                                    {
                                        expression = expression.Replace($"{{{{{ match }}}}}", row[match]?.ToString() ?? String.Empty);
                                    }

                                    item[indexField] = expression;
                                }

                                items.Add(item);
                                counter++;
                            }

                            if (counter % 1000 == 0)
                            {
                                await IndexItems(client, indexName, items, counter);
                            }
                        }
                    }

                    await client.IndexItems(indexName, items);
                }

                Console.WriteLine($"{ counter } records ... { Math.Round((DateTime.Now-startTime).TotalMinutes, 2) } minutes");

                Console.WriteLine("finished successfully");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Exception:");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);

                return 1;
            }
        }

        async static Task IndexItems(LuceneServerClient client, string indexName, List<IDictionary<string,object>> items, int counter)
        {
            Console.Write($"Index { items.Count() } items...");
            if(!await client.IndexItems(indexName, items))
            {
                throw new Exception("Can't index items");
            }
            Console.WriteLine($"succeeded ... { counter }");

            items.Clear();
        }
    }
}
