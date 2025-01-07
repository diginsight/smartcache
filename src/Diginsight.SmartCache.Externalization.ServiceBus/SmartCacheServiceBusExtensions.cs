using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Diginsight.SmartCache.Externalization.ServiceBus;

public static class SmartCacheServiceBusExtensions
{
    public static SmartCacheBuilder SetServiceBusCompanion(
        this SmartCacheBuilder builder,
        Func<IConfiguration, IHostEnvironment, bool>? isEnabled = null,
        Action<SmartCacheServiceBusOptions>? configureOptions = null
    )
    {
        builder.SetCompanion(new ServiceBusCacheCompanionInstaller(isEnabled));

        if (configureOptions is not null)
        {
            builder.Services.Configure(configureOptions);
        }

        return builder;
    }
}
