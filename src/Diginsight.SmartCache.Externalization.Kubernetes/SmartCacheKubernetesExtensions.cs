using Diginsight.SmartCache.Externalization.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Diginsight.SmartCache.Externalization.Kubernetes;

public static class SmartCacheKubernetesExtensions
{
    public static SmartCacheBuilder SetKubernetesCompanion(
        this SmartCacheBuilder builder,
        Func<IConfiguration, IHostEnvironment, bool>? isEnabled = null,
        Action<SmartCacheKubernetesOptions>? configureKubernetesOptions = null,
        Action<SmartCacheHttpOptions>? configureHttpOptions = null
    )
    {
        builder
            .AddHttp(configureHttpOptions)
            .SetCompanion(new KubernetesCacheCompanionInstaller(isEnabled));

        if (configureKubernetesOptions is not null)
        {
            builder.Services.Configure(configureKubernetesOptions);
        }

        return builder;
    }
}
