using X1Beer.Tunnel;

namespace X1Beer.Tunnel.Hosting;

public sealed class TunnelInfo : ITunnelInfo
{
    private ITunnel? _tunnel;

    public string? PublicUrl => _tunnel?.PublicUrl;

    public string? Provider => _tunnel?.Provider;

    public bool IsConnected => _tunnel?.IsConnected == true;

    internal void Publish(ITunnel tunnel) => _tunnel = tunnel;
}
