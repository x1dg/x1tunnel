using System.Text.RegularExpressions;

namespace X1Beer.Tunnel;

internal static partial class UrlMatchers
{
    public static string? CloudflareQuick(string line)
    {
        var match = CloudflareQuickRegex().Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? LocalTunnel(string line)
    {
        var match = LocalTunnelRegex().Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? Ngrok(string line)
    {
        var logfmt = NgrokLogfmtRegex().Match(line);
        if (logfmt.Success)
            return logfmt.Groups[1].Value.TrimEnd(',');

        var json = NgrokJsonRegex().Match(line);
        return json.Success ? json.Groups[1].Value : null;
    }

    public static string? CloudflareNamed(string line, string publicUrl) =>
        line.Contains("Registered tunnel connection", StringComparison.OrdinalIgnoreCase)
            ? publicUrl
            : null;

    [GeneratedRegex(@"(https://[a-z0-9-]+\.trycloudflare\.com)", RegexOptions.IgnoreCase)]
    private static partial Regex CloudflareQuickRegex();

    [GeneratedRegex(@"your url is:\s*(https://\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex LocalTunnelRegex();

    [GeneratedRegex(@"url=(https://[^\s,""]+)", RegexOptions.IgnoreCase)]
    private static partial Regex NgrokLogfmtRegex();

    [GeneratedRegex(@"""url""\s*:\s*""(https://[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex NgrokJsonRegex();
}
