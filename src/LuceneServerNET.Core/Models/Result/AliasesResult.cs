using System.Collections.Generic;

namespace LuceneServerNET.Core.Models.Result
{
    /// <summary>Index aliases: alias => index.</summary>
    public class AliasesResult : ApiResult
    {
        public IDictionary<string, string> Aliases { get; set; }
    }
}
