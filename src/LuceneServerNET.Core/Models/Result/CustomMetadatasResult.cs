using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Core.Models.Result
{
    public class CustomMetadatasResult : ApiResult
    {
        public IDictionary<string, string> Metadata { get; set; }
    }
}
