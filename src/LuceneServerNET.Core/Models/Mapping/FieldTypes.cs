using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace LuceneServerNET.Core.Models.Mapping
{
    public class FieldTypes
    {
        public const string StringType = "string";
        public const string TextType = "text";
        public const string Int32Type = "int32";
        public const string DoubleType = "double";
        public const string SingleType = "single";
        public const string DateTimeType = "datetime";
        public const string GeoType = "geo";

        public const string GuidType = "guid";

        static public string[] Values()
        {
            var fields = typeof(FieldTypes).GetFields(BindingFlags.Public | BindingFlags.Static);

            return fields
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.GetValue(null)!=null)
                .Select(f => f.GetValue(null).ToString())
                .ToArray();
        }
    }
}
