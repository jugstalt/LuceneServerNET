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

    /// <summary>
    /// Converts a document value to UTC. Values with "Z" or an offset are converted to UTC,
    /// values without time zone ("2024-01-31", "2024-01-31T12:00:00") are taken as UTC
    /// (independent of the server time zone). Strings are parsed culture invariant.
    /// </summary>
    static public DateTime ToUtcDateTime(this object value)
    {
        switch (value)
        {
            case DateTime dateTime:
                return dateTime.Kind switch
                {
                    DateTimeKind.Utc => dateTime,
                    DateTimeKind.Local => dateTime.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                };
            case DateTimeOffset dateTimeOffset:
                return dateTimeOffset.UtcDateTime;
            case JsonElement { ValueKind: JsonValueKind.String } e:
                return ParseUtc(e.GetString());
            default:
                return ParseUtc(value?.ToString());
        }
    }

    private static DateTime ParseUtc(string text)
    {
        if (String.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Empty date value");
        }

        return DateTime.Parse(text.Trim(), CultureInfo.InvariantCulture,
                              DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    /// <summary>Reads a stored date value: ISO 8601 (current format) or, for older indices, the server culture format.</summary>
    static public DateTime? StoredStringToUtcDateTime(string stored)
    {
        if (String.IsNullOrWhiteSpace(stored))
        {
            return null;
        }
        if (DateTime.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var invariant))
        {
            return invariant;
        }
        if (DateTime.TryParse(stored, CultureInfo.CurrentCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var legacy))
        {
            return legacy;
        }
        return null;
    }

    static public int ToInt32(this object value)
        => value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } e => int.Parse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture),
            JsonElement e => e.GetInt32(),
            IConvertible => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            _ => throw new Exception($"Can't convert {value?.GetType()} to int32")
        };
}
