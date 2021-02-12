using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using LuceneServerNET.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LuceneController : ControllerBase
    {
        private readonly LuceneService _lucene;
        private readonly ILogger<LuceneController> _logger;
        private readonly IWebHostEnvironment _env;
        

        public LuceneController(LuceneService lucene,
                                IWebHostEnvironment env,
                                ILogger<LuceneController> logger)
        {
            _lucene = lucene;
            _env = env;
            _logger = logger;
        }

        [HttpGet]
        [Route("search/{id}")]
        async public Task<IApiResult> Search(string id, string q, string outFields)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                var hits = _lucene.Search(id, q, String.IsNullOrEmpty(outFields) ? null : outFields.Split(',').Select(s=>s.Trim()));

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

        [HttpGet]
        [Route("indexexists/{id}")]
        async public Task<IApiResult> IndexExists(string id)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new ApiResult(_lucene.IndexExists(id)));
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

        [HttpGet]
        [Route("remove/{id}")]

        async public Task<IApiResult> Remove(string id, string term, string termField = "_guid")
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new ApiResult(
                    _lucene.RemoveDocuments(id, term, termField)
                    ));
            });
        }


        [HttpGet]
        [Route("refresh/{id}")]
        async public Task<IApiResult> Refresh(string id)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                _lucene.RefreshIndex(id);
                return Task.FromResult<IApiResult>(new ApiResult());
            });
        }

        [HttpGet]
        [Route("releaseall")]
        async public Task<IApiResult> ReleaseAll()
        {
            return await SecureMethodHandler("*", (id) =>
            {
                _lucene.ReleaseAll();
                return Task.FromResult<IApiResult>(new ApiResult());
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
                    if(_env.IsDevelopment())
                    {
                        return new ApiErrorResult(ex.Message);
                    } 
                    else
                    {
                        Console.WriteLine($"Exception: { DateTime.Now.ToShortDateString() } { DateTime.Now.ToLongTimeString() }");
                        Console.WriteLine(ex.Message);
                        Console.WriteLine(ex.StackTrace);

                        _logger.LogError(ex, $"Exception: { DateTime.Now.ToShortDateString() } { DateTime.Now.ToLongTimeString() }");

                        return new ApiErrorResult($"Error of type { ex.GetType().ToString() }");
                    }
                   
                }
            }
        }

        #endregion
    }
}
