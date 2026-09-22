using Microsoft.Extensions.Logging;

namespace x1beer.Tunnel;

public sealed class LocalTunnelProvider : ITunnelProvider
{
    public string Name => TunnelConstants.LocalTunnel;

    public bool CanStart(TunnelOptions options) => CommandLocator.Exists("npx");

    public Task<ITunnel> StartAsync(TunnelOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        var arguments = CommandLocator.WithExtra(BuildArguments(options), options.ExtraArguments);
        var startInfo = CommandLocator.ForCommand("npx", arguments);
        return TunnelProcessHost.RunAsync(
            startInfo,
            Name,
            options.LocalOrigin(),
            UrlMatchers.LocalTunnel,
            options.StartTimeout,
            logger,
            cancellationToken);
    }

    internal static IReadOnlyList<string> BuildArguments(TunnelOptions options)
    {
        var arguments = new List<string>
        {
            "--yes",
            "localtunnel",
            "--port",
            options.LocalPort.ToString(),
            "--local-host",
            options.LocalHost,
        };

        if (IsSubdomain(options.RequestedHostname))
        {
            arguments.Add("--subdomain");
            arguments.Add(options.RequestedHostname!);
        }

        return arguments;
    }

    private static bool IsSubdomain(string? hostname) =>
        !string.IsNullOrWhiteSpace(hostname)
        && !hostname.Contains("://", StringComparison.Ordinal)
        && !hostname.Contains('.', StringComparison.Ordinal);
}
