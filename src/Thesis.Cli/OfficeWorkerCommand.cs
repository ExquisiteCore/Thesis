using Thesis.Application;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class OfficeWorkerCommand
{
    public static CliResult Execute(string[] args)
    {
        if (!OperatingSystem.IsWindows()) throw new ArgumentException("Office COM requires Windows.");
        var options = new CommandOptions(args, "--request", "--report");
        var report = OfficeWorkerHost.Execute(options.Path("--request"), options.Path("--report"));
        return new CliResult { Status = "success", HostApplication = report };
    }
}
