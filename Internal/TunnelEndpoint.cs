namespace x1beer.Tunnel;

internal static class TunnelEndpoint
{
    public static bool TryParse(string? address, out string host, out int port)
    {
        host = "";
        port = 0;
        if (string.IsNullOrWhiteSpace(address))
            return false;

        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Port > 0)
        {
            port = uri.Port;
            host = NormalizeHost(uri.Host);
            return true;
        }

        var separator = address.LastIndexOf(':');
        if (separator < 0 || !int.TryParse(address[(separator + 1)..], out port) || port is < 1 or > 65535)
            return false;

        host = "127.0.0.1";
        return true;
    }

    public static bool TrySelect(
        IEnumerable<string>? addresses,
        string preferredScheme,
        out string host,
        out int port,
        out string scheme)
    {
        host = "";
        port = 0;
        scheme = "";
        (string Host, int Port, string Scheme)? preferred = null;
        (string Host, int Port, string Scheme)? http = null;
        (string Host, int Port, string Scheme)? any = null;

        foreach (var address in addresses ?? [])
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Port is < 1)
                continue;

            var parsed = (Host: NormalizeHost(uri.Host), Port: uri.Port, Scheme: uri.Scheme);
            any ??= parsed;
            if (string.Equals(parsed.Scheme, "http", StringComparison.OrdinalIgnoreCase))
                http ??= parsed;

            if (string.Equals(parsed.Scheme, preferredScheme, StringComparison.OrdinalIgnoreCase))
            {
                preferred = parsed;
                break;
            }
        }

        var chosen = preferred ?? http ?? any;
        if (chosen is null)
            return false;

        host = chosen.Value.Host;
        port = chosen.Value.Port;
        scheme = chosen.Value.Scheme;
        return true;
    }

    private static string NormalizeHost(string host) =>
        host is "::" or "[::]" or "0.0.0.0" or "+" or "*" or ""
            ? "127.0.0.1"
            : host;
}
