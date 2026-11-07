using LuceneServerNET.Models.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace LuceneServerNET.Authentication;

public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Basic";
    private const string Realm = "LuceneServerNET";

    private readonly IConfiguration _config;

    public BasicAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
                                      ILoggerFactory logger,
                                      UrlEncoder encoder,
                                      IConfiguration config)
        : base(options, logger, encoder)
    {
        _config = config;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();

        if (String.IsNullOrEmpty(authHeader) ||
            !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader.Substring("Basic ".Length).Trim()));
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid basic authentication header"));
        }

        var pos = credentials.IndexOf(':');
        if (pos <= 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid basic authentication header"));
        }

        var name = credentials.Substring(0, pos);
        var password = credentials.Substring(pos + 1);

        var clients = _config.GetSection("Authorization:Clients").Get<IEnumerable<ApiClient>>() ?? [];
        var client = clients.FirstOrDefault(c => name.Equals(c.Id, StringComparison.OrdinalIgnoreCase));

        if (client is null || !VerifySecret(client, password))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid client id or secret"));
        }

        // no roles configured => admin
        // (backwards compatible: before roles existed, every authenticated client could do everything)
        IEnumerable<string> configuredRoles = client.Roles is null || !client.Roles.Any()
            ? [ApiRoles.Admin]
            : client.Roles;

        var claims = new List<Claim> { new Claim(ClaimTypes.Name, client.Id) };
        claims.AddRange(configuredRoles.SelectMany(ApiRoles.Expand)
                                       .Distinct()
                                       .Select(r => new Claim(ClaimTypes.Role, r)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";

        return Task.CompletedTask;
    }

    private static bool VerifySecret(ApiClient client, string password)
    {
        if (!String.IsNullOrWhiteSpace(client.SecretHash))
        {
            return SecretHasher.Verify(password, client.SecretHash);
        }

        if (!String.IsNullOrEmpty(client.Secret))
        {
            return SecretHasher.FixedTimeEquals(password, client.Secret);
        }

        return false;
    }
}
