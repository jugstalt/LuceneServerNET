using Lucene.Net.Documents;
using Lucene.Net.Search;
using LuceneServerNET.Core.Models.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
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
                            val = DateTools.StringToDate(val?.ToString());
                        }
                        else
                        {
                            val = Convert.ToDateTime(val.ToString());
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

        static public object ParseExpression(this string expression, object val)
        {
            try
            {
                if (!String.IsNullOrEmpty(expression))
                {
                    var type = expression.Last();
                    var quantity = int.Parse(expression.Substring(0, expression.Length - 1));

                    var stringVal = val?.ToString() ?? String.Empty;
                    switch (type)
                    {
                        case 'c':
                            if (stringVal.Length >= quantity)
                            {
                                stringVal = $"{ stringVal.Substring(0, quantity) }...";
                            }
                            break;
                    }

                    val = stringVal;
                }
            }
            catch { }

            return val;
        }
    }
}
