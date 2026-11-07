using LuceneServerNET.Core.Models.Result;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;

namespace LuceneServerNET.Diagnostics;

/// <summary>
/// Custom metrics of LuceneServer.NET (exported via OpenTelemetry, see ServiceDefaults):
///   luceneserver.search.duration   histogram (s), tags: index, operation
///   luceneserver.search.requests   counter, tags: index, operation, outcome (success|error)
///   luceneserver.documents.indexed counter of documents sent to index requests, tag: index
/// HTTP request metrics per route come from the ASP.NET Core instrumentation (http.server.request.duration).
/// </summary>
public sealed class LuceneServerMetrics : IDisposable
{
    public const string MeterName = "LuceneServerNET";

    private readonly Meter _meter;
    private readonly Histogram<double> _searchDuration;
    private readonly Counter<long> _searchRequests;
    private readonly Counter<long> _documentsIndexed;

    public LuceneServerMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _searchDuration = _meter.CreateHistogram<double>("luceneserver.search.duration", unit: "s",
            description: "Duration of search, phonetic search and group-by requests");
        _searchRequests = _meter.CreateCounter<long>("luceneserver.search.requests",
            description: "Number of search, phonetic search and group-by requests");
        _documentsIndexed = _meter.CreateCounter<long>("luceneserver.documents.indexed",
            description: "Number of documents sent to index requests");
    }

    public void RecordSearch(string index, string operation, TimeSpan duration, bool success)
    {
        var tags = new TagList
        {
            { "index", index },
            { "operation", operation }
        };
        _searchDuration.Record(duration.TotalSeconds, tags);

        tags.Add("outcome", success ? "success" : "error");
        _searchRequests.Add(1, tags);
    }

    public void RecordIndexed(string index, int count)
    {
        if (count > 0)
        {
            _documentsIndexed.Add(count, new KeyValuePair<string, object>("index", index));
        }
    }

    public void Dispose() => _meter.Dispose();
}

/// <summary>
/// Global MVC filter: records the search metrics for the search, phonetic search and group actions
/// and the document count of index requests (v1 and v2 controllers) without touching the controller code.
/// </summary>
public sealed class SearchMetricsFilter : IAsyncActionFilter
{
    private readonly LuceneServerMetrics _metrics;

    public SearchMetricsFilter(LuceneServerMetrics metrics)
    {
        _metrics = metrics;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = context.ActionDescriptor.RouteValues.TryGetValue("action", out var a) ? a ?? "" : "";

        if (action is "Index" or "AddDocuments")
        {
            var executedIndex = await next();
            if (Succeeded(executedIndex) && context.ActionArguments.TryGetValue("items", out var items) && items is ICollection collection)
            {
                _metrics.RecordIndexed(IndexOf(context), collection.Count);
            }
            return;
        }

        var operation = action switch
        {
            "Search" => "search",
            "PhoneticSearch" or "SearchPhonetic" => "phonetic",
            "Group" => "groupby",
            _ => null
        };

        if (operation == null)
        {
            await next();
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var executed = await next();

        _metrics.RecordSearch(IndexOf(context), operation, Stopwatch.GetElapsedTime(start), Succeeded(executed));
    }

    private static bool Succeeded(ActionExecutedContext executed)
    {
        if (executed.Exception != null && !executed.ExceptionHandled)
        {
            return false;
        }
        return executed.Result switch
        {
            ObjectResult { Value: IApiResult apiResult } => apiResult.Success,
            ObjectResult { StatusCode: >= 400 } => false,
            StatusCodeResult { StatusCode: >= 400 } => false,
            _ => true
        };
    }

    private static string IndexOf(ActionExecutingContext context)
    {
        foreach (var key in new[] { "id", "index", "indexName" })
        {
            if (context.RouteData.Values.TryGetValue(key, out var value) && value != null)
            {
                return value.ToString();
            }
        }
        return "";
    }
}
