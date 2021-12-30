using Microsoft.Extensions.Configuration;
using System;

namespace LuceneServerNET.Extensions
{
    static public class ConfigurationExtensions
    {
        static public string GetStringValue(this IConfiguration configuration, string key, string defaultValue = "")
        {
            return configuration[key]
                .OrTake(GetEnvironmentVariable(key.ToUpper().Replace(":", "_")))
                .OrTake(defaultValue);
        }

        static public bool GetBoolValue(this IConfiguration configuration, string key, bool defaultValue = false)
        {
            var value = configuration.GetStringValue(key, defaultValue.ToString());

            return bool.Parse(value);
        }

        static public int GetIntValue(this IConfiguration configuration, string key, int defaultValue = 0)
        {
            var value = configuration.GetStringValue(key, defaultValue.ToString());

            return int.Parse(value);
        }

        static private string GetEnvironmentVariable(string name)
        {
            var environmentVariables = Environment.GetEnvironmentVariables();

            if (environmentVariables.Contains(name) && !String.IsNullOrWhiteSpace(environmentVariables[name]?.ToString()))
            {
                return environmentVariables[name]?.ToString();
            }

            return null;
        }
    }
}
