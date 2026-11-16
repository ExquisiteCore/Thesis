using Thesis.Host;
using Thesis.Schema;

namespace Thesis.Application;

public sealed class OfficeWorkerRequest
{
    public string Document { get; set; } = "";
    public HostApplicationOptions Options { get; set; } = new();
}

/// <summary>Bound COM calls to a disposable worker; a blocked automation call never holds the caller indefinitely.</summary>
internal static class OfficeProcessRunner
{
    public static HostApplicationReport Run(string document, HostApplicationOptions options, int timeoutSeconds)
    {
        var directory = Path.Combine(Path.GetTempPath(), "thesis-office-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var request = Path.Combine(directory, "request.json");
            var report = Path.Combine(directory, "report.json");
            File.WriteAllText(request, ThesisJson.Serialize(new OfficeWorkerRequest { Document = document, Options = options }));
            var executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Thesis.Cli.exe" : "Thesis.Cli");
            var arguments = new List<string>();
            if (!File.Exists(executable))
            {
                executable = "dotnet";
                arguments.Add(Path.Combine(AppContext.BaseDirectory, "Thesis.Cli.dll"));
            }
            arguments.AddRange(["internal-host-finalize", "--request", request, "--report", report]);
            ExternalProcessRunner.Run(executable, arguments, directory, timeoutSeconds);
            return ThesisJson.Deserialize<HostApplicationReport>(File.ReadAllText(report));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
