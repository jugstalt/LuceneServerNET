using System;
using System.Collections.Generic;
using System.Text;
using LuceneServerNET.Core.Models.Mapping;

namespace LuceneServerNET.Core.Models.Result
{
    public class MappingResult : ApiResult
    {
        public LuceneServerNET.Core.Models.Mapping.IndexMapping Mapping { get; set; }
    }
}
