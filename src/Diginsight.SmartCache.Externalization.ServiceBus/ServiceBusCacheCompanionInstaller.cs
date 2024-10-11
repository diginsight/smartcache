using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Diginsight.SmartCache.Externalization.ServiceBus;

public sealed class ServiceBusCacheCompanionInstaller : ICacheCompanionInstaller
{
    private readonly Func<IConfiguration, IHostEnvironment, bool>? isEnabled;

    public ServiceBusCacheCompanionInstaller(Func<IConfiguration, IHostEnvironment, bool>? isEnabled = null)
    {
        this.isEnabled = isEnabled;
    }

    public bool Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        ILoggerFactory? loggerFactory,
        [MaybeNullWhen(false)] out Action uninstall
    )
    {
        if (isEnabled?.Invoke(configuration, hostEnvironment) == false)
        {
            uninstall = null;
            return false;
        }

        ServiceDescriptor sd0 = ServiceDescriptor.Singleton<ServiceBusCacheCompanion, ServiceBusCacheCompanion>();
        services.TryAdd(sd0);

        ServiceDescriptor sd1 = ServiceDescriptor.Singleton<IHostedService, ServiceBusCacheCompanion>(static sp => sp.GetRequiredService<ServiceBusCacheCompanion>());
        services.TryAddEnumerable(sd1);

        ServiceDescriptor sd2 = ServiceDescriptor.Singleton<ICacheCompanion>(static sp => sp.GetRequiredService<ServiceBusCacheCompanion>());
        services.TryAdd(sd2);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SmartCacheServiceBusOptions>, ValidateSmartCacheServiceBusOptions>());

        uninstall = Uninstall;
        return true;

        void Uninstall()
        {
            services.Remove(sd0);
            services.Remove(sd1);
            services.Remove(sd2);
        }
    }

    private sealed class ValidateSmartCacheServiceBusOptions : IValidateOptions<SmartCacheServiceBusOptions>
    {
        public ValidateOptionsResult Validate(string? name, SmartCacheServiceBusOptions options)
        {
            if (name != Microsoft.Extensions.Options.Options.DefaultName)
            {
                return ValidateOptionsResult.Skip;
            }

            ICollection<string> failureMessages = new List<string>();
            if (string.IsNullOrEmpty(options.ConnectionString))
            {
                failureMessages.Add($"{nameof(SmartCacheServiceBusOptions.ConnectionString)} must be non-empty");
            }
            if (string.IsNullOrEmpty(options.TopicName))
            {
                failureMessages.Add($"{nameof(SmartCacheServiceBusOptions.TopicName)} must be non-empty");
            }
            if (string.IsNullOrEmpty(options.SubscriptionName))
            {
                failureMessages.Add($"{nameof(SmartCacheServiceBusOptions.SubscriptionName)} must be non-empty");
            }
            if (options.RequestTimeout < TimeSpan.FromSeconds(1))
            {
                failureMessages.Add($"{nameof(SmartCacheServiceBusOptions.RequestTimeout)} must be at least 1 second");
            }

            return failureMessages.Count > 0 ? ValidateOptionsResult.Fail(failureMessages) : ValidateOptionsResult.Success;
        }
    }
}
