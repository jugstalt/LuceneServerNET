using LuceneServerNET.Extensions;
using LuceneServerNET.Extensions.DependencyInjection;
using LuceneServerNET.Middleware.Authentication;
using LuceneServerNET.Services;
using LuceneServerNET.Services.Abstraction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System;

namespace LuceneServerNET
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IAppVersionService, AppVersionService>();

            services.AddLuceneService(options =>
            {
                options.RootPath = Configuration.GetStringValue("LuceneServer:RootPath");
                options.ArchivePath = Configuration.GetStringValue("LuceneServer:ArchivePath");
            });

            services.AddRestoreService(options =>
            {
                options.RestoreOnRestart = Configuration.GetBoolValue("LuceneServer:AutoRestoreOnStartup");
                options.RestoreOnRestartCount = Configuration.GetIntValue("LuceneServer:AutoRestoreOnStartupCount", 0);
                options.RestoreOnRestartSince = Configuration.GetIntValue("LuceneServer:AutoRestoreOnStartupSinceSeconds", 86400);
            });

            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "LuceneServer.NET", Version = "v0.1" });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app,
                              IWebHostEnvironment env,
                              RestoreService restore)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            if (env.IsDevelopment() ||
                "true".Equals(Configuration["useSwagger"], StringComparison.OrdinalIgnoreCase))
            {
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("../swagger/v1/swagger.json", "LuceneServer.NET v1"));
            }

            restore.TryRestoreIndices();

            //app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            switch (Configuration["Authorization:Type"]?.ToLower())
            {
                case "basic":
                    app.UseMiddleware<BasicAuthenticationMiddleware>();
                    break;
            }

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
