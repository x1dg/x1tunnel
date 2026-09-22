using Microsoft.Extensions.Logging;

namespace x1beer.Tunnel;

public sealed class CloudflareQuickTunnelProvider : ITunnelProvider
{
    public string Name => TunnelProviderNames.CloudflareQuick;

    public bool CanStart(TunnelOptions options) => CommandLocator.Exists("cloudflared");

    public Task<ITunnel> StartAsync(TunnelOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        var arguments = CommandLocator.WithExtra(BuildArguments(options), options.ExtraArguments);
        var startInfo = CommandLocator.ForCommand("cloudflared", arguments);
        return TunnelProcessHost.RunAsync(
            startInfo,
            Name,
            options.LocalOrigin(),
            UrlMatchers.CloudflareQuick,
            options.StartTimeout,
            logger,
            cancellationToken);
    }

    internal static IReadOnlyList<string> BuildArguments(TunnelOptions options) =>
    [
        "tunnel",
        "--url",
        options.LocalOrigin(),
    ];
}
