using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Core.Models.Result
{
    public class LuceneGroupResult : ApiResult
    {
        public IEnumerable<IDictionary<string, object>> Hits { get; set; }
    }
}
