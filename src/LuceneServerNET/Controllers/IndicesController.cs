using LuceneServerNET.Authentication;
using LuceneServerNET.Core.Models.Custom;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using LuceneServerNET.Engine.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LuceneServerNET.Controllers;

/// <summary>
/// Resource oriented API v2: /api/v2/indices/{id}/...
/// Runs in parallel to the v1 API (/lucene/...) and uses the same engine.
/// </summary>
[ApiController]
[Route("api/v2/indices")]
[ApiExplorerSettings(GroupName = "v2")]
[Authorize(Policy = ApiRoles.Read)]
public class IndicesController : LuceneControllerBase
{
    public IndicesController(LuceneService lucene,
                             IWebHostEnvironment env,
                             IConfiguration config,
                             ILogger<IndicesController> logger)
        : base(lucene, env, config, logger)
    {
    }

    /// <summary>v2 returns errors with HTTP status codes (400/404/409/500).</summary>
    protected override bool UseHttpStatusCodesForErrors => true;

    #region Index

    /// <summary>Returns the names of all indices.</summary>
    [HttpGet]
    public Task<IApiResult> ListIndices()
        => SecureMethodHandler(() => new LuceneGenericListResult<string>()
        {
            Result = _lucene.IndexNames()
        });

    /// <summary>Returns statistics of the index (document count, size on disk, last modification).</summary>
    [HttpGet("{id}/stats")]
    public Task<IApiResult> Stats(string id)
        => SecureMethodHandler(id, (id) => new IndexStatsResult() { Stats = _lucene.IndexStats(id) });

    /// <summary>Creates an index.</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> CreateIndex(string id)
        => SecureMethodHandler(id, (id) => new ApiResult(_lucene.CreateIndex(id)), resolveAlias: false);

    /// <summary>Removes an index.</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = ApiRoles.Admin)]
    public Task<IApiResult> RemoveIndex(string id)
        => SecureMethodHandler(id, (id) => new ApiResult(_lucene.RemoveIndex(id)), resolveAlias: false);

