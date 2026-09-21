namespace X1Beer.Tunnel;

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
            host = uri.Host is "::" or "[::]" or "0.0.0.0" or "+" or "*" or ""
                ? "127.0.0.1"
                : uri.Host;
            return true;
        }

        var separator = address.LastIndexOf(':');
        if (separator < 0 || !int.TryParse(address[(separator + 1)..], out port) || port is < 1 or > 65535)
            return false;

        host = "127.0.0.1";
        return true;
    }
}
