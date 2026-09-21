using X1Beer.Tunnel;

namespace X1Beer.Tunnel.Hosting;

public sealed class TunnelInfo : ITunnelInfo
{
    private ITunnel? _tunnel;

    public string? PublicUrl => Volatile.Read(ref _tunnel)?.PublicUrl;

    public string? Provider => Volatile.Read(ref _tunnel)?.Provider;

    public bool IsConnected => Volatile.Read(ref _tunnel)?.IsConnected == true;

    internal void Publish(ITunnel tunnel) => Volatile.Write(ref _tunnel, tunnel);

    internal void Clear() => Volatile.Write(ref _tunnel, null);
}
