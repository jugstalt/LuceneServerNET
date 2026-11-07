using System;
using System.Globalization;
using System.Text.Json;

namespace LuceneServerNET.Engine.Extensions;

/// <summary>
/// Converts document values (JSON elements, numbers or strings) to numbers.
/// Strings are always parsed culture invariant ("47.07"), independent of the server culture.
/// </summary>
static public class ConvertExtensions
{
    static public double ToDouble(this object value)
        => value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } e => double.Parse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            JsonElement e => e.GetDouble(),
            IConvertible => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            _ => throw new Exception($"Can't convert {value?.GetType()} to double")
        };

    static public float ToSingle(this object value)
        => value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } e => float.Parse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            JsonElement e => e.GetSingle(),
            IConvertible => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            _ => throw new Exception($"Can't convert {value?.GetType()} to single")
        };

    static public int ToInt32(this object value)
        => value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } e => int.Parse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture),
            JsonElement e => e.GetInt32(),
            IConvertible => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            _ => throw new Exception($"Can't convert {value?.GetType()} to int32")
        };
}
