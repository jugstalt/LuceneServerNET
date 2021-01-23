using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Core.Models.Mapping
{
    public class Mapping
    {
        ICollection<FieldMapping> Fields { get; set; }
    }
}
