using System.Collections.Generic;

namespace LuceneServerNET.Core.Models.Result
{
    public class WhoAmIResult : ApiResult
    {
        public string ClientId { get; set; }
        public IEnumerable<string> Roles { get; set; }
    }
}
