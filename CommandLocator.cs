using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace X1Beer.Tunnel;

internal static class CommandLocator
{
    private static readonly ConcurrentDictionary<string, string?> Resolved = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static bool Exists(string command) => Find(command) is not null;

    public static string? Find(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        return Resolved.GetOrAdd(command, Resolve);
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
            var resolved = Find(fileName) ?? fileName;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/d /s /c \"" + JoinCmd(resolved, arguments) + "\"";
            return startInfo;
        }

        startInfo.FileName = Find(fileName) ?? fileName;
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    public static IReadOnlyList<string> WithExtra(IReadOnlyList<string> arguments, string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra))
            return arguments;

        var combined = new List<string>(arguments);
        combined.AddRange(SplitArguments(extra));
        return combined;
    }

    internal static IReadOnlyList<string> SplitArguments(string extra)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var ch in extra)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(ch))
            {
                if (current.Length == 0)
                    continue;

                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (quoted)
            throw new ArgumentException("ExtraArguments has an unterminated quote.", nameof(extra));

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    private static string? Resolve(string command)
    {
        if (Path.IsPathRooted(command))
            return File.Exists(command) ? command : null;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        var extensions = new List<string> { string.Empty };
        if (OperatingSystem.IsWindows())
        {
            var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
            extensions.AddRange(pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, command + extension);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string JoinCmd(string fileName, IReadOnlyList<string> arguments)
    {
        var parts = new List<string>(arguments.Count + 1) { QuoteCmd(fileName) };
        foreach (var argument in arguments)
            parts.Add(QuoteCmd(argument));

        return string.Join(' ', parts);
    }

    private static string QuoteCmd(string value)
    {
        if (value.IndexOfAny(['\r', '\n', '%', '!']) >= 0)
            throw new ArgumentException("Tunnel arguments cannot contain line breaks, '%', or '!'.");

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
