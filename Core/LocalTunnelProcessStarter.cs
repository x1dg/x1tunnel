namespace x1beer.Tunnel;

[Obsolete("Use TunnelFactory.StartAsync and dispose the returned ITunnel.")]
public static class LocalTunnelProcessStarter
{
    private static ITunnel? _tunnel;

    public static bool IsRunning => _tunnel?.IsConnected == true;

    public static string? ActiveTunnelUrl => IsRunning ? _tunnel?.PublicUrl : null;

    public static async Task<string?> StartAsync(int port, TimeSpan? timeout = null)
    {
        StopExistingTunnel();
        try
        {
            _tunnel = await new TunnelFactory().StartAsync(new TunnelOptions
            {
                LocalPort = port,
                StartTimeout = timeout ?? TimeSpan.FromSeconds(45),
                WaitForLocalListener = false,
                Providers =
                [
                    TunnelConstants.CloudflareQuick,
                    TunnelConstants.LocalTunnel,
                ],
            }).ConfigureAwait(false);

            return _tunnel.PublicUrl;
        }
        catch (TunnelException)
        {
            return null;
        }
    }

    public static void StopExistingTunnel()
    {
        var tunnel = Interlocked.Exchange(ref _tunnel, null);
        if (tunnel is null)
            return;

        tunnel.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }
}
