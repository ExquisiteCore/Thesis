using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Thesis.Gui.ViewModels;
using Thesis.Schema;
using Thesis.Application;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Thesis.Gui;

public sealed partial class MainWindow : Window
{
#if THESIS_UI_SMOKE
    private readonly WorkspaceViewModel viewModel = new(SmokeScenario.Directory);
#else
    private readonly WorkspaceViewModel viewModel = new();
#endif
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer saveTimer;
    private bool interactionInProgress;
    private bool allowClose;
    private bool closing;

    public MainWindow(string? projectPath = null)
    {
        InitializeComponent();
        Root.DataContext = viewModel;
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported()) SystemBackdrop = new MicaBackdrop();
        saveTimer = DispatcherQueue.CreateTimer();
        saveTimer.Interval = TimeSpan.FromSeconds(1);
        saveTimer.IsRepeating = false;
        saveTimer.Tick += async (_, _) =>
        {
            try
            {
                await viewModel.SaveAsync();
                if (viewModel.IsDirty && !closing) saveTimer.Start();
            }
            catch (Exception ex) { viewModel.ShowError(ex); }
        };
        viewModel.Edited += (_, _) => { saveTimer.Stop(); saveTimer.Start(); };
        AppWindow.Closing += Window_Closing;
        InitializeWorkbench();
        if (projectPath is not null)
        {
            async Task OpenInitialProject() => await RunAsync(async () => { await viewModel.OpenAsync(projectPath); ShowPage("write"); await OfferRecoveryAsync(); });
            if (Root.IsLoaded) DispatcherQueue.TryEnqueue(async () => await OpenInitialProject());
            else Root.Loaded += async (_, _) => await OpenInitialProject();
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (interactionInProgress || viewModel.IsBusy || closing) return;
        interactionInProgress = true;
        try { await action(); }
        catch (Exception ex) { viewModel.ShowError(ex); }
        finally { interactionInProgress = false; }
    }

