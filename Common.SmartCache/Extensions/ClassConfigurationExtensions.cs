using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Common;

public static class ClassConfigurationExtensions
{
    public static IServiceCollection AddClassConfiguration(this IServiceCollection services)
    {
        services.TryAddSingleton(typeof(IClassConfigurationGetter<>), typeof(ClassConfigurationGetter<>));
        services.TryAddSingleton<IClassConfigurationGetterProvider, ClassConfigurationGetterProvider>();
        return services;
    }
}
