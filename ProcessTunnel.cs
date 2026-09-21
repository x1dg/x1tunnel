using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace X1Beer.Tunnel;

internal sealed class ProcessTunnel : ITunnel
{
    private readonly Process _process;
    private readonly ILogger _logger;
    private int _stopped;
    private int _connected = 1;

    public ProcessTunnel(Process process, string provider, string publicUrl, ILogger logger)
    {
        _process = process;
        Provider = provider;
        PublicUrl = publicUrl;
        _logger = logger;
    }

    public string PublicUrl { get; }

    public string Provider { get; }

    public bool IsConnected
    {
        get
        {
            if (_connected == 0 || _stopped != 0)
                return false;

            try
            {
                return !_process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    public event EventHandler? Disconnected;

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill tunnel process for {Provider}", Provider);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timed out waiting for tunnel process {Provider} to exit", Provider);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tunnel process {Provider} did not exit cleanly", Provider);
        }
        finally
        {
            _process.Dispose();
            MarkDisconnected();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    internal void MarkDisconnected()
    {
        if (Interlocked.Exchange(ref _connected, 0) != 1)
            return;

        try
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tunnel disconnected handler failed for {Provider}", Provider);
        }
    }
}