    private async void Window_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true;
        if (closing || interactionInProgress) return;
        closing = true;
        saveTimer.Stop();
        try
        {
            if (viewModel.IsBusy)
            {
                await DialogAsync("任务仍在处理", "请等待任务结束，或点击取消任务后再关闭窗口。", null);
                return;
            }
            await viewModel.SaveAsync();
            previewJob?.Cancel();
            allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            viewModel.ShowError(ex);
            await DialogAsync("当前内容尚未保存", "内容仍保留在编辑器中。请使用“另存副本”保留当前修改，再处理项目冲突。", null);
        }
        finally { closing = false; }
    }

    private async Task NewAsync()
    {
        await viewModel.SaveAsync();
        var title = new TextBox { Header = "论文题目", PlaceholderText = "输入题目，之后可以修改" };
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = "项目先使用通用本科论文模板。创建后可在“模板”中导入学校模板或学长的合格论文，再导入 Markdown / Word 正文。", TextWrapping = TextWrapping.Wrap });
        if (await new ContentDialog { XamlRoot = Root.XamlRoot, Title = "创建论文项目", Content = panel, PrimaryButtonText = "选择保存位置", CloseButtonText = "取消" }.ShowAsync() != ContentDialogResult.Primary) return;
        var path = await SaveFileAsync("论文项目", ".thesis.json");
        if (path is not null) { await viewModel.CreateAsync(path); if (!string.IsNullOrWhiteSpace(title.Text)) viewModel.Title = title.Text; await viewModel.SaveAsync(); ShowPage("template"); }
    }

    private async Task OpenAsync()
    {
        await viewModel.SaveAsync();
        var path = await OpenFileAsync("打开论文项目", ".json");
        if (path is not null) { await viewModel.OpenAsync(path); ShowPage("write"); await OfferRecoveryAsync(); }
    }

    private Task SaveAsync() => viewModel.SaveAsync();
    private async void New_Click(object sender, RoutedEventArgs e) => await RunAsync(NewAsync);
    private async void Open_Click(object sender, RoutedEventArgs e) => await RunAsync(OpenAsync);
    private async void Save_Click(object sender, RoutedEventArgs e) => await RunAsync(SaveAsync);
    private async void SaveCopy_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var path = await SaveFileAsync("论文项目副本", ".thesis.json");
        if (path is not null) await viewModel.SaveCopyAsync(path);
    });

    private async void Import_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var path = await OpenFileAsync("导入正文", ".docx", ".json", ".md", ".markdown");
        if (path is null) return;
        if (await DialogAsync("导入并替换正文", "将替换当前项目的正文和大纲，保留模板设置。本次导入可以通过大纲下方的撤销按钮恢复。", "导入") == ContentDialogResult.Primary)
            await viewModel.ImportAsync(path);
    });

    private void AddChapter_Click(object sender, RoutedEventArgs e) => viewModel.Add(false);
    private void AddSection_Click(object sender, RoutedEventArgs e) => viewModel.Add(true);
    private void MoveUp_Click(object sender, RoutedEventArgs e) => viewModel.Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => viewModel.Move(1);
    private async void UndoStructure_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (await DialogAsync("恢复到上次大纲操作前", "将恢复新增、移动、删除或导入操作之前的正文快照，也会还原此后对正文的编辑。", "恢复") == ContentDialogResult.Primary)
            viewModel.UndoStructure();
    });
    private async void Delete_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (await DialogAsync("删除当前章节", "该章节及所属小节将从论文中移除。可以通过大纲下方的撤销按钮恢复。", "删除") == ContentDialogResult.Primary)
            viewModel.DeleteSelected();
    });
    private void Heading_LostFocus(object sender, RoutedEventArgs e) => viewModel.RefreshLabels();
    private void Cancel_Click(object sender, RoutedEventArgs e) => viewModel.Cancel();

    private void Template_Click(object sender, RoutedEventArgs e) => ShowPage("template");
    private async void Plan_Click(object sender, RoutedEventArgs e) => await RunAsync(() => PickProjectFileAsync("plan", "选择已确认的套版方案", ".json"));
    private async void Render_Click(object sender, RoutedEventArgs e) => await RunAsync(() => PickProjectFileAsync("render", "选择真实分页配置", ".json"));
    private async void Rules_Click(object sender, RoutedEventArgs e) => await RunAsync(() => PickProjectFileAsync("rules", "载入学校要求", ".json"));
    private async void Compose_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ComposeAsync(false));
    private async void Preview_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ComposeAsync(true));

    private async Task ComposeAsync(bool render)
    {
        var path = await SaveFileAsync(render ? "论文候选与 PDF" : "论文候选 DOCX", ".docx");
        if (path is null) return;
        await viewModel.ComposeAsync(path, render);
        if (render && viewModel.PdfPath is not null) await ShowPdfAsync(viewModel.PdfPath);
    }

    private async void ConfigureRender_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var engine = new ComboBox { Header = "排版软件", ItemsSource = new[] { "自动选择", "WPS", "Microsoft Word" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "使用本机 WPS 或 Word 更新目录、分页并导出 PDF。检查程序已经内置，无需配置 Python。日常快速预览不启动 Office。", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(engine);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "真实分页设置", Content = panel, PrimaryButtonText = "保存设置", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await viewModel.ConfigureRenderAsync(new LayoutRenderConfig { Engine = new[] { "auto", "wps", "word" }[engine.SelectedIndex] });
    });

    private async void OpenDocument_Click(object sender, RoutedEventArgs e) => await RunAsync(() => OpenPathAsync(viewModel.DocumentPath));
    private async void OpenPdf_Click(object sender, RoutedEventArgs e) => await RunAsync(() => OpenPathAsync(viewModel.PdfPath));

    private async Task PickProjectFileAsync(string kind, string title, params string[] extensions)
    {
        var path = await OpenFileAsync(title, extensions);
        if (path is not null) viewModel.SetFile(kind, path);
    }

    private async Task<string?> OpenFileAsync(string title, params string[] extensions)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, ViewMode = PickerViewMode.List, CommitButtonText = title };
        foreach (var extension in extensions.Distinct(StringComparer.OrdinalIgnoreCase)) picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFileAsync())?.Path;
    }

    private async Task<string?> SaveFileAsync(string title, string extension)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "我的论文", DefaultFileExtension = extension };
        picker.FileTypeChoices.Add(title, [extension]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return (await picker.PickSaveFileAsync())?.Path;
    }

    private async Task OpenPathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            await DialogAsync("请先生成成品", "选择模板后生成 DOCX，或配置排版软件后生成真实分页预览。", null);
            return;
        }
        if (!await Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(path)))
            throw new IOException("系统尚未设置此类文件的默认打开程序。文件已保存到输出位置。");
    }

    private Task<ContentDialogResult> DialogAsync(string title, string message, string? primary) =>
        new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, Content = message, PrimaryButtonText = primary ?? "", CloseButtonText = primary is null ? "知道了" : "取消", DefaultButton = ContentDialogButton.Close }.ShowAsync().AsTask();
}
