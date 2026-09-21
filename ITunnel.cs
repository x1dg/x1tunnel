namespace X1Beer.Tunnel;

public interface ITunnel : IAsyncDisposable
{
    string PublicUrl { get; }

    string Provider { get; }

    bool IsConnected { get; }

    event EventHandler? Disconnected;

    Task StopAsync(CancellationToken cancellationToken = default);
}
