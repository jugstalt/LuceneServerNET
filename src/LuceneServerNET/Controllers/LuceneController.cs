using LuceneServerNET.Core.Models.Custom;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using LuceneServerNET.Models.Spatial;
using LuceneServerNET.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
        async public Task<IApiResult> Search(string id,
                                             string q,
                                             string outFields = null,
                                             int size = 20,
                                             string sortField = null,
                                             bool sortReverse = false,
                                             string filter = "",
                                             string format = "")
        {
            return await SecureMethodHandler(id, (id) =>
            {
                ISpatialFilter spatialFilter = null;
                if (!String.IsNullOrEmpty(filter))
                {
                    filter = filter.Trim();
                    if (filter.StartsWith("bbox(", StringComparison.OrdinalIgnoreCase))
                    {
                        spatialFilter = BBoxFilter.Parse(filter.Substring(5, filter.Length - 6));
                    }
                    else if(filter.StartsWith("dist_km(", StringComparison.OrdinalIgnoreCase))
                    {
                        spatialFilter = DistanceKmFilter.Parse(filter.Substring(8, filter.Length - 9));
                    }
                    else if (filter.StartsWith("linedist_km("))
                    {
                        spatialFilter = LineStringDistanceKmFilter.Parse(filter.Substring(12, filter.Length - 13));
                    }
                }

                var hits = _lucene.Search(id,
                                          term: q,
                                          outFieldNames: outFields ?? String.Empty,
                                          size: size,
                                          sortFieldName: sortField,
                                          sortReverse: sortReverse,
                                          spatialFilter: spatialFilter);

                if(format.EndsWith(":geojson", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult<IApiResult>(new GeoJsonResult(
                        format.Substring(0, format.IndexOf(":geojson", StringComparison.OrdinalIgnoreCase)),
                        hits));
                }

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

                return Task.FromResult<IApiResult>(new LuceneGroupResult()
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
        [Route("addmeta/{id}")]
        async public Task<IApiResult> AddMeta(string id, string name, [FromBody] CustomMetadata metaData)
        {
            return await SecureMethodHandler(id, (id) =>
            {
                return Task.FromResult<IApiResult>(new ApiResult(
                    _lucene.AddCustomMetadata(id, name, metaData.Metadata)
                ));
            });
        }

        [HttpGet]
        [Route("getmeta/{id}")]
        async public Task<IApiResult> GetMeta(string id, string name)
        {
            return await SecureMethodHandler(id, async (id) =>
            {
                return new CustomMetadataResult()
                {
                    Metadata = await _lucene.GetCustomMetadata(id, name)
                };
            });
        }

        [HttpGet]
        [Route("getmetas/{id}")]
        async public Task<IApiResult> GetMetas(string id)
        {
            return await SecureMethodHandler(id, async (id) =>
            {
                var dict = await _lucene.GetCustomMetadatas(id);
                return new CustomMetadatasResult()
                {
                    Metadata = await _lucene.GetCustomMetadatas(id)
                };
            });
        }

        [HttpGet]
        [Route("getmetanames/{id}")]
        async public Task<IApiResult> GetMetaNames(string id)
        {
            return await SecureMethodHandler(id, async (id) =>
            {
                return new LuceneGenericListResult<string>()
                {
                    Result = await _lucene.GetCustomMetadataNames(id)
                };
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
