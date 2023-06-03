using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Engine.Extensions
{
    static public class IOExtensions
    {
        static public string CreateDirectoryIfNotExists(this string directoryName)
        {
            try
            {
                var di = new DirectoryInfo(directoryName);

                if (!di.Exists)
                {
                    di.Create();
                }
            }
            catch
            {

            }

            return directoryName;
        }
    }
}
