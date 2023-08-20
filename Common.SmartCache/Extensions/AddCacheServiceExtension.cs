using Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.IO;

namespace Common.SmartCache
{
    /// <summary>
    ///     Extension class.
    /// </summary>
    public static class AddCacheServiceExtension
    {
        private static readonly Type T = typeof(AddCacheServiceExtension);

        /// <summary>
        ///     Extension method for registering cache related services.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        /// <param name="hostEnvironment"></param>
        public static IServiceCollection AddCacheService(this IServiceCollection services, IConfiguration configuration, IHostEnvironment hostEnvironment) // 
        {
            using var scope = TraceLogger.BeginMethodScope(T);

            services.Configure<CacheServiceOptions>(configuration.GetSection(nameof(CacheServiceOptions)));
            services.AddMemoryCache();
            services.AddSingleton<IConfigureOptions<MemoryCacheOptions>, SetMemoryCacheSizeLimit>();

            string cachePersistenceFolder = Path.Combine(hostEnvironment.ContentRootPath, "cachePersistence");
            scope.LogDebug(() => new { cachePersistenceFolder });
            Directory.CreateDirectory(cachePersistenceFolder);
            services.AddSingleton<ICachePersistenceFileProvider>(new CachePersistenceFileProvider(new PhysicalFileProvider(cachePersistenceFolder)));
            services.AddSingleton<ICachePersistence, CachePersistence>();
            services.AddSingleton<ICacheService, CacheService>();
            services.AddSingleton<ICacheKeyService, CacheKeyService>();

            services.AddHttpClient(nameof(CacheService))
                .ConfigureHttpClient(
                    static (sp, hc) =>
                    {
                        var options = sp.GetRequiredService<IOptions<CacheServiceOptions>>().Value;
                        hc.Timeout = options.CrossPodRequestTimeout > TimeSpan.Zero ? options.CrossPodRequestTimeout : TimeSpan.FromSeconds(5);
                    });

            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration.GetValue<string>("RedisCache:Configuration")));
            services.AddSingleton(static p => p.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

            return services;
        }

        private sealed class SetMemoryCacheSizeLimit : IConfigureOptions<MemoryCacheOptions>
        {
            private readonly CacheServiceOptions cacheServiceOptions;

            public SetMemoryCacheSizeLimit(IOptions<CacheServiceOptions> cacheServiceOptions)
            {
                this.cacheServiceOptions = cacheServiceOptions.Value;
            }

            public void Configure(MemoryCacheOptions options)
            {
                options.SizeLimit = cacheServiceOptions.SizeLimit * 1024 * 1024;
            }
        }
    }
}
