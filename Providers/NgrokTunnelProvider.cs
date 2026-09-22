using Microsoft.Extensions.Logging;

namespace x1beer.Tunnel;

public sealed class NgrokTunnelProvider : ITunnelProvider
{
    public string Name => TunnelConstants.Ngrok;

    public bool CanStart(TunnelOptions options) => CommandLocator.Exists("ngrok");

    public Task<ITunnel> StartAsync(TunnelOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        var arguments = CommandLocator.WithExtra(BuildArguments(options), options.ExtraArguments);
        var startInfo = CommandLocator.ForCommand("ngrok", arguments);
        return TunnelProcessHost.RunAsync(
            startInfo,
            Name,
            options.LocalOrigin(),
            UrlMatchers.Ngrok,
            options.StartTimeout,
            logger,
            cancellationToken);
    }

    internal static IReadOnlyList<string> BuildArguments(TunnelOptions options)
    {
        var arguments = new List<string>
        {
            "http",
            options.LocalOrigin(),
            "--log",
            "stdout",
            "--log-format",
            "logfmt",
        };

        if (!string.IsNullOrWhiteSpace(options.RequestedHostname))
        {
            arguments.Add("--url");
            arguments.Add(TunnelOptions.ToPublicHttps(options.RequestedHostname));
        }

        return arguments;
    }
}
