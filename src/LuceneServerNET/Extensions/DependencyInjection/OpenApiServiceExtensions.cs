using LuceneServerNET.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using System.Collections.Generic;

namespace LuceneServerNET.Extensions.DependencyInjection;

/// <summary>
/// OpenAPI documents (Microsoft.AspNetCore.OpenApi) and API reference UI (Scalar).
///   /openapi/v1.json, /openapi/v2.json  OpenAPI documents
///   /scalar                             API reference UI
///   /swagger, /swagger/{v}/swagger.json  redirects (backwards compatibility)
/// </summary>
static public class OpenApiServiceExtensions
{
    private static readonly (string Name, string Title, string Description)[] Documents =
    {
        ("v2", "LuceneServer.NET v2", "Resource oriented API (/api/v2/indices/{id}/...) with paging and total hits."),
        ("v1", "LuceneServer.NET v1 (deprecated)", "Legacy RPC style API (/lucene/...). Still supported, use v2 for new clients.")
    };

    static public IServiceCollection AddLuceneServerOpenApi(this IServiceCollection services, IConfiguration configuration)
    {
        var basicAuth = configuration.IsBasicAuthenticationEnabled();

        foreach (var doc in Documents)
        {
            // Endpoints are assigned via [ApiExplorerSettings(GroupName = "v1"/"v2")];
            // endpoints without group (e.g. /info) appear in every document.
            services.AddOpenApi(doc.Name, options =>
            {
                options.AddDocumentTransformer((document, context, ct) =>
                {
                    document.Info = new OpenApiInfo
                    {
                        Title = doc.Title,
                        Version = doc.Name,
                        Description = doc.Description
                    };

                    if (basicAuth)
                    {
                        document.Components ??= new OpenApiComponents();
                        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                        document.Components.SecuritySchemes[BasicAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
                        {
                            Type = SecuritySchemeType.Http,
                            Scheme = "basic",
                            Description = "HTTP Basic authentication (client id / secret)"
                        };

                        document.Security ??= new List<OpenApiSecurityRequirement>();
                        document.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference(BasicAuthenticationHandler.SchemeName, document)] = new List<string>()
                        });
                    }

                    return System.Threading.Tasks.Task.CompletedTask;
                });
            });
        }

        return services;
    }

    static public IEndpointRouteBuilder MapLuceneServerOpenApi(this IEndpointRouteBuilder app)
    {
        app.MapOpenApi().AllowAnonymous();

        app.MapScalarApiReference("/scalar", options =>
        {
            options.WithTitle("LuceneServer.NET");
            foreach (var doc in Documents)
            {
                options.AddDocument(doc.Name, doc.Title);
            }
        }).AllowAnonymous();

        // backwards compatibility: old Swashbuckle URLs
        app.MapGet("/swagger", () => Results.Redirect("/scalar"))
           .AllowAnonymous().ExcludeFromDescription();
        app.MapGet("/swagger/index.html", () => Results.Redirect("/scalar"))
           .AllowAnonymous().ExcludeFromDescription();
        app.MapGet("/swagger/{version}/swagger.json", (string version) => Results.Redirect($"/openapi/{version}.json"))
           .AllowAnonymous().ExcludeFromDescription();

        return app;
    }
}
