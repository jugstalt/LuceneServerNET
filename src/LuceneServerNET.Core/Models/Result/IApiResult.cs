using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Core.Models.Result
{
    public interface IApiResult
    {
        bool Success { get; set; }
    }
}
