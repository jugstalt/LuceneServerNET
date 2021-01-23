using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using LuceneServerNET.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace LuceneServerNET.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LuceneController : ControllerBase
    {
        private readonly LuceneService _lucene;
        private readonly ILogger<LuceneController> _logger;

        public LuceneController(LuceneService lucene,
                                ILogger<LuceneController> logger)
        {
            _lucene = lucene;
            _logger = logger;
        }

        [HttpGet]
        [Route("search/{id}")]
        async public Task<IApiResult> Search(string id, string q)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                var hits = _lucene.Search(id, q);

                return Task.FromResult<IApiResult>(new LuceneSearchResult()
                {
                    Hits = hits
                });  
            });
            
        }

        [HttpGet]
        [Route("create/{id}")]
        async public Task<IApiResult> Create(string id)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new ApiResult(_lucene.CreateIndex(id)));
            });
        }

        [HttpPost]
        [Route("map/{id}")]
        async public Task<IApiResult> Map(string id, Mapping mapping)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new ApiResult(
                    
                    ));
            });
        }

        [HttpPost]
        [Route("index/{id}")]
        async public Task<IApiResult> Index(string id, string title, string content)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new ApiResult(
                    _lucene.Index(id, title, content)
                    ));
            });
        }

        #region Helper

        private async Task<IApiResult> SecureMethodHandler(string id, Func<string, Task<IApiResult>> func, Func<Exception, IApiResult> onException = null)
        {
            try
            {
                id = id?.Trim().ToLower();

                if (String.IsNullOrEmpty(id))
                {
                    throw new Exception("Index id not specified");
                }

                return await func(id);
            }
            catch (Exception ex)
            {
                if (onException != null)
                {
                    return onException(ex);
                }
                else
                {
                    return new ApiErrorResult(ex.Message);
                }
            }
        }

        #endregion
    }
}
