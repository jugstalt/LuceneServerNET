using Lucene.Net.Documents;
using Lucene.Net.Search;
using LuceneServerNET.Core.Models.Mapping;
using System;
using System.Globalization;

namespace LuceneServerNET.Engine.Extensions;

static public class MappingFieldExtensions
{
    static public object GetValue(this FieldMapping fieldMapping, Document doc)
    {
        object val = doc.Get(fieldMapping.Name);

        switch (fieldMapping.FieldType)
        {
            case FieldTypes.Int32Type:
            case FieldTypes.DoubleType:
            case FieldTypes.SingleType:
                // Document.Get() formats numbers with the current culture ("47,0707" on a german server)
                // => format the stored numeric value culture invariant (stays a string for compatibility)
                var field = doc.GetField(fieldMapping.Name);
                var invariant = field?.NumericType switch
                {
                    NumericFieldType.INT32 => field.GetInt32Value()?.ToString(CultureInfo.InvariantCulture),
                    NumericFieldType.INT64 => field.GetInt64Value()?.ToString(CultureInfo.InvariantCulture),
                    NumericFieldType.SINGLE => field.GetSingleValue()?.ToString(CultureInfo.InvariantCulture),
                    NumericFieldType.DOUBLE => field.GetDoubleValue()?.ToString(CultureInfo.InvariantCulture),
                    _ => null
                };
                if (invariant != null)
                {
                    val = invariant;
                }
                break;
            case FieldTypes.DateTimeType:
                try
                {
                    // indexed: DateTools string (UTC); stored only: ISO 8601 (older indices: server culture format)
                    // => always returned as UTC ("...Z")
                    if (fieldMapping.Index == true)
                    {
                        val = val != null ? DateTime.SpecifyKind(DateTools.StringToDate(val.ToString()), DateTimeKind.Utc) : null;
                    }
                    else
                    {
                        val = val != null ? ConvertExtensions.StoredStringToUtcDateTime(val.ToString()) ?? val : null;
                    }
                }
                catch { }
                break;
        }

        return val;
    }

    static public SortFieldType GetSortFieldType(this FieldMapping fieldMapping)
    {
        switch (fieldMapping?.FieldType)
        {
            case FieldTypes.DoubleType:
                return SortFieldType.DOUBLE;
            case FieldTypes.Int32Type:
                return SortFieldType.INT32;
            case FieldTypes.SingleType:
                return SortFieldType.SINGLE;
            default:
                return SortFieldType.STRING;
        }
    }

    //static public object ParseExpression(this string expression, object val)
    //{
    //    try
    //    {
    //        if (!String.IsNullOrEmpty(expression))
    //        {
    //            string newVal = Regex.Replace(val.ToString() ?? String.Empty,
    //                //@"^((?:\S+\s+){0}\S+).*", 
    //                expression,
    //                "${1}",
    //                RegexOptions.Multiline);

    //            if (newVal.Length < val.ToString().Length)
    //                newVal = $"{ newVal }...";

    //            val = newVal;
    //        }
    //    }
    //    catch { }

    //    return val;
    //}
}
