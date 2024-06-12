using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Diginsight.SmartCache.Externalization.Local;

public sealed class LocalCacheCompanionInstaller : ICacheCompanionInstaller
{
    public static readonly ICacheCompanionInstaller Instance = new LocalCacheCompanionInstaller();

    private LocalCacheCompanionInstaller() { }

    public bool Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        ILoggerFactory? loggerFactory,
        [MaybeNullWhen(false)] out Action uninstall
    )
    {
        ServiceDescriptor sd0 = ServiceDescriptor.Singleton<ICacheCompanion, LocalCacheCompanion>();
        services.TryAdd(sd0);

        uninstall = Uninstall;
        return true;

        void Uninstall()
        {
            services.Remove(sd0);
        }
    }
}
