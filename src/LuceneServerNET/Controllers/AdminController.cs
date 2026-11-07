using LuceneServerNET.Authentication;
using LuceneServerNET.Core.Models.Admin;
using LuceneServerNET.Core.Models.Result;
using LuceneServerNET.Engine.Services;
using LuceneServerNET.Services.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LuceneServerNET.Controllers;

/// <summary>
/// Administration endpoints (used by the admin UI): /api/v2/admin/...
/// </summary>
[ApiController]
[Route("api/v2/admin")]
[ApiExplorerSettings(GroupName = "v2")]
public class AdminController : LuceneControllerBase
{
    private readonly IAppVersionService _appVersionService;

    public AdminController(LuceneService lucene,
                           IAppVersionService appVersionService,
                           IWebHostEnvironment env,
                           IConfiguration config,
                           ILogger<AdminController> logger)
        : base(lucene, env, config, logger)
    {
        _appVersionService = appVersionService;
    }

    protected override bool UseHttpStatusCodesForErrors => true;

    /// <summary>Server version, platform, uptime and number of indices.</summary>
    [HttpGet("info")]
    [Authorize(Policy = ApiRoles.Admin)]
    public Task<IApiResult> Info()
        => SecureMethodHandler(() =>
        {
            using var process = Process.GetCurrentProcess();
            var started = process.StartTime.ToUniversalTime();

            return new ServerInfoResult()
            {
                Info = new ServerInfo()
                {
                    Version = _appVersionService.Version,
                    Platform = SystemInfo.Platform,
                    Framework = RuntimeInformation.FrameworkDescription,
                    StartedUtc = started,
                    UptimeSeconds = (DateTime.UtcNow - started).TotalSeconds,
                    IndexCount = _lucene.IndexNames().Count(),
                    WorkingSetBytes = process.WorkingSet64
                }
            };
        });

    /// <summary>
    /// Roles of the calling client (read, write, admin). Used by the admin UI to decide which actions it offers.
    /// If authentication is disabled, all roles are returned.
    /// </summary>
    [HttpGet("whoami")]
    [Authorize(Policy = ApiRoles.Read)]
    public Task<IApiResult> WhoAmI()
        => SecureMethodHandler(() =>
        {
            var roles = _config.IsBasicAuthenticationEnabled()
                ? new[] { ApiRoles.Read, ApiRoles.Write, ApiRoles.Admin }.Where(r => User.IsInRole(r)).ToArray()
                : new[] { ApiRoles.Read, ApiRoles.Write, ApiRoles.Admin };

            return new WhoAmIResult()
            {
                ClientId = User?.Identity?.Name,
                Roles = roles
            };
        });
}
