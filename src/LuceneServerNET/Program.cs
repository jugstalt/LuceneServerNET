using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET
{
    public class Program
    {
        public static void Main(string[] args)
        {
            #region Hash secret => print and exit

            int hashArg = Array.FindIndex(args, a => "--hash-secret".Equals(a, StringComparison.OrdinalIgnoreCase));
            if (hashArg >= 0)
            {
                if (hashArg + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Usage: LuceneServerNET --hash-secret <secret>");
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine(LuceneServerNET.Authentication.SecretHasher.Hash(args[hashArg + 1]));
                return;
            }

            #endregion

            #region First Start => init configuration

            new Setup().TrySetup(args);

            #endregion

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile("_config/luceneserver.json", optional: true);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();

                    #region Expose Ports

                    List<string> urls = new List<string>();
                    for (int i = 0; i < args.Length - 1; i++)
                    {
                        switch (args[i].ToLower())
                        {
                            case "-expose-http":
                                urls.Add("http://localhost:" + int.Parse(args[++i]));
                                break;
                            case "-expose-https":
                                urls.Add("https://localhost:" + int.Parse(args[++i]));
                                break;
                        }
                    }
                    if (urls.Count > 0)
                    {
                        webBuilder = webBuilder.UseUrls(urls.ToArray());
                    }

                    #endregion
                });
    }
}
