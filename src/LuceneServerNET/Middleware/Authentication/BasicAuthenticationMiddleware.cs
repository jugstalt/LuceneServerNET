using LuceneServerNET.Models.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuceneServerNET.Middleware.Authentication
{
    public class BasicAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _config;

        public BasicAuthenticationMiddleware(RequestDelegate next,
                                             IConfiguration config)
        {
            _next = next;
            _config = config;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if ("Basic".Equals(_config["Authorization:Type"], StringComparison.InvariantCultureIgnoreCase))
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();

                if (authHeader.StartsWith("Basic ", StringComparison.InvariantCultureIgnoreCase))
                {
                    var authCode =
                        System.Text.Encoding.ASCII.GetString(
                            Convert.FromBase64String(authHeader.Substring("basic ".Length)));

                    var pos = authCode.IndexOf(":");

                    if (pos > 0)
                    {
                        var name = authCode.Substring(0, pos);
                        var password = authCode.Substring(pos + 1);

                        var users = _config.GetSection("Authorization:Clients").Get<IEnumerable<ApiClient>>();
                        var user = users?.Where(u => name.Equals(u.Id, StringComparison.InvariantCultureIgnoreCase) && password == u.Secret)
                                         .FirstOrDefault();

                        if (user != null)
                        {
                            await _next(context);
                            return;
                        }
                    }
                }

                context.Response.StatusCode = 401;
                context.Response.Headers.Add("WWW-Authenticate", @"Basic realm=""Access to message queue""");
            }
            else
            {
                await _next(context);
            }
        }
    }
}
