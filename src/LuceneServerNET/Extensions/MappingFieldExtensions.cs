using Lucene.Net.Documents;
using Lucene.Net.Search;
using LuceneServerNET.Core.Models.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LuceneServerNET.Extensions
{
    static public class MappingFieldExtensions
    {
        static public object GetValue(this FieldMapping fieldMapping, Document doc)
        {
            object val = doc.Get(fieldMapping.Name);

            switch (fieldMapping.FieldType)
            {
                case FieldTypes.DateTimeType:
                    try
                    {
                        if (fieldMapping.Index == true)
                        {
                            val = val != null ? DateTools.StringToDate(val.ToString()) : null;
                        }
                        else
                        {
                            val = val != null ? Convert.ToDateTime(val.ToString()) : null;
                        }
                    }
                    catch { }
                    break;
            }

            return val;
        }

        static public SortFieldType GetSortFieldType(this FieldMapping fieldMapping)
        {
            switch(fieldMapping?.FieldType)
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
}
