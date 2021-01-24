using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Core.Models.Result
{
    public class ApiResult : IApiResult
    {
        public ApiResult()
        {
            this.Success = true;
        }

        public ApiResult(bool success)
        {
            this.Success = success;
        }

        public bool Success { get; set; }

        public double MilliSeconds { get; set; }
    }
}
