using Xunit;

namespace x1beer.Tunnel.Tests;

public class TunnelFactoryTests
{
    [Fact]
    public async Task PublicUrl_skips_providers()
    {
        var provider = new FakeProvider("cloudflare-quick");
        var factory = new TunnelFactory([provider]);

        await using var tunnel = await factory.StartAsync(new TunnelOptions
        {
            LocalPort = 5201,
            PublicUrl = "https://hooks.example",
            WaitForLocalListener = false,
        });

        Assert.Equal("https://hooks.example", tunnel.PublicUrl);
        Assert.Equal("external", tunnel.Provider);
        Assert.Equal(0, provider.Starts);
    }

    [Fact]
    public async Task Environment_url_is_used_when_options_url_is_empty()
    {
        var previous = Environment.GetEnvironmentVariable(TunnelEnvironment.PublicUrl);
        Environment.SetEnvironmentVariable(TunnelEnvironment.PublicUrl, "https://from-env.example");
        try
        {
            var provider = new FakeProvider(TunnelProviderNames.CloudflareQuick) { Available = true };
            var factory = new TunnelFactory([provider]);
            await using var tunnel = await factory.StartAsync(new TunnelOptions
            {
                LocalPort = 5201,
                WaitForLocalListener = false,
            });

            Assert.Equal("https://from-env.example", tunnel.PublicUrl);
            Assert.Equal(0, provider.Starts);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TunnelEnvironment.PublicUrl, previous);
        }
    }

    [Fact]
    public async Task Falls_through_unavailable_provider()
    {
        var skipped = new FakeProvider("first") { Available = false };
        var used = new FakeProvider("second");
        var factory = new TunnelFactory([skipped, used]);

        await using var tunnel = await factory.StartAsync(new TunnelOptions
        {
            LocalPort = 5201,
            WaitForLocalListener = false,
            Providers = ["first", "second"],
        });

        Assert.Equal("https://second.example", tunnel.PublicUrl);
        Assert.Equal(0, skipped.Starts);
        Assert.Equal(1, used.Starts);
    }

    [Fact]
    public async Task Failed_provider_does_not_block_the_next_one()
    {
        var failing = new FakeProvider("first") { Fail = true };
        var used = new FakeProvider("second");
        var factory = new TunnelFactory([failing, used]);

        await using var tunnel = await factory.StartAsync(new TunnelOptions
        {
            LocalPort = 5201,
            WaitForLocalListener = false,
            Providers = ["first", "second"],
        });

        Assert.Equal("https://second.example", tunnel.PublicUrl);
        Assert.Equal(1, failing.Starts);
    }

    [Fact]
    public async Task Throws_when_every_provider_fails()
    {
        var factory = new TunnelFactory(
        [
            new FakeProvider("first") { Fail = true },
            new FakeProvider("second") { Available = false },
        ]);

        var exception = await Assert.ThrowsAsync<TunnelException>(() => factory.StartAsync(new TunnelOptions
        {
            LocalPort = 5201,
            WaitForLocalListener = false,
            Providers = ["first", "second"],
        }));

        Assert.Contains("first", exception.Message, StringComparison.Ordinal);
        Assert.Contains("second", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_sessions_are_independent()
    {
        var factory = new TunnelFactory([new FakeProvider("a"), new FakeProvider("b")]);
        var firstTask = factory.StartAsync(new TunnelOptions
        {
            LocalPort = 5201,
            WaitForLocalListener = false,
            Providers = ["a"],
        });
        var secondTask = factory.StartAsync(new TunnelOptions
        {
            LocalPort = 5202,
            WaitForLocalListener = false,
            Providers = ["b"],
        });

        var tunnels = await Task.WhenAll(firstTask, secondTask);
        await using var first = tunnels[0];
        await using var second = tunnels[1];

        Assert.Equal("https://a.example", first.PublicUrl);
        Assert.Equal("https://b.example", second.PublicUrl);
        Assert.True(first.IsConnected);
        await first.DisposeAsync();
        Assert.False(first.IsConnected);
        Assert.True(second.IsConnected);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var factory = new TunnelFactory([new FakeProvider("slow") { Block = true }]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.StartAsync(new TunnelOptions
        {
            LocalPort = 5201,
            WaitForLocalListener = false,
            Providers = ["slow"],
        }, cancellation.Token));
    }

    [Fact]
    public async Task Does_not_start_a_provider_until_the_local_port_accepts_connections()
    {
        var provider = new FakeProvider(TunnelProviderNames.CloudflareQuick);
        var factory = new TunnelFactory([provider]);

        await Assert.ThrowsAsync<TunnelException>(() => factory.StartAsync(new TunnelOptions
        {
            LocalPort = ClosedPort(),
            ListenTimeout = TimeSpan.FromMilliseconds(400),
            Providers = [TunnelProviderNames.CloudflareQuick],
        }));

        Assert.Equal(0, provider.Starts);
    }

    [Fact]
    public void Empty_provider_list_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new TunnelFactory(Array.Empty<ITunnelProvider>()));

    [Fact]
    public async Task Null_provider_list_is_rejected()
    {
        var factory = new TunnelFactory([new FakeProvider("cloudflare-quick")]);
        await Assert.ThrowsAsync<ArgumentException>(() => factory.StartAsync(new TunnelOptions
        {
            LocalPort = 5201,
            WaitForLocalListener = false,
            Providers = null!,
        }));
    }

    [Fact]
    public async Task Public_url_must_be_absolute_http()
    {
        var factory = new TunnelFactory([new FakeProvider("cloudflare-quick")]);
        var exception = await Assert.ThrowsAsync<TunnelException>(() => factory.StartAsync(new TunnelOptions
        {
            PublicUrl = "not-a-url",
            WaitForLocalListener = false,
        }));
        Assert.Equal("external", exception.Provider);
    }

    private static int ClosedPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FakeProvider : ITunnelProvider
    {
        public FakeProvider(string name) => Name = name;

        public string Name { get; }

        public bool Available { get; init; } = true;

        public bool Fail { get; init; }

        public bool Block { get; init; }

        public int Starts { get; private set; }

        public bool CanStart(TunnelOptions options) => Available;

        public async Task<ITunnel> StartAsync(TunnelOptions options, Microsoft.Extensions.Logging.ILogger logger, CancellationToken cancellationToken)
        {
            Starts++;
            if (Block)
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);

            if (Fail)
                throw new TunnelException(Name, "boom", "provider log");

            return new ExternalUrlTunnel($"https://{Name}.example", Name);
        }
    }
}
