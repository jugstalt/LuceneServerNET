using LuceneServerNET.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace LuceneServerNET.Extensions.DependencyInjection;

static public class ServiceCollectionExtensions
{
    static public IServiceCollection AddLuceneService(this IServiceCollection services, Action<LuceneServiceOptions> optionsAction)
    {
        services.Configure<LuceneServiceOptions>(optionsAction);

        services.AddSingleton<LuceneSharedResourcesService>();
        services.AddTransient<LuceneService>();
        services.AddTransient<ArchiveService>();

        return services;
    }

    static public IServiceCollection AddRestoreService(this IServiceCollection services, Action<RestoreServiceOptions> optionsAction)
    {
        services.Configure<RestoreServiceOptions>(optionsAction);

        services.AddTransient<RestoreService>();

        return services;
    }
}
