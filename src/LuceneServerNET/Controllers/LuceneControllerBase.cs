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

/// <summary>
/// Shared helpers for the v1 (LuceneController) and v2 (IndicesController) API.
/// </summary>
public abstract class LuceneControllerBase : ControllerBase
{
    protected readonly LuceneService _lucene;
    protected readonly ILogger _logger;
    protected readonly IWebHostEnvironment _env;
    protected readonly IConfiguration _config;

    protected LuceneControllerBase(LuceneService lucene,
                                   IWebHostEnvironment env,
                                   IConfiguration config,
                                   ILogger logger)
    {
        _config = config;
        _lucene = lucene;
        _env = env;
        _logger = logger;
    }

    #region Helper

    public const string AllowLegacyGetMutationsConfigKey = "LuceneServer:AllowLegacyGetMutations";

    /// <summary>
    /// Handles deprecated GET calls of state-changing endpoints.
    /// Allowed only if config LuceneServer:AllowLegacyGetMutations is true (default: true).
    /// </summary>
    protected async Task<IApiResult> LegacyGet(string successorMethod, Func<Task<IApiResult>> func)
    {
        if (!_config.GetValue(AllowLegacyGetMutationsConfigKey, true))
        {
            Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            Response.Headers["Allow"] = successorMethod;
            return new ApiErrorResult($"HTTP GET is not allowed for this endpoint. Use {successorMethod}.");
        }

        _logger.LogWarning("Deprecated HTTP GET call to {path}. Use {method} instead.", Request.Path, successorMethod);
        Response.Headers["Deprecation"] = "true";
        Response.Headers["Warning"] = $"299 - \"Deprecated: use HTTP {successorMethod} for this endpoint\"";

        return await func();
    }

    protected ISpatialFilter ParseSpatialFilter(string filter)
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

    protected static bool TryGetFilterArguments(string filter, string name, out string arguments)
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

    protected static IApiResult ToSearchResult(SearchPage page, int from, int size, string format)
    {
        int geoJsonIndex = (format ?? String.Empty).IndexOf(":geojson", StringComparison.OrdinalIgnoreCase);
        if (geoJsonIndex >= 0 && format.EndsWith(":geojson", StringComparison.OrdinalIgnoreCase))
        {
            return new GeoJsonResult(format.Substring(0, geoJsonIndex), page.Hits);
        }

        return new LuceneSearchResult()
        {
            Hits = page.Hits,
            TotalHits = page.TotalHits,
            From = from,
            Size = size,
            NextSearchAfter = page.NextSearchAfter
        };
    }

    protected Task<IApiResult> SecureMethodHandler(string id, Func<string, IApiResult> func, Func<Exception, IApiResult> onException = null)
        => SecureMethodHandler(id, (id) => Task.FromResult(func(id)), onException);

    protected Task<IApiResult> SecureMethodHandler(Func<IApiResult> func, Func<Exception, IApiResult> onException = null)
        => SecureMethodHandler(() => Task.FromResult(func()), onException);

    /// <summary>
    /// For index specific endpoints: normalizes and validates the index id (whitelist) before calling <paramref name="func"/>.
    /// </summary>
    protected Task<IApiResult> SecureMethodHandler(string id, Func<string, Task<IApiResult>> func, Func<Exception, IApiResult> onException = null)
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
    protected async Task<IApiResult> SecureMethodHandler(Func<Task<IApiResult>> func, Func<Exception, IApiResult> onException = null)
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
