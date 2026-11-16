using Thesis.Application;
using Thesis.Core;
using Thesis.Host;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class OfficeWorkerCommand
{
    public static CliResult Execute(string[] args)
    {
        if (!OperatingSystem.IsWindows()) throw new ArgumentException("Office COM requires Windows.");
        var options = new CommandOptions(args, "--request", "--report");
        var request = ThesisJson.DeserializeStrict<OfficeWorkerRequest>(File.ReadAllText(options.Path("--request")));
        HostApplicationReport? report = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var lease = new Mutex(false, @"Local\Thesis.Office.Automation");
                var entered = false;
                try
                {
                    try { entered = lease.WaitOne(TimeSpan.FromMinutes(2)); }
                    catch (AbandonedMutexException) { entered = true; }
                    if (!entered) throw new IOException("Office automation is busy; retry after the current job finishes.");
                    report = new WpsComAutomationHost().FinalizeDocument(request.Document, request.Options);
                }
                finally { if (entered) lease.ReleaseMutex(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        using var transaction = new FileOutputTransaction();
        File.WriteAllText(transaction.Stage(options.Path("--report")), ThesisJson.Serialize(report));
        transaction.Commit();
        return new CliResult { Status = "success", HostApplication = report };
    }
}
