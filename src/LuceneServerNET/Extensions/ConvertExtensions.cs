using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LuceneServerNET.Extensions
{
    static public class ConvertExtensions
    {
        static public double ToDouble(this object value)
        {
            if(value is JsonElement)
            {
                return ((JsonElement)value).GetDouble();
            }
            else if(value is IConvertible)
            {
                return Convert.ToDouble(value);
            }

            throw new Exception($"Can't convert { value?.GetType() } to double");
        }

        static public float ToSingle(this object value)
        {
            if (value is JsonElement)
            {
                return ((JsonElement)value).GetSingle();
            }
            else if (value is IConvertible)
            {
                return Convert.ToSingle(value);
            }

            throw new Exception($"Can't convert { value?.GetType() } to double");
        }

        static public int ToInt32(this object value)
        {
            if (value is JsonElement)
            {
                return ((JsonElement)value).GetInt32();
            }
            else if (value is IConvertible)
            {
                return Convert.ToInt32(value);
            }

            throw new Exception($"Can't convert { value?.GetType() } to double");
        }
    }
}
