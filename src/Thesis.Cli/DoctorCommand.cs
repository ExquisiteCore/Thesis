using Thesis.Application;
using Thesis.Host;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class DoctorCommand
{
    public static CliResult Execute(string[] args)
    {
        var options = new CommandOptions(args, "--render-config");
        var config = RenderConfiguration.Load(options.Path("--render-config"));
        if (!string.IsNullOrWhiteSpace(config.PythonPath))
        {
            ExternalProcessRunner.Run(config.PythonPath, ["-c", "import pymupdf; print(pymupdf.__version__)"], AppContext.BaseDirectory, config.TimeoutSeconds);
            var script = Path.Combine(AppContext.BaseDirectory, "scripts", "audit_layout.py");
            if (!File.Exists(script)) throw new IOException("PDF audit script is missing from this installation.");
        }
        if (config.Engine == "libreoffice") ExternalProcessRunner.Run(config.LibreOfficePath, ["--version"], AppContext.BaseDirectory, config.TimeoutSeconds);
        else if (OperatingSystem.IsWindows())
        {
            var candidates = config.ProgId is not null ? new[] { config.ProgId } : config.Engine switch
            { "wps" => ["KWPS.Application", "WPS.Application"], "word" => ["Word.Application"], _ => new[] { "KWPS.Application", "Word.Application" } };
            var registered = false;
            foreach (var id in candidates) registered |= Type.GetTypeFromProgID(id) is not null;
            if (!registered) throw new ArgumentException("No requested Office COM host is registered.");
        }
        return new CliResult { Status = "success", Diagnostics = [new Diagnostic { Severity = "info", Code = "environment_ready",
            Message = $"Engine={config.Engine}; PDF audit={(string.IsNullOrWhiteSpace(config.PythonPath) ? "managed .NET" : "Python/PyMuPDF")}. Template fonts still need verification on the rendering host." }] };
    }
}
