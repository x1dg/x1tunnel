namespace x1beer.Tunnel;

public sealed class TunnelException : Exception
{
    public TunnelException(string provider, string message, string? output = null, Exception? innerException = null)
        : base(Format(provider, message, output), innerException)
    {
        Provider = provider;
        Output = output;
    }

    public string Provider { get; }

    public string? Output { get; }

    private static string Format(string provider, string message, string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return $"[{provider}] {message}";

        return $"[{provider}] {message}{Environment.NewLine}{output}";
    }
}
