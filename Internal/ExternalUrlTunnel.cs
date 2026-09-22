using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace x1beer.Tunnel;

internal sealed class ExternalUrlTunnel : ITunnel
{
    private readonly ILogger _logger;
    private int _stopped;

    public ExternalUrlTunnel(string publicUrl, string provider, ILogger? logger = null)
    {
        PublicUrl = publicUrl;
        Provider = provider;
        _logger = logger ?? NullLogger.Instance;
    }

    public string PublicUrl { get; }

    public string Provider { get; }

    public bool IsConnected => _stopped == 0;

    public event EventHandler? Disconnected;

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return Task.CompletedTask;

        try
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tunnel disconnected handler failed for {Provider}", Provider);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
