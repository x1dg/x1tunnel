using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace X1Beer.Tunnel;

public sealed class TunnelFactory : ITunnelFactory
{
    private readonly IReadOnlyList<ITunnelProvider> _providers;
    private readonly ILogger<TunnelFactory> _logger;

    public TunnelFactory(ILogger<TunnelFactory>? logger = null)
        : this(CreateDefaultProviders(), logger)
    {
    }

    public TunnelFactory(IEnumerable<ITunnelProvider> providers, ILogger<TunnelFactory>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
        if (_providers.Count == 0)
            throw new ArgumentException("At least one tunnel provider is required.", nameof(providers));

        _logger = logger ?? NullLogger<TunnelFactory>.Instance;
    }

    public static IReadOnlyList<ITunnelProvider> CreateDefaultProviders() =>
    [
        new CloudflareNamedTunnelProvider(),
        new CloudflareQuickTunnelProvider(),
        new NgrokTunnelProvider(),
        new LocalTunnelProvider(),
    ];

    public async Task<ITunnel> StartAsync(TunnelOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var publicUrl = FirstNonEmpty(options.PublicUrl, Environment.GetEnvironmentVariable(TunnelEnvironment.PublicUrl));
        if (publicUrl is not null)
            EnsurePublicHttpUrl(publicUrl);

        var remoteOrigin = UsesRemoteCloudflareOrigin(options);
        options.Validate(requirePort: publicUrl is null && !remoteOrigin);
        if (options.Providers is null)
        {
            if (publicUrl is null && !remoteOrigin)
                throw new ArgumentException("At least one tunnel provider is required.", nameof(options));

            options.Providers = [];
        }

        if (!TunnelOptions.IsLoopback(options.LocalHost))
        {
            _logger.LogWarning(
                "Tunnel target host {Host} is not loopback. Traffic will be forwarded there.",
                options.LocalHost);
        }

        if (remoteOrigin)
        {
            _logger.LogWarning(
                "Cloudflare token mode forwards traffic to the origin stored in the tunnel token, not necessarily {Origin}. RequestedHostname is not checked against the token.",
                options.LocalPort > 0 ? options.LocalOrigin() : "a local port");
        }

        if (publicUrl is not null)
        {
            if (options.WaitForLocalListener && options.LocalPort > 0)
            {
                await LocalListenerProbe.WaitAsync(options.LocalHost, options.LocalPort, options.ListenTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation("Using configured public tunnel URL {Url}", publicUrl);
            return new ExternalUrlTunnel(publicUrl, "external", _logger);
        }

        if (options.WaitForLocalListener && options.LocalPort > 0)
        {
            await LocalListenerProbe.WaitAsync(options.LocalHost, options.LocalPort, options.ListenTimeout, cancellationToken)
                .ConfigureAwait(false);
        }

        if (options.Providers.Count == 0)
            throw new ArgumentException("At least one tunnel provider is required.", nameof(options));

        var errors = new List<string>();
        foreach (var name in options.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = _providers.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (provider is null)
            {
                errors.Add($"[{name}] is not registered");
                _logger.LogInformation("Tunnel provider {Provider} is not registered", name);
                continue;
            }

            if (!provider.CanStart(options))
            {
                errors.Add($"[{name}] is not available");
                _logger.LogInformation("Tunnel provider {Provider} is not available", name);
                continue;
            }

            try
            {
                return await provider.StartAsync(options, _logger, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tunnel provider {Provider} failed", name);
                errors.Add(ex.Message);
            }
        }

        throw new TunnelException(
            "tunnel",
            "No tunnel provider produced a public URL. Install cloudflared, or set PublicUrl / X1TUNNEL_PUBLIC_URL.",
            string.Join(Environment.NewLine, errors));
    }

    private static string? FirstNonEmpty(string? first, string? second)
    {
        if (!string.IsNullOrWhiteSpace(first))
            return first.Trim();

        if (!string.IsNullOrWhiteSpace(second))
            return second.Trim();

        return null;
    }

    private static bool UsesRemoteCloudflareOrigin(TunnelOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CloudflareTunnelToken)
            || string.IsNullOrWhiteSpace(options.RequestedHostname))
            return false;

        var providers = options.Providers;
        return providers is { Count: > 0 }
            && providers.TrueForAll(name =>
                string.Equals(name, TunnelProviderNames.CloudflareNamed, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsurePublicHttpUrl(string publicUrl)
    {
        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new TunnelException("external", $"Public URL must be an absolute http(s) URL. Got '{publicUrl}'.");
    }
}
