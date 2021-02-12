using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Core.Models.Mapping
{
    public class DocumentGuidField : FieldMapping
    {
        public DocumentGuidField()
            : base("_guid", FieldTypes.GuidType, true)
        {
        }
    }
}
