namespace X1Beer.Tunnel;

internal sealed class ExternalUrlTunnel : ITunnel
{
    private int _stopped;

    public ExternalUrlTunnel(string publicUrl, string provider)
    {
        PublicUrl = publicUrl;
        Provider = provider;
    }

    public string PublicUrl { get; }

    public string Provider { get; }

    public bool IsConnected => _stopped == 0;

    public event EventHandler? Disconnected;

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
            Disconnected?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
