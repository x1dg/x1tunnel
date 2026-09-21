# X1Beer.Tunnel

Библиотека для локальных HTTPS-туннелей: вебхуки, интеграционные тесты, разработка. Один вызов поднимает публичный URL до `127.0.0.1:port` и возвращает сессию, которую нужно остановить.

Провайдеры, по порядку из `TunnelOptions.Providers`:

| Имя | Когда |
| --- | --- |
| `cloudflare-quick` | `cloudflared` в PATH. Случайный `*.trycloudflare.com`. Это значение по умолчанию. |
| `cloudflare-named` | Токен или имя туннеля плюс `RequestedHostname`. Один и тот же хост между перезапусками. |
| `ngrok` | `ngrok` в PATH. Свой домен через `RequestedHostname`. |
| `localtunnel` | `npx`. Случайный `*.loca.lt`, страница-заглушка в браузере. Только если явно включить. |

Если задан `PublicUrl` или переменная `X1TUNNEL_PUBLIC_URL`, процесс не запускается.

## Подключение

```xml
<ProjectReference Include="..\x1beer\x1tunnel.csproj" />
```

Пакет ASP.NET Core:

```xml
<ProjectReference Include="..\x1beer\src\X1Beer.Tunnel.AspNetCore\X1Beer.Tunnel.AspNetCore.csproj" />
```

Нужен .NET 8 или .NET 10. Для quick tunnel — `cloudflared` в PATH.

## Код

```csharp
await using var tunnel = await new TunnelFactory().StartAsync(new TunnelOptions
{
    LocalPort = 5101,
});

var webhook = tunnel.PublicUrl + "/api/webhooks/provider";
```

Несколько туннелей в одном процессе не мешают друг другу: у каждого свой `ITunnel`.

Стабильный хост для вебхука:

```csharp
await using var tunnel = await new TunnelFactory().StartAsync(new TunnelOptions
{
    LocalPort = 5101,
    RequestedHostname = "hooks.example.com",
    CloudflareTunnelToken = token,
    Providers = [TunnelProviderNames.CloudflareNamed],
});
```

`localtunnel` не входит в список по умолчанию. Старый запасной вариант:

```csharp
Providers =
[
    TunnelProviderNames.CloudflareQuick,
    TunnelProviderNames.LocalTunnel,
]
```

Свой провайдер — реализация `ITunnelProvider`, переданная в `TunnelFactory`.

## ASP.NET Core

Только Development, после того как Kestrel уже слушает порт. Вне Development туннель не стартует, пока не включён `AllowInNonDevelopment`.

```csharp
builder.Services.AddLocalTunnel(options =>
{
    options.LocalPort = 5101;
});
```

`appsettings.Development.json`:

```json
{
  "Tunnel": {
    "LocalPort": 5101,
    "RequestedHostname": "hooks.example.com",
    "Providers": [ "cloudflare-named", "cloudflare-quick" ]
  }
}
```

`LocalPort: 0` — взять порт из адреса Kestrel. Текущий URL: `ITunnelInfo.PublicUrl`.

В CI задайте `X1TUNNEL_PUBLIC_URL` и не ставьте `cloudflared` на агент.

Токен Cloudflare не пишите в репозиторий. `Tunnel__CloudflareTunnelToken` или user secrets.

## Поведение

- `StartAsync` ждёт, пока локальный порт примет TCP (`WaitForLocalListener`), затем ждёт публичный URL.
- Ошибка — `TunnelException` с именем провайдера и хвостом stdout. Отмена — `OperationCanceledException`.
- Остановка — `await using` или `StopAsync`. Процесс гасится вместе с деревом дочерних процессов.
- Логи — `ILogger`, не консоль.
- Туннель публикует локальный порт в интернет. Библиотека пишет warning с URL.

## Устаревший фасад

`LocalTunnelProcessStarter` оставлен для старого вызова `StartAsync(port)` и по-прежнему пробует cloudflared, затем localtunnel. Новый код берёт `TunnelFactory`.
