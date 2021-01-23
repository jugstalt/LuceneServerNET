using Lucene.Net.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Core.Models.Mapping
{
    public class FieldMapping
    {
        public string FieldType { get; set; }
        public string Name { get; set; }
        public Field.Store Store { get; set; }
    }
}
