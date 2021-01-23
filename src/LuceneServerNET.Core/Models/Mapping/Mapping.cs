using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Core.Models.Mapping
{
    public class Mapping
    {
        public ICollection<FieldMapping> Fields { get; set; }

        public string DefaultField { get; set; }
    }
}
