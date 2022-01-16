using System;
using System.Text.Json;

namespace LuceneServerNET.Extensions
{
    static public class ParametersExtension
    {
        static public string GetArgumentValue(this string[] args, string argument, StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
        {
            if (args == null)
            {
                return null;
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != null && args[i].Equals(argument, stringComparison))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        static public string ToJsonString(this string str)
        {
            str = JsonSerializer.Serialize(str);
            if (str.StartsWith("\"") && str.EndsWith("\""))
            {
                str = str.Substring(1, str.Length - 2);
            }

            return str;
        }
    }
}
