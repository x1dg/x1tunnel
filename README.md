# x1tunnel

Class library (`net10.0`) that starts a public HTTPS tunnel to a local port.

Priority: **cloudflared** → fallback **localtunnel** (`npx`).

## Usage

```csharp
using X1Beer.Tunnel;

var url = await LocalTunnelProcessStarter.StartAsync(5201);
// url: https://....trycloudflare.com or https://....loca.lt

LocalTunnelProcessStarter.StopExistingTunnel();
```

## ProjectReference

```xml
<ProjectReference Include="..\x1beer\x1tunnel.csproj" />
```

## Requirements

- .NET 10 SDK
- Optional: `cloudflared` on PATH
- Fallback: Node.js / `npx` (Windows uses `localtunnel-start.cmd` from build output)
