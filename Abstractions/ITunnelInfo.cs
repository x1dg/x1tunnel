namespace x1beer.Tunnel;

public interface ITunnelInfo
{
    string? PublicUrl { get; }

    string? Provider { get; }

    bool IsConnected { get; }
}
