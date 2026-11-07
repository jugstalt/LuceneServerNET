using LuceneServerNET.Models.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LuceneServerNET.Authentication;

public static class AuthenticationServiceExtensions
{
    public static bool IsBasicAuthenticationEnabled(this IConfiguration config)
        => "basic".Equals(config["Authorization:Type"], StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddLuceneServerAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var enabled = config.IsBasicAuthenticationEnabled();

        if (enabled)
        {
            services.AddAuthentication(BasicAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(BasicAuthenticationHandler.SchemeName, null);
        }

        services.AddAuthorization(options =>
        {
            foreach (var role in new[] { ApiRoles.Read, ApiRoles.Write, ApiRoles.Admin })
            {
                options.AddPolicy(role, policy =>
                {
                    if (enabled)
                    {
                        policy.RequireAuthenticatedUser().RequireRole(role);
                    }
                    else
                    {
                        // authentication disabled => everything is allowed (as before)
                        policy.RequireAssertion(_ => true);
                    }
                });
            }

            if (enabled)
            {
                // endpoints without an explicit policy (e.g. /info) require an authenticated client (as before)
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            }
        });

        return services;
    }

    public static void LogAuthenticationWarnings(this IConfiguration config, ILogger logger)
    {
        if (!config.IsBasicAuthenticationEnabled())
        {
            return;
        }

        var clients = config.GetSection("Authorization:Clients").Get<IEnumerable<ApiClient>>() ?? [];
        if (!clients.Any())
        {
            logger.LogWarning("Basic authentication is enabled, but no clients are configured (Authorization:Clients). All requests will be rejected.");
        }

        foreach (var client in clients)
        {
            if (String.IsNullOrWhiteSpace(client.SecretHash) && !String.IsNullOrEmpty(client.Secret))
            {
                logger.LogWarning("Client {ClientId} uses a plain text secret. Consider using SecretHash (create one with: LuceneServerNET --hash-secret <secret>).", client.Id);
            }
            if (client.Roles is null || !client.Roles.Any())
            {
                logger.LogWarning("Client {ClientId} has no roles configured and gets full access (admin). Consider setting Roles: read, write or admin.", client.Id);
            }
        }
    }
}
