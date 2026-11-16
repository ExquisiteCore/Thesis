using Thesis.Schema;

namespace Thesis.Cli;

internal static class CliCommandSupport
{
    internal static string RequiredOption(string[] args, string name)
    {
        return OptionalOption(args, name)
            ?? throw new CliException("missing_option", $"Missing required option: {name}");
    }

    internal static string? OptionalOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    internal static List<string> Options(string[] args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                values.Add(args[i + 1]);
            }
        }

        return values;
    }

    internal static bool HasFlag(string[] args, string name)
    {
        return args.Any(arg => string.Equals(arg, name, StringComparison.Ordinal));
    }

    internal static CliResult Error(string code, string message)
    {
        return new CliResult
        {
            Status = "error",
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "error",
                    Code = code,
                    Message = message
                }
            ]
        };
    }

    internal static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsPathInsideDirectory(string directory, string path)
    {
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

}
