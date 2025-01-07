using Diginsight.SmartCache.Externalization.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Diginsight.SmartCache.Externalization.AspNetCore;

public static class SmartCacheAspNetCoreExtensions
{
    public static SmartCacheBuilder AddMiddleware(
        this SmartCacheBuilder builder, Action<SmartCacheHttpOptions>? configureOptions = null
    )
    {
        builder.AddHttp(configureOptions);
        builder.Services.TryAddTransient<SmartCacheMiddleware>();

        return builder;
    }

    public static IApplicationBuilder UseSmartCache(this IApplicationBuilder app)
    {
        if (app.ApplicationServices.GetService(typeof(SmartCacheMiddleware)) is null)
        {
            throw new InvalidOperationException("Middleware not available in the service provider");
        }

        return app.UseMiddleware<SmartCacheMiddleware>();
    }
}
