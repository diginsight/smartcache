using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Diginsight.SmartCache.Externalization.Kubernetes;

public sealed class KubernetesCacheCompanionInstaller : ICacheCompanionInstaller
{
    private readonly Func<IConfiguration, IHostEnvironment, bool>? isEnabled;

    public KubernetesCacheCompanionInstaller(Func<IConfiguration, IHostEnvironment, bool>? isEnabled = null)
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

        services
            .AddHttpClient(nameof(KubernetesCacheCompanion))
            .ConfigureHttpClient(
                static (sp, client) =>
                {
                    ISmartCacheKubernetesOptions options = sp.GetRequiredService<IOptions<SmartCacheKubernetesOptions>>().Value;
                    client.Timeout = options.PodRequestTimeout;
                }
            );

        ServiceDescriptor sd0 = ServiceDescriptor.Singleton<ICacheCompanion, KubernetesCacheCompanion>();
        services.TryAdd(sd0);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SmartCacheKubernetesOptions>, ValidateSmartCacheKubernetesOptions>());

        uninstall = Uninstall;
        return true;

        void Uninstall()
        {
            services.Remove(sd0);
        }
    }

    private sealed class ValidateSmartCacheKubernetesOptions : IValidateOptions<SmartCacheKubernetesOptions>
    {
        public ValidateOptionsResult Validate(string? name, SmartCacheKubernetesOptions options)
        {
            if (name != Microsoft.Extensions.Options.Options.DefaultName)
            {
                return ValidateOptionsResult.Skip;
            }

            ICollection<string> failureMessages = new List<string>();
            if (string.IsNullOrEmpty(options.PodsDnsName))
            {
                failureMessages.Add($"{nameof(SmartCacheKubernetesOptions.PodsDnsName)} must be non-empty");
            }
            if (string.IsNullOrEmpty(options.PodIpEnvVariableName))
            {
                failureMessages.Add($"{nameof(SmartCacheKubernetesOptions.PodIpEnvVariableName)} must be non-empty");
            }
            if (options.PodRequestTimeout < TimeSpan.FromSeconds(1))
            {
                failureMessages.Add($"{nameof(SmartCacheKubernetesOptions.PodRequestTimeout)} must be at least 1 second");
            }

            return failureMessages.Count > 0 ? ValidateOptionsResult.Fail(failureMessages) : ValidateOptionsResult.Success;
        }
    }
}
