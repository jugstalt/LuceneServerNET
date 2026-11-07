using LuceneServerNET.Core.Models.Custom;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using LuceneServerNET.Engine.Models.Spatial;
using LuceneServerNET.Engine.Services;
using LuceneServerNET.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LuceneServerNET.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize(Policy = ApiRoles.Read)]
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
    public Task<IApiResult> Search(string id,
                                   string q,
                                   string outFields = null,
                                   int size = 20,
                                   string sortField = null,
                                   bool sortReverse = false,
                                   string filter = "",
                                   string format = "")
    {
        return SecureMethodHandler(id, (id) =>
        {
            var hits = _lucene.Search(id,
                                      term: q,
                                      outFieldNames: outFields ?? String.Empty,
                                      size: size,
                                      sortFieldName: sortField,
                                      sortReverse: sortReverse,
                                      spatialFilter: ParseSpatialFilter(filter));

            return ToSearchResult(hits, format);
        });
    }

    [HttpGet]
    [Route("searchphonetic/{id}")]
    public Task<IApiResult> PhoneticSearch(string id,
                                           string q,
                                           string outFields = null,
                                           int size = 20,
                                           string sortField = null,
                                           bool sortReverse = false,
                                           string filter = "",
                                           string format = "")
    {
        return SecureMethodHandler(id, (id) =>
        {
            var hits = _lucene.SearchPhonetic(id,
                                              term: q,
                                              outFieldNames: outFields ?? String.Empty,
                                              size: size,
                                              sortFieldName: sortField,
                                              sortReverse: sortReverse,
                                              spatialFilter: ParseSpatialFilter(filter));

            return ToSearchResult(hits, format);
        });
    }

    [HttpGet]
    [Route("group/{id}")]
    public Task<IApiResult> Group(string id, string groupField, string q)
    {
        return SecureMethodHandler(id, (id) =>
        {
            var hits = _lucene.GroupBy(id, groupField, q);

            return new LuceneGroupResult()
            {
                Hits = hits
            };
        });
    }

    [HttpGet]
    [Route("createindex/{id}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> CreateIndex(string id)
    {
        return SecureMethodHandler(id, (id) =>
        {
            return new ApiResult(_lucene.CreateIndex(id));
        });
    }

    [HttpGet]
    [Route("removeindex/{id}")]
    [Authorize(Policy = ApiRoles.Admin)]
    public Task<IApiResult> RemoveIndex(string id)
    {
        return SecureMethodHandler(id, (id) =>
        {
            return new ApiResult(_lucene.RemoveIndex(id));
        });
    }

    [HttpGet]
    [Route("indexexists/{id}")]
    public Task<IApiResult> IndexExists(string id)
    {
        return SecureMethodHandler(id, (id) =>
        {
            return new ApiResult(_lucene.IndexExists(id));
        });
    }

    [HttpPost]
    [Route("map/{id}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> Map(string id, [FromBody] IndexMapping mapping)
    {
        return SecureMethodHandler(id, (id) =>
        {
            return new ApiResult(
                _lucene.Map(id, mapping)
            );
        });
    }

    [HttpGet]
    [Route("mapping/{id}")]
    public Task<IApiResult> Mapping(string id)
    {
        return SecureMethodHandler(id, (id) =>
        {
            return new MappingResult()
            {
                Mapping = _lucene.Mapping(id)
            };
        });
    }

    [HttpPost]
    [Route("addmeta/{id}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> AddMeta(string id, string name, [FromBody] CustomMetadata metaData)
    {
        return SecureMethodHandler(id, (id) =>
        {
            return new ApiResult(
                _lucene.AddCustomMetadata(id, name, metaData.Metadata)
            );
        });
    }

    [HttpGet]
    [Route("getmeta/{id}")]
    public Task<IApiResult> GetMeta(string id, string name)
    {
        return SecureMethodHandler(id, async (id) =>
        {
            return new CustomMetadataResult()
            {
                Metadata = await _lucene.GetCustomMetadata(id, name)
            };
        });
    }

    [HttpGet]
    [Route("getmetas/{id}")]
    public Task<IApiResult> GetMetas(string id)
    {
        return SecureMethodHandler(id, async (id) =>
        {
            return new CustomMetadatasResult()
            {
                Metadata = await _lucene.GetCustomMetadatas(id)
            };
        });
    }

    [HttpGet]
    [Route("getmetanames/{id}")]
    public Task<IApiResult> GetMetaNames(string id)
    {
        return SecureMethodHandler(id, async (id) =>
        {
            return new LuceneGenericListResult<string>()
            {
                Result = await _lucene.GetCustomMetadataNames(id)
            };
        });
    }

    [HttpPost]
    [Route("index/{id}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> Index(string id, [FromBody] IEnumerable<IDictionary<string, object>> items)
    {
        return SecureMethodHandler(id, (id) =>
        {
            return new ApiResult(
                _lucene.Index(id, items)
                );
        });
    }

    [HttpGet]
    [Route("remove/{id}")]
    [Authorize(Policy = ApiRoles.Write)]

    public Task<IApiResult> Remove(string id, string term, string termField = "_guid")
    {
        return SecureMethodHandler(id, (id) =>
        {
            return new ApiResult(
                _lucene.RemoveDocuments(id, term, termField)
                );
        });
    }


    [HttpGet]
    [Route("refresh/{id}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> Refresh(string id)
    {
        return SecureMethodHandler(id, (id) =>
        {
            _lucene.RefreshIndex(id);
            return new ApiResult();
        });
    }

    [HttpGet]
    [Route("releaseall")]
    [Authorize(Policy = ApiRoles.Admin)]
    public Task<IApiResult> ReleaseAll()
    {
        return SecureMethodHandler(() =>
        {
            _lucene.ReleaseAll();
            return new ApiResult();
        });
    }

    #region Helper

    private ISpatialFilter ParseSpatialFilter(string filter)
    {
        if (String.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        filter = filter.Trim();

        if (TryGetFilterArguments(filter, "bbox", out var args))
        {
            return BBoxFilter.Parse(args);
        }
        if (TryGetFilterArguments(filter, "dist_km", out args))
        {
            return DistanceKmFilter.Parse(args);
        }
        if (TryGetFilterArguments(filter, "linedist_km", out args))
        {
            return LineStringDistanceKmFilter.Parse(args);
        }

        _logger.LogWarning("Unknown spatial filter '{filter}' is ignored", filter);
        return null;
    }

    private static bool TryGetFilterArguments(string filter, string name, out string arguments)
    {
        arguments = null;
        var prefix = name + "(";

        if (!filter.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!filter.EndsWith(")"))
        {
            throw new ArgumentException($"Invalid filter '{filter}': missing closing parenthesis.");
        }

        arguments = filter.Substring(prefix.Length, filter.Length - prefix.Length - 1);
        return true;
    }

    private static IApiResult ToSearchResult(IEnumerable<IDictionary<string, object>> hits, string format)
    {
        int geoJsonIndex = (format ?? String.Empty).IndexOf(":geojson", StringComparison.OrdinalIgnoreCase);
        if (geoJsonIndex >= 0 && format.EndsWith(":geojson", StringComparison.OrdinalIgnoreCase))
        {
            return new GeoJsonResult(format.Substring(0, geoJsonIndex), hits);
        }

        return new LuceneSearchResult()
        {
            Hits = hits
        };
    }

    private Task<IApiResult> SecureMethodHandler(string id, Func<string, IApiResult> func, Func<Exception, IApiResult> onException = null)
        => SecureMethodHandler(id, (id) => Task.FromResult(func(id)), onException);

    private Task<IApiResult> SecureMethodHandler(Func<IApiResult> func, Func<Exception, IApiResult> onException = null)
        => SecureMethodHandler(() => Task.FromResult(func()), onException);

    /// <summary>
    /// For index specific endpoints: normalizes and validates the index id (whitelist) before calling <paramref name="func"/>.
    /// </summary>
    private Task<IApiResult> SecureMethodHandler(string id, Func<string, Task<IApiResult>> func, Func<Exception, IApiResult> onException = null)
    {
        return SecureMethodHandler(() =>
        {
            id = id?.Trim().ToLower();

            if (String.IsNullOrEmpty(id))
            {
                throw new Exception("Index id not specified");
            }

            if (!LuceneServerNET.Engine.Extensions.PathGuard.IsValidName(id))
            {
                throw new ArgumentException($"Invalid index id '{id}'. Allowed: a-z, 0-9, '_', '-' (max. 64 characters).");
            }

            return func(id);
        }, onException);
    }

    /// <summary>
    /// For endpoints without index id: error handling and timing only.
    /// </summary>
    private async Task<IApiResult> SecureMethodHandler(Func<Task<IApiResult>> func, Func<Exception, IApiResult> onException = null)
    {
        try
        {
            var start = DateTime.Now;

            var apiResult = await func();

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
                if (_env.IsDevelopment())
                {
                    return new ApiErrorResult(ex.Message);
                }
                else
                {
                    Console.WriteLine($"Exception: {DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}");
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.StackTrace);

                    _logger.LogError(ex, $"Exception: {DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}");

                    return new ApiErrorResult($"Error of type {ex.GetType().ToString()}");
                }

            }
        }
    }

    #endregion
}
