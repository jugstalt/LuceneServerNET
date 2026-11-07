using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LuceneServerNET.Admin.Services.Auth;

public class LocalUser
{
    public string Name { get; set; } = "";

    /// <summary>pbkdf2-sha256$... (create with: LuceneServerNET.Admin --hash-password &lt;password&gt;)</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Plain text password (development only, a warning is logged).</summary>
    public string? Password { get; set; }
}

public static class AdminAuthentication
{
    public const string Local = "Local", Oidc = "Oidc", None = "None";

    public static string AuthenticationType(this IConfiguration config)
        => config["Admin:Authentication:Type"] switch
        {
            string t when t.Equals(Oidc, StringComparison.OrdinalIgnoreCase) => Oidc,
            string t when t.Equals(None, StringComparison.OrdinalIgnoreCase) => None,
            _ => Local
        };

    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var type = config.AuthenticationType();

        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            if (type == Oidc)
            {
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            }
        })
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.LogoutPath = "/account/logout";
            options.Cookie.Name = "LuceneServerNET.Admin";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        if (type == Oidc)
        {
            var section = config.GetSection("Admin:Authentication:Oidc");
            authentication.AddOpenIdConnect(options =>
            {
                options.Authority = section["Authority"];
                options.ClientId = section["ClientId"];
                options.ClientSecret = section["ClientSecret"];
                options.ResponseType = "code";
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "roles";
                options.Scope.Clear();
                foreach (var scope in section.GetSection("Scopes").Get<string[]>() ?? ["openid", "profile"])
                {
                    options.Scope.Add(scope);
                }
            });
        }

        services.AddAuthorization(options =>
        {
            if (type == None)
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
                options.DefaultPolicy = options.FallbackPolicy;
            }
            else
            {
                var builder = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
                var requiredRole = config["Admin:Authentication:Oidc:RequiredRole"];
                if (type == Oidc && !String.IsNullOrWhiteSpace(requiredRole))
                {
                    builder.RequireRole(requiredRole);
                }
                options.FallbackPolicy = options.DefaultPolicy = builder.Build();
            }
        });

        services.AddCascadingAuthenticationState();

        return services;
    }

    public static IEnumerable<LocalUser> LocalUsers(this IConfiguration config)
        => config.GetSection("Admin:Authentication:Users").Get<LocalUser[]>() ?? [];

    public static bool VerifyLocalUser(this IConfiguration config, string? name, string? password)
    {
        if (String.IsNullOrWhiteSpace(name) || String.IsNullOrEmpty(password))
        {
            return false;
        }

        var user = config.LocalUsers().FirstOrDefault(u => u.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user == null)
        {
            return false;
        }

        if (!String.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return SecretHasher.Verify(password, user.PasswordHash);
        }

        return !String.IsNullOrEmpty(user.Password) &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                   System.Text.Encoding.UTF8.GetBytes(user.Password), System.Text.Encoding.UTF8.GetBytes(password));
    }

    public static void LogAuthenticationWarnings(this IConfiguration config, ILogger logger)
    {
        switch (config.AuthenticationType())
        {
            case None:
                logger.LogWarning("Admin:Authentication:Type is None: everybody with network access can administrate all instances. Use this only for local development.");
                break;
            case Local:
                var users = config.LocalUsers().ToList();
                if (users.Count == 0)
                {
                    logger.LogWarning("No local users configured (Admin:Authentication:Users). Nobody can log in.");
                }
                foreach (var user in users.Where(u => String.IsNullOrWhiteSpace(u.PasswordHash) && !String.IsNullOrEmpty(u.Password)))
                {
                    logger.LogWarning("User {User} uses a plain text password. Use PasswordHash (LuceneServerNET.Admin --hash-password <password>).", user.Name);
                }
                break;
        }
    }

    /// <summary>POST /account/login (form) and POST /account/logout.</summary>
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var type = app.Configuration.AuthenticationType();

        app.MapPost("/account/login", async (HttpContext context,
                                             [FromForm] string? username,
                                             [FromForm] string? password,
                                             [FromForm] string? returnUrl,
                                             IConfiguration config,
                                             ILogger<LocalUser> logger) =>
        {
            var target = SafeReturnUrl(returnUrl);

            if (type == Oidc)
            {
                return Results.Challenge(new AuthenticationProperties { RedirectUri = target });
            }

            if (!config.VerifyLocalUser(username, password))
            {
                logger.LogWarning("Failed admin login for user {User} from {Ip}", username, context.Connection.RemoteIpAddress);
                await Task.Delay(500);
                return Results.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(target)}");
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, username!.Trim())],
                CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Redirect(target);
        }).AllowAnonymous();

        app.MapGet("/account/oidc-login", (string? returnUrl) =>
            Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
                              [OpenIdConnectDefaults.AuthenticationScheme]))
           .AllowAnonymous();

        app.MapPost("/account/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (type == Oidc)
            {
                return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                                       [OpenIdConnectDefaults.AuthenticationScheme]);
            }
            return Results.Redirect("/login");
        });
    }

    /// <summary>Only local paths (no open redirects).</summary>
    internal static string SafeReturnUrl(string? returnUrl)
        => !String.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";
}
