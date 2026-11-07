using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Core.Models.Result
{
    public class LuceneSearchResult : ApiResult
    {
        public IEnumerable<IDictionary<string, object>> Hits { get; set; }

        /// <summary>
        /// Total number of matching documents (independent of from/size).
        /// </summary>
        public long? TotalHits { get; set; }

        /// <summary>
        /// Offset of the first hit of this page.
        /// </summary>
        public int? From { get; set; }

        /// <summary>
        /// Requested page size.
        /// </summary>
        public int? Size { get; set; }

        /// <summary>
        /// Cursor for the next page (pass it as searchAfter).
        /// Null if there are probably no more hits or if a sortField is used.
        /// </summary>
        public string NextSearchAfter { get; set; }
    }
}
