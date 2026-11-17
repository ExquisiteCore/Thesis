using Microsoft.UI.Xaml;

namespace Thesis.Gui;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? window;
    private readonly string? projectPath;

    public App(string? projectPath = null)
    {
        this.projectPath = projectPath;
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thesis", "logs");
            try { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "last-error.txt"), args.Exception.ToString()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow(projectPath);
#if THESIS_UI_SMOKE
        if (SmokeScenario.Directory is not null) ((MainWindow)window).StartSmokeScenario();
#endif
        window.Activate();
    }
}
