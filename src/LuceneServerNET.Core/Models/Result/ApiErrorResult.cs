using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Core.Models.Result
{
    public class ApiErrorResult : ApiResult
    {
        public ApiErrorResult(string message)
            : base(false)
        {
            this.Message = message;
        }

        public string Message { get; set; }
    }
}
