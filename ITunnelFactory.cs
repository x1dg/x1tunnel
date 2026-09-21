namespace X1Beer.Tunnel;

public interface ITunnelFactory
{
    Task<ITunnel> StartAsync(TunnelOptions options, CancellationToken cancellationToken = default);
}
