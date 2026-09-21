using System.Diagnostics;
using System.Text;

namespace X1Beer.Tunnel;

internal static class CommandLocator
{
    public static bool Exists(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where.exe" : "which",
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

    public static ProcessStartInfo ForCommand(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (OperatingSystem.IsWindows() && fileName is "npx" or "npm")
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(fileName + " " + string.Join(' ', arguments.Select(Quote)));
            return startInfo;
        }

        startInfo.FileName = fileName;
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    public static IReadOnlyList<string> WithExtra(IReadOnlyList<string> arguments, string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra))
            return arguments;

        var combined = new List<string>(arguments);
        combined.AddRange(extra.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return combined;
    }

    private static string Quote(string argument) =>
        argument.Contains(' ') ? $"\"{argument}\"" : argument;
}
