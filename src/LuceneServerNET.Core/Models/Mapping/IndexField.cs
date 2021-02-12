using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Core.Models.Mapping
{
    public class IndexField : FieldMapping
    {
        public IndexField(string name, string fieldType = FieldTypes.TextType)
            : base(name, fieldType)
        {

        }
    }
}
