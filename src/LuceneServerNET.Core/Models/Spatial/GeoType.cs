using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Core.Models.Spatial
{
    public abstract class GeoType
    {
        public abstract string Type { get; set; }
        public abstract bool IsValid();
    }
}
