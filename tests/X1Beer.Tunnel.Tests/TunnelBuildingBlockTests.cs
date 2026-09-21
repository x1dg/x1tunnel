using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace X1Beer.Tunnel.Tests;

public class TunnelBuildingBlockTests
{
    [Fact]
    public async Task Listener_probe_returns_when_the_port_is_open()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await LocalListenerProbe.WaitAsync("127.0.0.1", port, TimeSpan.FromSeconds(2), CancellationToken.None);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Theory]
    [InlineData("https://random-name.trycloudflare.com something", "https://random-name.trycloudflare.com")]
    public void Cloudflare_quick_url_is_parsed(string line, string expected) =>
        Assert.Equal(expected, UrlMatchers.CloudflareQuick(line));

    [Fact]
    public void Localtunnel_url_is_parsed() =>
        Assert.Equal(
            "https://ab-cd.loca.lt",
            UrlMatchers.LocalTunnel("your url is: https://ab-cd.loca.lt"));

    [Fact]
    public void Ngrok_logfmt_url_is_parsed() =>
        Assert.Equal(
            "https://foo.ngrok.app",
            UrlMatchers.Ngrok("msg=\"started tunnel\" url=https://foo.ngrok.app"));

    [Fact]
    public void Ngrok_keeps_the_last_started_tunnel_url() =>
        Assert.Equal(
            "https://second.ngrok.app",
            UrlMatchers.Ngrok("msg=\"started tunnel\" url=https://first.ngrok.app url=https://second.ngrok.app"));

    [Fact]
    public void Named_cloudflare_is_ready_when_the_connection_is_registered() =>
        Assert.Equal(
            "https://hooks.example",
            UrlMatchers.CloudflareNamed("INF Registered tunnel connection connIndex=0", "https://hooks.example"));

    [Fact]
    public void Cloudflare_quick_arguments_target_the_local_origin()
    {
        var arguments = CloudflareQuickTunnelProvider.BuildArguments(new TunnelOptions
        {
            LocalHost = "127.0.0.1",
            LocalPort = 5201,
        });

        Assert.Equal(["tunnel", "--url", "http://127.0.0.1:5201"], arguments);
    }

    [Fact]
    public void Named_cloudflare_token_arguments_do_not_include_the_local_port()
    {
        var arguments = CloudflareNamedTunnelProvider.BuildArguments(new TunnelOptions
        {
            LocalPort = 5201,
            RequestedHostname = "hooks.example",
            CloudflareTunnelToken = "token-value",
        });

        Assert.Equal(["tunnel", "run", "--token", "token-value"], arguments);
    }

    [Fact]
    public void Named_cloudflare_without_hostname_is_not_configured() =>
        Assert.Null(CloudflareNamedTunnelProvider.BuildArguments(new TunnelOptions
        {
            LocalPort = 5201,
            CloudflareTunnelToken = "token-value",
        }));

    [Fact]
    public void Ngrok_reserved_url_is_passed_through()
    {
        var arguments = NgrokTunnelProvider.BuildArguments(new TunnelOptions
        {
            LocalPort = 5101,
            RequestedHostname = "hooks.ngrok.app",
        });

        Assert.Contains("--url", arguments);
        Assert.Contains("https://hooks.ngrok.app", arguments);
    }

    [Fact]
    public void Localtunnel_uses_a_single_label_as_subdomain()
    {
        var arguments = LocalTunnelProvider.BuildArguments(new TunnelOptions
        {
            LocalPort = 5101,
            RequestedHostname = "myapp",
        });

        Assert.Contains("--subdomain", arguments);
        Assert.Contains("myapp", arguments);
    }

    [Fact]
    public void Server_address_keeps_the_port_when_the_host_is_unspecified()
    {
        Assert.True(TunnelEndpoint.TryParse("http://0.0.0.0:5201", out var host, out var port));
        Assert.Equal("127.0.0.1", host);
        Assert.Equal(5201, port);
    }

    [Fact]
    public void Server_address_prefers_the_requested_scheme()
    {
        Assert.True(TunnelEndpoint.TrySelect(
            ["https://127.0.0.1:7000", "http://127.0.0.1:5000"],
            "http",
            out var host,
            out var port,
            out var scheme));
        Assert.Equal("127.0.0.1", host);
        Assert.Equal(5000, port);
        Assert.Equal("http", scheme);
    }

    [Fact]
    public void Ipv6_loopback_origin_is_bracketed() =>
        Assert.Equal("http://[::1]:5201", new TunnelOptions { LocalHost = "::1", LocalPort = 5201 }.LocalOrigin());

    [Fact]
    public void Extra_arguments_keep_quoted_tokens()
    {
        var arguments = CommandLocator.WithExtra(["tunnel"], "--protocol \"http2 extra\"");
        Assert.Equal(["tunnel", "--protocol", "http2 extra"], arguments);
    }

    [Fact]
    public void Windows_npx_arguments_are_quoted_for_cmd()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var startInfo = CommandLocator.ForCommand("npx", ["--subdomain", "a&b"]);
        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.Contains("\"a&b\"", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => CommandLocator.ForCommand("npx", ["a%b"]));
    }

    [Fact]
    public async Task Process_host_reads_a_url_and_stops()
    {
        var startInfo = OperatingSystem.IsWindows()
            ? CommandLocator.ForCommand("cmd.exe", ["/c", "echo your url is: https://abc.loca.lt"])
            : CommandLocator.ForCommand("/bin/sh", ["-c", "echo 'your url is: https://abc.loca.lt'"]);

        await using var tunnel = await TunnelProcessHost.RunAsync(
            startInfo,
            "localtunnel",
            "http://127.0.0.1:9",
            UrlMatchers.LocalTunnel,
            TimeSpan.FromSeconds(10),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal("https://abc.loca.lt", tunnel.PublicUrl);
        Assert.False(tunnel.IsConnected);
    }

    [Fact]
    public async Task Process_host_stays_connected_while_the_process_is_alive()
    {
        var startInfo = OperatingSystem.IsWindows()
            ? CommandLocator.ForCommand("cmd.exe", ["/c", "echo your url is: https://abc.loca.lt & ping -n 30 127.0.0.1"])
            : CommandLocator.ForCommand("/bin/sh", ["-c", "echo 'your url is: https://abc.loca.lt'; sleep 30"]);

        await using var tunnel = await TunnelProcessHost.RunAsync(
            startInfo,
            "localtunnel",
            "http://127.0.0.1:9",
            UrlMatchers.LocalTunnel,
            TimeSpan.FromSeconds(10),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal("https://abc.loca.lt", tunnel.PublicUrl);
        Assert.True(tunnel.IsConnected);
    }

    [Fact]
    public async Task Process_host_times_out_and_kills_the_process()
    {
        var startInfo = OperatingSystem.IsWindows()
            ? CommandLocator.ForCommand("cmd.exe", ["/c", "ping -n 20 127.0.0.1"])
            : CommandLocator.ForCommand("/bin/sh", ["-c", "sleep 20"]);

        var exception = await Assert.ThrowsAsync<TunnelException>(() => TunnelProcessHost.RunAsync(
            startInfo,
            "cloudflare-quick",
            "http://127.0.0.1:9",
            UrlMatchers.CloudflareQuick,
            TimeSpan.FromSeconds(1),
            NullLogger.Instance,
            CancellationToken.None));

        Assert.Contains("Timed out", exception.Message, StringComparison.Ordinal);
    }
}
