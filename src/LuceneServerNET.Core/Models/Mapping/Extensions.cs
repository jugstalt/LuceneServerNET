using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace LuceneServerNET.Core.Models.Mapping
{
    public static class Extensions
    {
        static public bool TryParseFieldType(this string text, out string fieldType)
        {
            foreach(var field in typeof(FieldTypes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.IsLiteral && !field.IsInitOnly && 
                    field.GetValue(null).ToString().Equals(text, StringComparison.InvariantCultureIgnoreCase))
                {
                    fieldType = field.GetValue(null).ToString();
                    return true;
                }
            }

            fieldType = null;
            return false;
        }
    }
}
