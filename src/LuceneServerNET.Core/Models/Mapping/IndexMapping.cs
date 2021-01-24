using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Core.Models.Mapping
{
    public class IndexMapping
    {
        private ICollection<FieldMapping> _fields = null;
        public ICollection<FieldMapping> Fields
        {
            get { return _fields ?? new FieldMapping[0]; }
            set { _fields = value; }
        }

        public string PrimaryField { get; set; }
    }
}
