using Microsoft.Extensions.Logging;

namespace X1Beer.Tunnel;

public interface ITunnelProvider
{
    string Name { get; }

    bool CanStart(TunnelOptions options);

    Task<ITunnel> StartAsync(TunnelOptions options, ILogger logger, CancellationToken cancellationToken);
}
