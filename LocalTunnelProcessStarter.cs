using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace X1Beer.Tunnel;

public static class LocalTunnelProcessStarter
{
    private static readonly Regex TunnelUrlRegex = new(
        @"your url is:\s*(https://[\w-]+\.loca\.lt)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CloudflareTunnelUrlRegex = new(
        @"(https://[\w-]+\.trycloudflare\.com)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Process? _tunnelProcess;
    private static string? _activeTunnelUrl;

    public static bool IsRunning
    {
        get
        {
            try
            {
                return _tunnelProcess is { HasExited: false };
            }
            catch
            {
                return false;
            }
        }
    }

    public static string? ActiveTunnelUrl => IsRunning ? _activeTunnelUrl : null;

    public static async Task<string?> StartAsync(int port, TimeSpan? timeout = null)
    {
        StopExistingTunnel();

        var cloudflareUrl = await TryStartCloudflaredAsync(port, timeout);
        if (!string.IsNullOrEmpty(cloudflareUrl))
            return cloudflareUrl;

        return await StartLocalTunnelAsync(port, timeout);
    }

    private static async Task<string?> TryStartCloudflaredAsync(int port, TimeSpan? timeout)
    {
        if (IsCommandAvailable("cloudflared"))
            return await StartCloudflaredProcessAsync("cloudflared", port, timeout);

        return null;
    }

    private static async Task<string?> StartCloudflaredProcessAsync(
        string fileName,
        int port,
        TimeSpan? timeout,
        string? extraArgs = null)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new StringBuilder();
        var arguments = string.IsNullOrEmpty(extraArgs)
            ? $"tunnel --url http://127.0.0.1:{port}"
            : $"{extraArgs} tunnel --url http://127.0.0.1:{port}";
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };
        _tunnelProcess = process;

        void HandleLine(string? line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            output.AppendLine(line);
            Console.WriteLine($"[tunnel] {line}");

            var match = CloudflareTunnelUrlRegex.Match(line);
            if (match.Success)
                tcs.TrySetResult(match.Groups[1].Value);
        }

        process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data);
        process.Exited += (_, _) =>
        {
            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(null);
        };

        try
        {
            if (!process.Start())
                return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[tunnel] failed to start {fileName}: {ex.Message}");
            StopExistingTunnel();
            return null;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var waitTimeout = timeout ?? TimeSpan.FromSeconds(45);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(waitTimeout));
        if (completed != tcs.Task)
        {
            Console.Error.WriteLine($"[tunnel] cloudflared timeout. Output:\n{output}");
            StopExistingTunnel();
            return null;
        }

        var tunnelUrl = await tcs.Task;
        if (!string.IsNullOrEmpty(tunnelUrl))
        {
            _activeTunnelUrl = tunnelUrl;
            Console.WriteLine($"[tunnel] cloudflared running (pid {_tunnelProcess.Id})");
        }

        return tunnelUrl;
    }

    private static async Task<string?> StartLocalTunnelAsync(int port, TimeSpan? timeout)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new StringBuilder();
        var psi = CreateStartInfo(port);

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };
        _tunnelProcess = process;

        void HandleLine(string? line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            output.AppendLine(line);
            Console.WriteLine($"[tunnel] {line}");

            var match = TunnelUrlRegex.Match(line);
            if (match.Success)
                tcs.TrySetResult(match.Groups[1].Value);
        }

        process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data);

        process.Exited += (_, _) =>
        {
            Console.Error.WriteLine($"[tunnel] process exited with code {process.ExitCode}");
            _activeTunnelUrl = null;

            if (!tcs.Task.IsCompleted)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    if (!tcs.Task.IsCompleted)
                    {
                        Console.Error.WriteLine($"[tunnel] no URL before exit. Output:\n{output}");
                        tcs.TrySetResult(null);
                    }
                });
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start localtunnel process");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var waitTimeout = timeout ?? TimeSpan.FromSeconds(45);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(waitTimeout));
        if (completed != tcs.Task)
        {
            Console.Error.WriteLine($"Timeout waiting for tunnel URL. Output:\n{output}");
            StopExistingTunnel();
            return null;
        }

        var tunnelUrl = await tcs.Task;
        if (!string.IsNullOrEmpty(tunnelUrl))
        {
            _activeTunnelUrl = tunnelUrl;
            Console.WriteLine($"[tunnel] running in background (pid {_tunnelProcess.Id})");
        }

        return tunnelUrl;
    }

    public static void StopExistingTunnel()
    {
        _activeTunnelUrl = null;

        var process = _tunnelProcess;
        _tunnelProcess = null;
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static ProcessStartInfo CreateStartInfo(int port)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var scriptPath = Path.Combine(AppContext.BaseDirectory, "localtunnel-start.cmd");
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("localtunnel-start.cmd was not copied to output directory", scriptPath);

            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c \"{scriptPath}\" {port}";
        }
        else
        {
            psi.FileName = "npx";
            psi.Arguments = $"--yes localtunnel --port {port}";
        }

        return psi;
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            return process?.WaitForExit(3000) == true && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
