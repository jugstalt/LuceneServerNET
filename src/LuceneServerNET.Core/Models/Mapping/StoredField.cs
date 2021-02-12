using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Core.Models.Mapping
{
    public class StoredField : FieldMapping
    {
        public StoredField(string name, string fieldType = FieldTypes.StringType)
            : base(name, fieldType)
        {
            this.Index = false;
        }
    }
}
