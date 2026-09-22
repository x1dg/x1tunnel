using Microsoft.Extensions.Logging;

namespace x1beer.Tunnel;

public interface ITunnelProvider
{
    string Name { get; }

    bool CanStart(TunnelOptions options);

    Task<ITunnel> StartAsync(TunnelOptions options, ILogger logger, CancellationToken cancellationToken);
}
