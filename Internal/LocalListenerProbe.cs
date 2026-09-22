using System.Net.Sockets;

namespace x1beer.Tunnel;

internal static class LocalListenerProbe
{
    public static async Task WaitAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient();
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromMilliseconds(500));
                await client.ConnectAsync(host, port, connectTimeout.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (SocketException)
            {
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            var delay = remaining < TimeSpan.FromMilliseconds(200) ? remaining : TimeSpan.FromMilliseconds(200);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        throw new TunnelException(
            "listener",
            $"Nothing accepted TCP connections on {host}:{port} within {timeout.TotalSeconds:0}s.");
    }
}