    /// <summary>Returns success=true if the index exists (HTTP 404 otherwise). Also available as HEAD.</summary>
    [HttpGet("{id}")]
    [HttpHead("{id}")]
    public Task<IApiResult> IndexExists(string id)
        => SecureMethodHandler(id, (id) =>
        {
            var exists = _lucene.IndexExists(id);
            if (!exists)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
            }
            return new ApiResult(exists);
        });

    /// <summary>Refreshes the index searcher (makes recently indexed documents visible).</summary>
    [HttpPost("{id}/refresh")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> Refresh(string id)
        => SecureMethodHandler(id, (id) =>
        {
            _lucene.RefreshIndex(id);
            return new ApiResult();
        });

    /// <summary>Releases all open index resources.</summary>
    [HttpPost("_release")]
    [Authorize(Policy = ApiRoles.Admin)]
    public Task<IApiResult> ReleaseAll()
        => SecureMethodHandler(() =>
        {
            _lucene.ReleaseAll();
            return new ApiResult();
        });

    #endregion

    #region Mapping

    [HttpGet("{id}/mapping")]
    public Task<IApiResult> GetMapping(string id)
        => SecureMethodHandler(id, (id) => new MappingResult() { Mapping = _lucene.Mapping(id) });

    [HttpPut("{id}/mapping")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> PutMapping(string id, [FromBody] IndexMapping mapping)
        => SecureMethodHandler(id, (id) => new ApiResult(_lucene.Map(id, mapping)));

    #endregion

    #region Metadata

    /// <summary>Returns all custom metadata of the index (name => value).</summary>
    [HttpGet("{id}/meta")]
    public Task<IApiResult> GetMetas(string id)
        => SecureMethodHandler(id, async (id) => new CustomMetadatasResult()
        {
            Metadata = await _lucene.GetCustomMetadatas(id)
        });

    /// <summary>Returns the names of all custom metadata entries.</summary>
    [HttpGet("{id}/meta/_names")]
    public Task<IApiResult> GetMetaNames(string id)
        => SecureMethodHandler(id, async (id) => new LuceneGenericListResult<string>()
        {
            Result = await _lucene.GetCustomMetadataNames(id)
        });

    [HttpGet("{id}/meta/{name}")]
    public Task<IApiResult> GetMeta(string id, string name)
        => SecureMethodHandler(id, async (id) => new CustomMetadataResult()
        {
            Metadata = await _lucene.GetCustomMetadata(id, name)
        });

    [HttpPut("{id}/meta/{name}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> PutMeta(string id, string name, [FromBody] CustomMetadata metaData)
        => SecureMethodHandler(id, (id) => new ApiResult(_lucene.AddCustomMetadata(id, name, metaData.Metadata)));

    #endregion

    #region Documents

    /// <summary>
    /// Exports all documents (stored fields, without _guid) as NDJSON (one JSON object per line).
    /// Fields that are not stored can not be exported. Header X-Total-Count: number of documents.
    /// </summary>
    [HttpGet("{id}/documents/_export")]
    [Produces("application/x-ndjson")]
    public async Task ExportDocuments(string id, CancellationToken cancellationToken)
    {
        id = id?.Trim().ToLower();
        if (String.IsNullOrEmpty(id) || !LuceneServerNET.Engine.Extensions.PathGuard.IsValidName(id))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new ApiErrorResult($"Invalid index id '{id}'"), cancellationToken);
            return;
        }
        id = _lucene.ResolveAlias(id);
        if (!_lucene.IndexExists(id))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(new ApiErrorResult("Index not found"), cancellationToken);
            return;
        }

        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers["X-Total-Count"] = _lucene.IndexStats(id).DocumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var newLine = "\n"u8.ToArray();
        int count = 0;

        foreach (var document in _lucene.ExportDocuments(id))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await JsonSerializer.SerializeAsync(Response.Body, document, options, cancellationToken);
            await Response.Body.WriteAsync(newLine, cancellationToken);

            if (++count % 1000 == 0)
            {
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
    }

    /// <summary>Adds documents to the index.</summary>
    [HttpPost("{id}/documents")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> AddDocuments(string id, [FromBody] IEnumerable<IDictionary<string, object>> items)
        => SecureMethodHandler(id, (id) => new ApiResult(_lucene.Index(id, items)));

    /// <summary>Removes all documents where field equals term (default field: _guid).</summary>
    [HttpDelete("{id}/documents")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> RemoveDocuments(string id, [FromQuery] string term, [FromQuery] string field = "_guid")
        => SecureMethodHandler(id, (id) => new ApiResult(_lucene.RemoveDocuments(id, term, field)));

    #endregion

    #region Search

    /// <summary>
    /// Search with paging. Either use from/size (from + size max. 10000)
    /// or searchAfter (cursor from nextSearchAfter of the previous page, relevance sort only).
    /// </summary>
    [HttpGet("{id}/search")]
    public Task<IApiResult> Search(string id,
                                   string q,
                                   string outFields = null,
                                   int from = 0,
                                   int size = 20,
                                   string searchAfter = null,
                                   string sortField = null,
                                   bool sortReverse = false,
                                   string filter = "",
                                   string format = "")
        => SecureMethodHandler(id, (id) =>
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

    /// <summary>Phonetic search with paging (same parameters as search).</summary>
    [HttpGet("{id}/search/phonetic")]
    public Task<IApiResult> SearchPhonetic(string id,
                                           string q,
                                           string outFields = null,
                                           int from = 0,
                                           int size = 20,
                                           string searchAfter = null,
                                           string sortField = null,
                                           bool sortReverse = false,
                                           string filter = "",
                                           string format = "")
        => SecureMethodHandler(id, (id) =>
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

    /// <summary>Groups the hits of q by a field and returns the values with hit counts.</summary>
    [HttpGet("{id}/groups")]
    public Task<IApiResult> Group(string id, [FromQuery] string field, string q)
        => SecureMethodHandler(id, (id) => new LuceneGroupResult()
        {
            Hits = _lucene.GroupBy(id, field, q)
        });

    #endregion
}
