namespace x1beer.Tunnel;

public interface ITunnelFactory
{
    Task<ITunnel> StartAsync(TunnelOptions options, CancellationToken cancellationToken = default);
}
