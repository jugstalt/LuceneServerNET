using LuceneServerNET.Core.Models.Mapping;
using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Core.Models.Result
{
    public class LuceneMappingResult : ApiResult
    {
        public IndexMapping Mapping { get; set; } 
    }
}
