using Microsoft.Extensions.Logging;

namespace X1Beer.Tunnel;

public sealed class CloudflareNamedTunnelProvider : ITunnelProvider
{
    public string Name => TunnelProviderNames.CloudflareNamed;

    public bool CanStart(TunnelOptions options) =>
        CommandLocator.Exists("cloudflared") && BuildArguments(options) is not null;

    public Task<ITunnel> StartAsync(TunnelOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        var arguments = BuildArguments(options)
            ?? throw new TunnelException(Name, "Set CloudflareTunnelToken or CloudflareTunnelName together with RequestedHostname.");

        if (!string.IsNullOrWhiteSpace(options.CloudflareTunnelToken))
        {
            logger.LogInformation(
                "Starting a remotely managed Cloudflare tunnel. The origin URL is taken from the token configuration.");
        }

        var startInfo = CommandLocator.ForCommand("cloudflared", CommandLocator.WithExtra(arguments, options.ExtraArguments));
        var publicUrl = TunnelOptions.ToPublicHttps(options.RequestedHostname!);
        var reportedOrigin = string.IsNullOrWhiteSpace(options.CloudflareTunnelToken)
            ? options.LocalOrigin()
            : "origin from the Cloudflare tunnel token";
        return TunnelProcessHost.RunAsync(
            startInfo,
            Name,
            reportedOrigin,
            line => UrlMatchers.CloudflareNamed(line, publicUrl),
            options.StartTimeout,
            logger,
            cancellationToken);
    }

    internal static IReadOnlyList<string>? BuildArguments(TunnelOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RequestedHostname))
            return null;

        if (!string.IsNullOrWhiteSpace(options.CloudflareTunnelToken))
        {
            if (options.CloudflareTunnelToken.Any(char.IsWhiteSpace))
                return null;

            return
            [
                "tunnel",
                "run",
                "--token",
                options.CloudflareTunnelToken,
            ];
        }

        if (string.IsNullOrWhiteSpace(options.CloudflareTunnelName))
            return null;

        return
        [
            "tunnel",
            "--url",
            options.LocalOrigin(),
            "--hostname",
            options.RequestedHostname,
            "--name",
            options.CloudflareTunnelName,
        ];
    }
}
