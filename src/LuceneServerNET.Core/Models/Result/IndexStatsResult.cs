using LuceneServerNET.Core.Models.Admin;

namespace LuceneServerNET.Core.Models.Result
{
    public class IndexStatsResult : ApiResult
    {
        public IndexStats Stats { get; set; }
    }
}
