using System.Diagnostics;

namespace Thesis.Host;

/// <summary>Runs an explicit executable without shell interpolation, with a bounded lifetime.</summary>
public static class ExternalProcessRunner
{
    public static void Run(string executable, IEnumerable<string> arguments, string workingDirectory, int timeoutSeconds)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false,
            RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException($"Process did not start: {executable}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new IOException($"Process timed out after {timeoutSeconds}s: {Path.GetFileName(executable)}");
        }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0)
            throw new IOException($"{Path.GetFileName(executable)} exited {process.ExitCode}: {Limit(stderr.Result + stdout.Result)}");
    }
    private static string Limit(string text) => text.Length <= 4000 ? text : text[..4000];
}
