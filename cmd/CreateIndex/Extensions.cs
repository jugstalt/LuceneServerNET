using LuceneServerNET.Core.Models.Mapping;
using System;

namespace CreateIndex
{
    public static class Extensions
    {
        public static FieldMapping ToFieldMapping(this string field, bool stored = true, bool index = true)
        {
            var parts = field.Split('.');

            string fieldName = parts[0].ToLower(), 
                   fieldType = FieldTypes.TextType;

            if (parts.Length > 1)
            {
                if (!parts[1].TryParseFieldType(out fieldType))
                {
                    throw new Exception($"Unknown fieldtype { parts[1] }. Use: { String.Join(", ", FieldTypes.Values()) }");
                }
            }

            if (parts.Length > 2)
            {
                switch(parts[2].ToLower())
                {
                    case "stored":
                        stored = true;
                        break;
                    case "not_stored":
                        stored = false;
                        break;
                    default:
                        throw new Exception($"Unknown literal { parts[2] } in { field }. User stored or not_stored");
                }
            }

            return new FieldMapping(fieldName, fieldType)
            {
                Store = stored,
                Index = index
            };
        }
    }
}
