using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Thesis.Application;
using Thesis.Schema;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Thesis.Gui;

// Included only in opt-in test builds. Production packages contain no test entry point.
internal static class SmokeScenario
{
    public static string? Directory { get; private set; }
    public static void Configure(string[] args)
    {
        if (args is ["--ui-smoke", var directory]) Directory = Path.GetFullPath(directory);
    }
}

public sealed partial class MainWindow
{
    internal void StartSmokeScenario()
    {
        if (Root.IsLoaded) DispatcherQueue.TryEnqueue(async () => await RunSmokeScenarioAsync());
        else Root.Loaded += async (_, _) => await RunSmokeScenarioAsync();
    }
    private async Task RunSmokeScenarioAsync()
    {
        var directory = SmokeScenario.Directory!;
        System.IO.Directory.CreateDirectory(directory);
        var steps = new List<string>();
        try
        {
            void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message); steps.Add(message);
                File.WriteAllText(Path.Combine(directory, "progress.json"), ThesisJson.Serialize(steps));
            }
            await CaptureSmokeAsync(directory, "home");
            var picker = ProjectFilePicker.Save(this, "论文项目", ".thesis.json", "示例论文");
            Require(picker.DefaultFileExtension == ".json" && picker.SuggestedFileName == "示例论文.thesis", "系统保存选择器接受项目扩展名");
            await VerifyNewProjectDialogAsync(directory, Require);
            viewModel.Title = "论文工作台交互验证";
            viewModel.Author = "示例作者";
            viewModel.Body = "**Markdown 正文**和*斜体*。\n\n| 项目 | 结果 |\n| --- | --- |\n| 预览 | 正常 |\n\n- 第一项\n- 第二项";
            await viewModel.SaveAsync();
            ShowPage("write"); Root.UpdateLayout(); await Task.Delay(100);
            Require(BodyEditor.Text.Contains("Markdown 正文"), "编辑器绑定到当前章节");
            await CaptureSmokeAsync(directory, "writing");
            ShowPage("template"); Root.UpdateLayout();
            Require(RoleList.Items.Count > 0 && editingRole is not null, "模板角色与样本已加载");
            RoleOverride.IsChecked = true; RoleSize.Value = 13; RoleFont.Text = "仿宋";
            await SaveDesignAsync(false);
            Require(viewModel.Design!.Roles.Single(r => r.Role == "body").Size == 13, "模板修改保存并应用");
            await CaptureSmokeAsync(directory, "template");
            ShowPage("preview"); await RefreshQuickPreviewAsync(); Root.UpdateLayout();
            Require(PreviewBlocks.Children.Count > 0 && quickPreview?.Blocks.Any(b => b.Kind == "table") == true, "原生快速预览显示内容与表格");
            await CaptureSmokeAsync(directory, "preview");
            var external = ThesisProjectStore.Open(viewModel.ProjectPath!); external.Document.Content.Chapters[0].Markdown = "CLI 外部更新正文";
            ThesisProjectStore.Save(external); await viewModel.ReloadExternalAsync();
            Require(viewModel.Body == "CLI 外部更新正文", "GUI 载入外部已保存版本");
            await RefreshQuickPreviewAsync();
            Require(quickPreview!.Blocks.Any(b => b.Text.Contains("CLI 外部更新正文")), "外部更新进入快速预览");
            ShowPage("checks"); await viewModel.CheckAsync(); Require(viewModel.Issues.Count > 0, "内容预检显示问题");
            ShowPage("history"); Require(viewModel.Versions.Count > 0, "历史检查点已加载");
            HistoryBefore.Text = viewModel.HistoryText(viewModel.Versions[0].Path); Require(HistoryBefore.Text.Length > 0, "历史正文可查看");
            await viewModel.ExportMarkdownAsync(Path.Combine(directory, "paper.md"));
            await viewModel.ExportPackageAsync(Path.Combine(directory, "paper.zip"));
            Require(File.Exists(Path.Combine(directory, "paper.zip")), "项目包与 Markdown 导出");
            await viewModel.ComposeAsync(Path.Combine(directory, "candidate.docx"), false);
            Require(File.Exists(viewModel.DocumentPath), "GUI 使用公共链路生成 DOCX");
            using (var pdf = new UglyToad.PdfPig.Writer.PdfDocumentBuilder())
            {
                var font = pdf.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
                pdf.AddPage(595, 842).AddText("Native PDF preview", 18, new UglyToad.PdfPig.Core.PdfPoint(70, 700), font);
                File.WriteAllBytes(Path.Combine(directory, "preview.pdf"), pdf.Build());
            }
            await ShowPdfAsync(Path.Combine(directory, "preview.pdf"));
            Require(pdfPreview?.PageCount == 1 && PreviewBlocks.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().Any(), "Windows 原生 PDF 页面显示");
            for (var i = 0; i < 3 && Root.RequestedTheme != ElementTheme.Dark; i++) Theme_Click(this, new RoutedEventArgs());
            Inspector_Click(this, new RoutedEventArgs());
            Require(Root.RequestedTheme == ElementTheme.Dark, "深色主题切换生效");
            ShowPage("write"); await CaptureSmokeAsync(directory, "dark");
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Restore();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 1000));
            await Task.Delay(200);
            await CaptureSmokeAsync(directory, "writing-compact");
            ShowPage("template"); await CaptureSmokeAsync(directory, "template-compact");
            File.WriteAllText(Path.Combine(directory, "result.json"), ThesisJson.Serialize(new { Passed = true, Steps = steps }));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "result.json"), ThesisJson.Serialize(new { Passed = false, Steps = steps, Error = ex.ToString() }));
        }
        finally { previewJob?.Cancel(); allowClose = true; Close(); }
    }

    private async Task VerifyNewProjectDialogAsync(string directory, Action<bool, string> require)
    {
        var failPicker = false;
        var dialog = new NewProjectDialog(directory, _ => failPicker
                ? Task.FromException<string?>(new IOException("位置选择器测试错误"))
                : Task.FromResult<string?>(null),
            details => viewModel.CreateAsync(details.Path, details.Title, details.Author)) { XamlRoot = Root.XamlRoot };
        var pending = dialog.ShowAsync().AsTask();
        await Task.Delay(150);
        var title = (TextBox)dialog.FindName("PaperTitle");
        var location = (TextBox)dialog.FindName("ProjectLocation");
        title.Text = "测试论文"; await Task.Delay(80);
        require(location.Text.EndsWith(Path.Combine("测试论文", "测试论文.thesis.json"), StringComparison.Ordinal), "新建位置随论文题目更新");
        var path = Path.Combine(directory, "smoke.thesis.json"); location.Text = path;
        InvokeButton((Button)dialog.FindName("BrowseButton")); await Task.Delay(50);
        require(title.Text == "测试论文" && location.Text == path && !pending.IsCompleted, "取消位置选择保留表单");
        title.Text = "更新后的论文题目"; await Task.Delay(80);
        require(location.Text == path, "修改题目保留用户自定义位置");
        failPicker = true;
        InvokeButton((Button)dialog.FindName("BrowseButton")); await Task.Delay(50);
        require(((InfoBar)dialog.FindName("ValidationMessage")).IsOpen && ((Button)dialog.FindName("BrowseButton")).IsEnabled && !pending.IsCompleted, "位置选择失败后显示原因并可重试");
        var primary = Descendants(dialog).OfType<Button>().Single(b => b.Name == "PrimaryButton");
        location.Text = "relative.json"; InvokeButton(primary); await Task.Delay(100);
        require(!pending.IsCompleted && ((InfoBar)dialog.FindName("ValidationMessage")).IsOpen, "无效位置在新建窗口中显示错误");
        var existingPath = Path.Combine(directory, "existing.json");
        File.WriteAllText(existingPath, "existing project"); location.Text = existingPath;
        InvokeButton(primary); await Task.Delay(100);
        require(!pending.IsCompleted && File.ReadAllText(existingPath) == "existing project", "创建论文保护已有文件");
        File.WriteAllText(path, ""); // The native save picker reserves an empty file.
        location.Text = path; InvokeButton(primary);
        require(await pending.WaitAsync(TimeSpan.FromSeconds(15)) == ContentDialogResult.Primary && File.Exists(path), "新建窗口通过创建按钮生成项目");
    }

    private static void InvokeButton(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private async Task CaptureSmokeAsync(string directory, string name)
    {
        Root.UpdateLayout(); await Task.Delay(350);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(Root);
        var pixels = await bitmap.GetPixelsAsync();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        using var reader = DataReader.FromBuffer(pixels); var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync(); stream.Seek(0);
        using var output = File.Create(Path.Combine(directory, name + ".png")); await stream.AsStreamForRead().CopyToAsync(output);
    }
}
