using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using X1Beer.Tunnel;

namespace X1Beer.Tunnel.Hosting;

internal sealed class LocalTunnelHostedService : BackgroundService
{
    private readonly ITunnelFactory _factory;
    private readonly IOptions<TunnelOptions> _options;
    private readonly IServer _server;
    private readonly IHostEnvironment _environment;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TunnelInfo _info;
    private readonly ILogger<LocalTunnelHostedService> _logger;

    public LocalTunnelHostedService(
        ITunnelFactory factory,
        IOptions<TunnelOptions> options,
        IServer server,
        IHostEnvironment environment,
        IHostApplicationLifetime lifetime,
        TunnelInfo info,
        ILogger<LocalTunnelHostedService> logger)
    {
        _factory = factory;
        _options = options;
        _server = server;
        _environment = environment;
        _lifetime = lifetime;
        _info = info;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value.Clone();
        if (!options.Enabled)
            return;

        if (!_environment.IsDevelopment() && !options.AllowInNonDevelopment)
        {
            _logger.LogWarning(
                "Local tunnel skipped outside Development. Set Tunnel:AllowInNonDevelopment to start it.");
            return;
        }

        try
        {
            if (!_lifetime.ApplicationStarted.IsCancellationRequested)
            {
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
                await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
            }

            if (options.LocalPort == 0)
                ApplyServerAddress(options);

            await using var tunnel = await _factory.StartAsync(options, stoppingToken).ConfigureAwait(false);
            _info.Publish(tunnel);
            try
            {
                _logger.LogInformation("Local tunnel {Provider} is {Url}", tunnel.Provider, tunnel.PublicUrl);
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                _info.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _info.Clear();
            _logger.LogError(ex, "Local tunnel failed to start");
            if (options.ThrowOnStartFailure)
                _lifetime.StopApplication();
        }
    }

    private void ApplyServerAddress(TunnelOptions options)
    {
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (!TunnelEndpoint.TrySelect(addresses, options.LocalScheme, out var host, out var port, out var scheme))
            throw new TunnelException("tunnel", "Set Tunnel:LocalPort. The server did not publish a bind address.");

        options.LocalPort = port;
        options.LocalHost = host;
        options.LocalScheme = scheme;
    }
}
