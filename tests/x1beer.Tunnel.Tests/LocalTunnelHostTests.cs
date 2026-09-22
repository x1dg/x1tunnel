using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using x1beer.Tunnel;
using x1beer.Tunnel.Hosting;
using Xunit;

namespace x1beer.Tunnel.Tests;

public class LocalTunnelHostTests
{
    [Fact]
    public void AddLocalTunnel_registers_one_hosted_service()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLocalTunnel(options => options.Enabled = false);
        builder.Services.AddLocalTunnel(options => options.Enabled = false);

        var registrations = builder.Services.Count(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(LocalTunnelHostedService));

        Assert.Equal(1, registrations);
    }

    [Fact]
    public async Task Stop_clears_the_published_url()
    {
        var builder = CreateBuilder();
        builder.Services.AddLocalTunnel(options =>
        {
            options.PublicUrl = "https://hooks.example";
            options.WaitForLocalListener = false;
            options.LocalPort = 9;
        });

        await using var app = builder.Build();
        await app.StartAsync();
        var info = app.Services.GetRequiredService<ITunnelInfo>();
        var published = await WaitForUrlAsync(info);
        Assert.Equal("https://hooks.example", published);

        await app.StopAsync();
        Assert.Null(info.PublicUrl);
        Assert.False(info.IsConnected);
    }

    [Fact]
    public async Task Start_failure_does_not_stop_the_host_by_default()
    {
        var builder = CreateBuilder();
        builder.Services.AddSingleton<ITunnelFactory, ThrowingTunnelFactory>();
        builder.Services.AddLocalTunnel(options =>
        {
            options.LocalPort = 9;
            options.WaitForLocalListener = false;
            options.ThrowOnStartFailure = false;
        });

        await using var app = builder.Build();
        await app.StartAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        Assert.False(app.Lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.Null(app.Services.GetRequiredService<ITunnelInfo>().PublicUrl);
        await app.StopAsync();
    }

    [Fact]
    public async Task Start_failure_stops_the_host_when_requested()
    {
        var builder = CreateBuilder();
        builder.Services.AddSingleton<ITunnelFactory, ThrowingTunnelFactory>();
        builder.Services.AddLocalTunnel(options =>
        {
            options.LocalPort = 9;
            options.WaitForLocalListener = false;
            options.ThrowOnStartFailure = true;
        });

        await using var app = builder.Build();
        await app.StartAsync();

        var stopping = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (!app.Lifetime.ApplicationStopping.IsCancellationRequested && DateTimeOffset.UtcNow < stopping)
            await Task.Delay(TimeSpan.FromMilliseconds(50));

        Assert.True(app.Lifetime.ApplicationStopping.IsCancellationRequested);
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseEnvironment(Environments.Development);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    private static async Task<string?> WaitForUrlAsync(ITunnelInfo info)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (info.PublicUrl is null && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(50));

        return info.PublicUrl;
    }

    private sealed class ThrowingTunnelFactory : ITunnelFactory
    {
        public Task<ITunnel> StartAsync(TunnelOptions options, CancellationToken cancellationToken) =>
            throw new TunnelException("test", "nope");
    }
}
