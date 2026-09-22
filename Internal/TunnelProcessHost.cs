using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace x1beer.Tunnel;

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
        var stdoutEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handoff = 0;
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        void OnLine(string? line, TaskCompletionSource ended)
        {
            if (line is null)
            {
                ended.TrySetResult();
                return;
            }

            if (line.Length == 0)
                return;

            lock (output)
                output.AppendLine(line);

            logger.LogDebug("[{Provider}] {Line}", provider, line);
            var url = matchUrl(line);
            if (url is not null)
                urlSource.TrySetResult(url);
        }

        process.OutputDataReceived += (_, eventArgs) => OnLine(eventArgs.Data, stdoutEnded);
        process.ErrorDataReceived += (_, eventArgs) => OnLine(eventArgs.Data, stderrEnded);
        process.Exited += (_, _) =>
        {
            _ = FailIfUrlMissingAfterDrainAsync();
        };

        async Task FailIfUrlMissingAfterDrainAsync()
        {
            try
            {
                await Task.WhenAll(stdoutEnded.Task, stderrEnded.Task)
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
            }

            if (Volatile.Read(ref handoff) == 1)
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
            stdoutEnded.TrySetResult();
            stderrEnded.TrySetResult();
            await KillAsync(process, logger, provider).ConfigureAwait(false);
            throw new TunnelException(provider, "Failed to read tunnel process output.", Tail(output), ex);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var url = await urlSource.Task.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
            Volatile.Write(ref handoff, 1);
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
        catch (Exception ex)
        {
            await KillAsync(process, logger, provider).ConfigureAwait(false);
            if (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new TunnelException(
                    provider,
                    $"Timed out after {timeout.TotalSeconds:0}s waiting for a public URL.",
                    Tail(output));
            }

            throw;
        }
    }

    private static async Task KillAsync(Process process, ILogger logger, string provider)
    {
        TryKill(process, logger, provider);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to wait for {Provider} process exit", provider);
            TryKill(process, logger, provider);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void TryKill(Process process, ILogger logger, string provider)
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
}
