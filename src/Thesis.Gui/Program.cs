using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Thesis.Application;

namespace Thesis.Gui;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
#if THESIS_UI_SMOKE
        SmokeScenario.Configure(args);
#endif
        if (args is ["internal-host-finalize", "--request", var request, "--report", var report])
        {
            OfficeWorkerHost.Execute(request, report);
            return;
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(args is ["--project", var projectPath] ? Path.GetFullPath(projectPath) : null);
        });
    }
}
