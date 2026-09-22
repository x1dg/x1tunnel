namespace x1beer.Tunnel;

public sealed class TunnelOptions
{
    public const string SectionName = "Tunnel";

    public string LocalHost { get; set; } = "127.0.0.1";

    public int LocalPort { get; set; }

    public string LocalScheme { get; set; } = "http";

    public List<string> Providers { get; set; } = [TunnelConstants.CloudflareQuick];

    public string? RequestedHostname { get; set; }

    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(45);

    public TimeSpan ListenTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public bool WaitForLocalListener { get; set; } = true;

    public string? ExtraArguments { get; set; }

    public string? CloudflareTunnelToken { get; set; }

    public string? CloudflareTunnelName { get; set; }

    public string? PublicUrl { get; set; }

    public bool AllowInNonDevelopment { get; set; }

    public bool Enabled { get; set; } = true;

    public bool ThrowOnStartFailure { get; set; }

    public TunnelOptions Clone() => new()
    {
        LocalHost = LocalHost,
        LocalPort = LocalPort,
        LocalScheme = LocalScheme,
        Providers = Providers is null ? [] : [.. Providers],
        RequestedHostname = RequestedHostname,
        StartTimeout = StartTimeout,
        ListenTimeout = ListenTimeout,
        WaitForLocalListener = WaitForLocalListener,
        ExtraArguments = ExtraArguments,
        CloudflareTunnelToken = CloudflareTunnelToken,
        CloudflareTunnelName = CloudflareTunnelName,
        PublicUrl = PublicUrl,
        AllowInNonDevelopment = AllowInNonDevelopment,
        Enabled = Enabled,
        ThrowOnStartFailure = ThrowOnStartFailure,
    };

    internal void Validate(bool requirePort)
    {
        if (requirePort && (Providers is null || Providers.Count == 0))
            throw new ArgumentException("At least one tunnel provider is required.", nameof(Providers));

        if (requirePort && LocalPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(LocalPort), LocalPort, "LocalPort must be between 1 and 65535.");

        if (LocalPort is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(LocalPort), LocalPort, "LocalPort must be between 0 and 65535.");

        if (LocalScheme is not ("http" or "https"))
            throw new ArgumentException("LocalScheme must be http or https.", nameof(LocalScheme));

        if (string.IsNullOrWhiteSpace(LocalHost) || LocalHost.IndexOfAny([' ', '"', '\'']) >= 0)
            throw new ArgumentException("LocalHost is invalid.", nameof(LocalHost));

        if (StartTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StartTimeout));

        if (ListenTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ListenTimeout));
    }

    internal string LocalOrigin()
    {
        var host = string.IsNullOrWhiteSpace(LocalHost) ? "127.0.0.1" : LocalHost;
        if (host.Contains(':') && !host.StartsWith('['))
            host = "[" + host + "]";

        var scheme = string.IsNullOrWhiteSpace(LocalScheme) ? "http" : LocalScheme;
        return $"{scheme}://{host}:{LocalPort}";
    }

    internal static string ToPublicHttps(string hostname)
    {
        var trimmed = hostname.Trim().TrimEnd('/');
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "https://" + trimmed["http://".Length..];

        return "https://" + trimmed;
    }

    internal static bool IsLoopback(string host) =>
        host is "127.0.0.1" or "localhost" or "::1" or "[::1]";
}
