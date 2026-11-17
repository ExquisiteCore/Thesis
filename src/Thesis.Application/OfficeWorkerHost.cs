using System.Runtime.ExceptionServices;
using Thesis.Core;
using Thesis.Host;
using Thesis.Schema;

namespace Thesis.Application;

/// <summary>Shared headless entry point. Both executables can host the isolated COM worker.</summary>
public static class OfficeWorkerHost
{
    public static HostApplicationReport Execute(string requestPath, string reportPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Office COM requires Windows.");
        var request = ThesisJson.DeserializeStrict<OfficeWorkerRequest>(File.ReadAllText(requestPath));
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
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        using var transaction = new FileOutputTransaction();
        File.WriteAllText(transaction.Stage(reportPath), ThesisJson.Serialize(report));
        transaction.Commit();
        return report!;
    }
}
