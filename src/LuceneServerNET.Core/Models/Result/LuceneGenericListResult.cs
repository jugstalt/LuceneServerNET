using System;
using System.Collections.Generic;
using System.Text;

namespace LuceneServerNET.Core.Models.Result
{
    public class LuceneGenericListResult<T> : ApiResult
    {
        public IEnumerable<T> Result { get; set; }
    }
}
