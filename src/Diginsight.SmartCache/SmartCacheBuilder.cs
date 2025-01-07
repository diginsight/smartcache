using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diginsight.SmartCache;

public sealed class SmartCacheBuilder
{
    public IServiceCollection Services { get; }
    public IConfiguration Configuration { get; }
    public IHostEnvironment HostEnvironment { get; }
    public ILoggerFactory? LoggerFactory { get; }

    internal SmartCacheBuilder(IServiceCollection services, IConfiguration configuration, IHostEnvironment hostEnvironment, ILoggerFactory? loggerFactory)
    {
        services.AddMemoryCache();

        services.AddClassAwareOptions();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SmartCacheCoreOptions>, ValidateSmartCacheCoreOptions>());
        services
            .VolatilelyConfigure<SmartCacheCoreOptions>()
            .DynamicallyConfigure<DynamicSmartCacheCoreOptions>();

        services.TryAddSingleton<ISmartCache, SmartCache>();
        services.TryAddSingleton(static sp => new Lazy<ISmartCache>(sp.GetRequiredService<ISmartCache>));

        services.TryAddSingleton<ICacheKeyService, CacheKeyService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, SmartCacheHttpMessageHandlerBuilderFilter>());

        services.TryAddSingleton<SmartCacheDownstreamSettings>();

        Services = services;
        Configuration = configuration;
        HostEnvironment = hostEnvironment;
        LoggerFactory = loggerFactory;
    }

    private sealed class ValidateSmartCacheCoreOptions : IValidateOptions<SmartCacheCoreOptions>
    {
        public ValidateOptionsResult Validate(string? name, SmartCacheCoreOptions options)
        {
            if (name != Microsoft.Extensions.Options.Options.DefaultName)
            {
                return ValidateOptionsResult.Skip;
            }

            ICollection<string> messages = new List<string>();
            if (options.LowPrioritySizeThreshold < options.MidPrioritySizeThreshold)
            {
                messages.Add($"{nameof(SmartCacheCoreOptions.LowPrioritySizeThreshold)} must be greater than or equal to {nameof(SmartCacheCoreOptions.MidPrioritySizeThreshold)}");
            }

            int locationPrefetchCount = options.LocationPrefetchCount;
            int locationMaxParallelism = options.LocationMaxParallelism;

            if (locationPrefetchCount <= 0)
            {
                messages.Add($"{nameof(SmartCacheCoreOptions.LocationPrefetchCount)} must be positive");
            }

            if (locationMaxParallelism <= 0)
            {
                messages.Add($"{nameof(SmartCacheCoreOptions.LocationMaxParallelism)} must be positive");
            }

            if (locationPrefetchCount > 0 && locationMaxParallelism > 0 && locationPrefetchCount < locationMaxParallelism)
            {
                messages.Add($"{nameof(SmartCacheCoreOptions.LocationMaxParallelism)} must be less than or equal to {nameof(SmartCacheCoreOptions.LocationPrefetchCount)}");
            }

            return messages.Any() ? ValidateOptionsResult.Fail(messages) : ValidateOptionsResult.Success;
        }
    }
}
