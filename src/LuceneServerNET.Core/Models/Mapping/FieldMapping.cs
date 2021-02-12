using System;
using System.Linq;

namespace LuceneServerNET.Core.Models.Mapping
{
    public class FieldMapping
    {
        public FieldMapping()
        {
            this.Store = this.Index = true;
        }

        public FieldMapping(string name, string fieldType = FieldTypes.TextType)
            : this(name, fieldType, false)
        {
            
        }

        internal FieldMapping(string name, string fieldType, bool allowInternalNames)
            : this()
        {
            name = name?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("field mapping name is empty");
            }

            if (!FieldTypes.Values().Contains(fieldType))
            {
                throw new ArgumentException($"Unknown field type: { fieldType }");
            }

            if (allowInternalNames==false)
            {
                if (name.StartsWith("_"))
                {
                    throw new ArgumentException($"field name '{ name }' is not allowed");
                }
            }

            this.Name = name;
            this.FieldType = fieldType;
        }

        public string FieldType { get; set; }
        public string Name { get; set; }
        public bool Store { get; set; }
        public bool Index { get; set; }
    }
}
