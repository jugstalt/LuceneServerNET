using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using LuceneServerNET.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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
        [Route("group/{id}")]
        async public Task<IApiResult> Group(string id, string groupField, string q)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                var hits = _lucene.GroupBy(id, groupField, q);

                return Task.FromResult<IApiResult>(new LuceneSearchResult()
                {
                    Hits = hits
                });
            });
        }

        [HttpGet]
        [Route("createindex/{id}")]
        async public Task<IApiResult> CreateIndex(string id)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new ApiResult(_lucene.CreateIndex(id)));
            });
        }

        [HttpGet]
        [Route("removeindex/{id}")]
        async public Task<IApiResult> RemoveIndex(string id)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new ApiResult(_lucene.RemoveIndex(id)));
            });
        }

        [HttpPost]
        [Route("map/{id}")]
        async public Task<IApiResult> Map(string id,[FromBody] IndexMapping mapping)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new ApiResult(
                    _lucene.Map(id, mapping)
                ));
            });
        }

        [HttpGet]
        [Route("mapping/{id}")]
        async public Task<IApiResult> Mapping(string id)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new MappingResult()
                {
                    Mapping = _lucene.Mapping(id)
                });
            });
        }

        [HttpPost]
        [Route("index/{id}")]
        async public Task<IApiResult> Index(string id, [FromBody] IEnumerable<IDictionary<string, object>> items)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new ApiResult(
                    _lucene.Index(id, items)
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

                var start = DateTime.Now;

                var apiResult = await func(id);

                if (apiResult != null)
                {
                    apiResult.MilliSeconds = (DateTime.Now - start).TotalMilliseconds;
                }

                return apiResult;
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
