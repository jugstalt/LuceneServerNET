using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Core.Models.Result
{
    public class LuceneSearchResult : ApiResult
    {
        public IEnumerable<IDictionary<string, object>> Hits { get; set; }
    }
}
