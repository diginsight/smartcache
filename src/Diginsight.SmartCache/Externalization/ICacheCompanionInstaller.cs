using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Diginsight.SmartCache.Externalization;

public interface ICacheCompanionInstaller
{
    bool Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        ILoggerFactory? loggerFactory,
        [MaybeNullWhen(false)] out Action uninstall
    );
}
