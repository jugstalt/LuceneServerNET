using LuceneServerNET.Authentication;
using LuceneServerNET.Core.Models.Result;
using LuceneServerNET.Engine.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Controllers;

/// <summary>
/// Index aliases (v2): an alias points to one index and can be used instead of the index id in all
/// endpoints (v1 and v2) except creating and removing indices. Switching an alias is atomic.
/// </summary>
[ApiController]
[Route("api/v2/aliases")]
[ApiExplorerSettings(GroupName = "v2")]
[Authorize(Policy = ApiRoles.Read)]
public class AliasesController : LuceneControllerBase
{
    public AliasesController(LuceneService lucene,
                             IWebHostEnvironment env,
                             IConfiguration config,
                             ILogger<AliasesController> logger)
        : base(lucene, env, config, logger)
    {
    }

    protected override bool UseHttpStatusCodesForErrors => true;

    public class SetAliasRequest
    {
        /// <summary>The index the alias points to.</summary>
        public string Index { get; set; }
    }

    /// <summary>All aliases (alias => index).</summary>
    [HttpGet]
    public Task<IApiResult> GetAliases()
        => SecureMethodHandler(() => new AliasesResult { Aliases = _lucene.Aliases().ToDictionary(a => a.Key, a => a.Value) });

    /// <summary>Creates or switches an alias. The alias must not have the name of an existing index.</summary>
    [HttpPut("{alias}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> SetAlias(string alias, [FromBody] SetAliasRequest request)
        => SecureMethodHandler(() =>
        {
            _lucene.SetAlias(alias?.Trim().ToLower(), request?.Index?.Trim().ToLower());
            return new ApiResult();
        });

    /// <summary>Removes an alias (the index is not changed). HTTP 404 if the alias does not exist.</summary>
    [HttpDelete("{alias}")]
    [Authorize(Policy = ApiRoles.Write)]
    public Task<IApiResult> RemoveAlias(string alias)
        => SecureMethodHandler(() =>
        {
            var removed = _lucene.RemoveAlias(alias?.Trim().ToLower());
            if (!removed)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
            }
            return new ApiResult(removed);
        });
}
