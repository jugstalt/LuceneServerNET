using LuceneServerNET.Core.Models.Custom;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using LuceneServerNET.Engine.Exceptions;
using LuceneServerNET.Engine.Models.Spatial;
using Lucene.Net.QueryParsers.Classic;
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
using System.Diagnostics;
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

    protected Task<IApiResult> SecureMethodHandler(string id, Func<string, IApiResult> func, Func<Exception, IApiResult> onException = null, bool resolveAlias = true)
        => SecureMethodHandler(id, (id) => Task.FromResult(func(id)), onException, resolveAlias);

    protected Task<IApiResult> SecureMethodHandler(Func<IApiResult> func, Func<Exception, IApiResult> onException = null)
        => SecureMethodHandler(() => Task.FromResult(func()), onException);

    /// <summary>
    /// For index specific endpoints: normalizes and validates the index id (whitelist) before calling <paramref name="func"/>.
    /// An alias is resolved to its index unless <paramref name="resolveAlias"/> is false (create/remove index).
    /// </summary>
    protected Task<IApiResult> SecureMethodHandler(string id, Func<string, Task<IApiResult>> func, Func<Exception, IApiResult> onException = null, bool resolveAlias = true)
    {
        return SecureMethodHandler(() =>
        {
            id = id?.Trim().ToLower();

            if (String.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Index id not specified");
            }

            if (!LuceneServerNET.Engine.Extensions.PathGuard.IsValidName(id))
            {
                throw new ArgumentException($"Invalid index id '{id}'. Allowed: a-z, 0-9, '_', '-' (max. 64 characters).");
            }

            return func(resolveAlias ? _lucene.ResolveAlias(id) : id);
        }, onException);
    }

    /// <summary>
    /// If true, errors are returned with a matching HTTP status code (400, 404, 409, 500)
    /// instead of HTTP 200. The body is an <see cref="ApiErrorResult"/> in both cases.
    /// v1 (LuceneController) keeps HTTP 200 for compatibility, v2 (IndicesController) uses status codes.
    /// </summary>
    protected virtual bool UseHttpStatusCodesForErrors => false;

    /// <summary>
    /// For endpoints without index id: error handling and timing only.
    /// </summary>
    protected async Task<IApiResult> SecureMethodHandler(Func<Task<IApiResult>> func, Func<Exception, IApiResult> onException = null)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            var apiResult = await func();

            if (apiResult != null)
            {
                apiResult.MilliSeconds = stopwatch.Elapsed.TotalMilliseconds;
            }

            return apiResult;
        }
        catch (Exception ex)
        {
            if (onException != null)
            {
                return onException(ex);
            }

            int statusCode = ToHttpStatusCode(ex);

            if (UseHttpStatusCodesForErrors)
            {
                Response.StatusCode = statusCode;
            }

            if (statusCode >= 500)
            {
                _logger.LogError(ex, "Error processing {method} {path}", Request?.Method, Request?.Path);

                return new ApiErrorResult(_env.IsDevelopment()
                    ? ex.Message
                    : $"Error of type {ex.GetType()}");
            }

            // client errors: message is safe to return and helps the caller
            _logger.LogInformation("Invalid request {method} {path}: {message}", Request?.Method, Request?.Path, ex.Message);

            return new ApiErrorResult(ex.Message);
        }
    }

    protected static int ToHttpStatusCode(Exception ex) => ex switch
    {
        IndexNotFoundException => StatusCodes.Status404NotFound,
        IndexConflictException => StatusCodes.Status409Conflict,
        ParseException => StatusCodes.Status400BadRequest,
        ArgumentException => StatusCodes.Status400BadRequest,   // incl. ArgumentNullException, ArgumentOutOfRangeException
        FormatException => StatusCodes.Status400BadRequest,     // e.g. invalid numbers in spatial filters
        _ => StatusCodes.Status500InternalServerError
    };

    #endregion
}
