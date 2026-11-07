using LuceneServerNET.Core.Models.Custom;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using LuceneServerNET.Engine.Models.Spatial;
using LuceneServerNET.Engine.Services;
using LuceneServerNET.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LuceneServerNET.Controllers;

[ApiController]
[Route("[controller]")]
[ApiExplorerSettings(GroupName = "v1")]
[Authorize(Policy = ApiRoles.Read)]
public class LuceneController : LuceneControllerBase
{
    public LuceneController(LuceneService lucene,
                            IWebHostEnvironment env,
                            IConfiguration config,
                            ILogger<LuceneController> logger)
        : base(lucene, env, config, logger)
    {
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
                                   string format = "",
                                   int from = 0,
                                   string searchAfter = null)
    {
        return SecureMethodHandler(id, (id) =>
        {
            var page = _lucene.SearchPage(id,
                                          term: q,
                                          outFieldNames: outFields ?? String.Empty,
                                          size: size,
                                          from: from,
                                          searchAfter: searchAfter,
                                          sortFieldName: sortField,
                                          sortReverse: sortReverse,
                                          spatialFilter: ParseSpatialFilter(filter));

            return ToSearchResult(page, from, size, format);
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
                                           string format = "",
                                           int from = 0,
                                           string searchAfter = null)
    {
        return SecureMethodHandler(id, (id) =>
        {
            var page = _lucene.SearchPhoneticPage(id,
                                                  term: q,
                                                  outFieldNames: outFields ?? String.Empty,
                                                  size: size,
                                                  from: from,
                                                  searchAfter: searchAfter,
                                                  sortFieldName: sortField,
                                                  sortReverse: sortReverse,
                                                  spatialFilter: ParseSpatialFilter(filter));

            return ToSearchResult(page, from, size, format);
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

    [HttpPost]
    [Route("createindex/{id}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> CreateIndex(string id)
    {
        return SecureMethodHandler(id, (id) =>
        {
            return new ApiResult(_lucene.CreateIndex(id));
        });
    }

    [Obsolete("Use POST createindex/{id}")]
    [HttpGet]
    [Route("createindex/{id}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> CreateIndexLegacyGet(string id)
        => LegacyGet("POST", () => CreateIndex(id));

    [HttpDelete]
    [Route("removeindex/{id}")]
    [Authorize(Policy = ApiRoles.Admin)]
    public Task<IApiResult> RemoveIndex(string id)
    {
        return SecureMethodHandler(id, (id) =>
        {
            return new ApiResult(_lucene.RemoveIndex(id));
        });
    }

    [Obsolete("Use DELETE removeindex/{id}")]
    [HttpGet]
    [Route("removeindex/{id}")]
    [Authorize(Policy = ApiRoles.Admin)]
    public Task<IApiResult> RemoveIndexLegacyGet(string id)
        => LegacyGet("DELETE", () => RemoveIndex(id));

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

    [HttpDelete]
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

    [Obsolete("Use DELETE remove/{id}")]
    [HttpGet]
    [Route("remove/{id}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> RemoveLegacyGet(string id, string term, string termField = "_guid")
        => LegacyGet("DELETE", () => Remove(id, term, termField));

    [Obsolete("Use POST refresh/{id}")]
    [HttpGet]
    [Route("refresh/{id}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> RefreshLegacyGet(string id)
        => LegacyGet("POST", () => Refresh(id));

    [Obsolete("Use POST releaseall")]
    [HttpGet]
    [Route("releaseall")]
    [Authorize(Policy = ApiRoles.Admin)]
    public Task<IApiResult> ReleaseAllLegacyGet()
        => LegacyGet("POST", () => ReleaseAll());

    [HttpPost]
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

    [HttpPost]
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
}
