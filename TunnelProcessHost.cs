using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace X1Beer.Tunnel;

internal static class TunnelProcessHost
{
    public static async Task<ITunnel> RunAsync(
        ProcessStartInfo startInfo,
        string provider,
        string localOrigin,
        Func<string, string?> matchUrl,
        TimeSpan timeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var urlSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handoff = new Handoff();
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        void OnLine(string? line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            lock (output)
                output.AppendLine(line);

            logger.LogDebug("[{Provider}] {Line}", provider, line);
            var url = matchUrl(line);
            if (url is not null)
                urlSource.TrySetResult(url);
        }

        process.OutputDataReceived += (_, eventArgs) => OnLine(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => OnLine(eventArgs.Data);
        process.Exited += (_, _) =>
        {
            _ = ObserveExitAsync();
        };

        async Task ObserveExitAsync()
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (Volatile.Read(ref handoff.Value) == 1)
                return;

            urlSource.TrySetException(new TunnelException(
                provider,
                $"Process exited with code {SafeExitCode(process)} before a public URL was available.",
                Tail(output)));
        }

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new TunnelException(provider, $"Failed to start '{startInfo.FileName}'.");
            }
        }
        catch (Exception ex) when (ex is not TunnelException)
        {
            process.Dispose();
            throw new TunnelException(provider, $"Failed to start '{startInfo.FileName}': {ex.Message}", innerException: ex);
        }

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            await KillAsync(process, logger, provider).ConfigureAwait(false);
            throw new TunnelException(provider, "Failed to read tunnel process output.", Tail(output), ex);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var url = await urlSource.Task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
            Volatile.Write(ref handoff.Value, 1);
            logger.LogWarning(
                "Public tunnel {Url} forwards internet traffic to {Origin} via {Provider} (pid {Pid}).",
                url,
                localOrigin,
                provider,
                process.Id);

            var session = new ProcessTunnel(process, provider, url, logger);
            process.Exited += (_, _) => session.MarkDisconnected();
            if (process.HasExited)
                session.MarkDisconnected();

            return session;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await KillAsync(process, logger, provider).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            await KillAsync(process, logger, provider).ConfigureAwait(false);
            throw new TunnelException(
                provider,
                $"Timed out after {timeout.TotalSeconds:0}s waiting for a public URL.",
                Tail(output));
        }
        catch
        {
            await KillAsync(process, logger, provider).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task KillAsync(Process process, ILogger logger, string provider)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to kill {Provider} process during startup failure", provider);
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to wait for {Provider} process exit", provider);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static string Tail(StringBuilder output)
    {
        lock (output)
        {
            var text = output.ToString();
            const int limit = 4000;
            return text.Length <= limit ? text : text[^limit..];
        }
    }

    private sealed class Handoff
    {
        public int Value;
    }
}
