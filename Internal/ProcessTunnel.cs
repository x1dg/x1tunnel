using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace x1beer.Tunnel;

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

        TryKill();
        if (!await WaitForExitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("Timed out waiting for tunnel process {Provider} to exit", Provider);
            TryKill();
            if (!await WaitForExitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false))
                _logger.LogError("Tunnel process {Pid} for {Provider} is still running after kill", SafePid(), Provider);
        }

        try
        {
            _process.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release tunnel process handle for {Provider}", Provider);
        }

        MarkDisconnected();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private void TryKill()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill tunnel process for {Provider}", Provider);
        }
    }

    private async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            await _process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tunnel process {Provider} did not exit cleanly", Provider);
            return false;
        }
    }

    private int SafePid()
    {
        try
        {
            return _process.Id;
        }
        catch
        {
            return -1;
        }
    }

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
