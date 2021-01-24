using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuceneServerNET.Core.Extensions
{
    static public class JsonExtensions
    {
        static public T DeserializeJson<T>(this string json)
        {
            var options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new JsonStringEnumConverter());

            return JsonSerializer.Deserialize<T>(json, options);
        }
    }
}
