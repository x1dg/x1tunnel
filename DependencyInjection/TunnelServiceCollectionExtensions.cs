using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using X1Beer.Tunnel;
using X1Beer.Tunnel.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class TunnelServiceCollectionExtensions
{
    public static IServiceCollection AddLocalTunnel(
        this IServiceCollection services,
        Action<TunnelOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<TunnelOptions>()
            .BindConfiguration(TunnelOptions.SectionName);

        if (configure is not null)
            services.Configure(configure);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITunnelProvider, CloudflareNamedTunnelProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITunnelProvider, CloudflareQuickTunnelProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITunnelProvider, NgrokTunnelProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITunnelProvider, LocalTunnelProvider>());
        services.TryAddSingleton<ITunnelFactory>(static services => new TunnelFactory(
            services.GetServices<ITunnelProvider>(),
            services.GetRequiredService<ILogger<TunnelFactory>>()));
        services.TryAddSingleton<TunnelInfo>();
        services.TryAddSingleton<ITunnelInfo>(static services => services.GetRequiredService<TunnelInfo>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LocalTunnelHostedService>());
        return services;
    }
}
