using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Thesis.Gui;

internal sealed record NewProjectDetails(string Path, string Title, string Author);

public sealed partial class NewProjectDialog : ContentDialog
{
    private readonly Func<string, Task<string?>> choosePath;
    private readonly Func<NewProjectDetails, Task> createProject;
    private readonly string initialDirectory;
    private bool customLocation;
    private string suggestedLocation = "";

    internal NewProjectDialog(string directory, Func<string, Task<string?>> choosePath, Func<NewProjectDetails, Task> createProject)
    {
        this.choosePath = choosePath; this.createProject = createProject; initialDirectory = directory;
        InitializeComponent();
        UpdateLocation();
    }

    private void Title_Changed(object sender, TextChangedEventArgs e)
    {
        if (!customLocation && ProjectLocation is not null) UpdateLocation();
    }

    private void UpdateLocation()
    {
        var name = ProjectFilePicker.FileName(PaperTitle.Text);
        suggestedLocation = Path.Combine(initialDirectory, name, name + ".thesis.json");
        ProjectLocation.Text = suggestedLocation;
    }

    private void Location_Changed(object sender, TextChangedEventArgs e)
    {
        // TextChanged is delivered asynchronously; a temporary setter flag is insufficient.
        if (ProjectLocation.Text != suggestedLocation) customLocation = true;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        BrowseButton.IsEnabled = false;
        IsPrimaryButtonEnabled = false;
        ValidationMessage.IsOpen = false;
        try
        {
            var path = await choosePath(ProjectFilePicker.FileName(PaperTitle.Text));
            if (path is not null) { customLocation = true; ProjectLocation.Text = path; }
        }
        catch (Exception ex) { ShowError("选择位置失败", ex); }
        finally { BrowseButton.IsEnabled = true; IsPrimaryButtonEnabled = true; }
    }

    private async void Create_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        IsPrimaryButtonEnabled = false; BrowseButton.IsEnabled = false;
        ValidationMessage.IsOpen = false; CreatingProgress.Visibility = Visibility.Visible;
        try
        {
            var path = ProjectLocation.Text.Trim();
            if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请输入完整的项目文件路径，例如 D:\\论文\\我的论文.thesis.json。");
            // The native save picker reserves the selected path with an empty file.
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                throw new IOException("这个位置已有文件，请换一个文件名或打开已有项目。");
            var title = string.IsNullOrWhiteSpace(PaperTitle.Text) ? "未命名论文" : PaperTitle.Text.Trim();
            await createProject(new(path, title, PaperAuthor.Text.Trim()));
        }
        catch (Exception ex) { args.Cancel = true; ShowError("论文尚未创建", ex); }
        finally
        {
            CreatingProgress.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = true; BrowseButton.IsEnabled = true;
            deferral.Complete();
        }
    }

    private void ShowError(string title, Exception error)
    {
        ValidationMessage.Title = title; ValidationMessage.Message = error.Message; ValidationMessage.IsOpen = true;
    }
}
