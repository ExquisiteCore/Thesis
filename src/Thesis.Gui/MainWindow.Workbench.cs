using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;
using Thesis.Application;
using Thesis.Core;
using Thesis.Schema;
using Thesis.Gui.ViewModels;
using Microsoft.Windows.Storage.Pickers;

namespace Thesis.Gui;

public sealed partial class MainWindow
{
    private string currentPage = "home";
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? previewTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? externalTimer;
    private bool checkingExternal;
    private void InitializeWorkbench()
    {
        if (Enum.TryParse<ElementTheme>(viewModel.Preferences.Theme, out var theme)) Root.RequestedTheme = theme;
        ShowPage("home");
        InitializeEditor();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is null or nameof(WorkspaceViewModel.CanCancelTask))
                CancelTaskButton.Visibility = viewModel.CanCancelTask ? Visibility.Visible : Visibility.Collapsed;
            if (args.PropertyName is null or nameof(WorkspaceViewModel.IsBusy))
                ProgressIndicator.Visibility = viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            if (args.PropertyName is null or nameof(WorkspaceViewModel.Status) or nameof(WorkspaceViewModel.IsDirty)
                or nameof(WorkspaceViewModel.IsBusy) or nameof(WorkspaceViewModel.HasError))
                StatusText.Text = viewModel.IsDirty && !viewModel.IsBusy && !viewModel.HasError ? "正在自动保存…" : viewModel.Status;
        };
        StatusText.Text = viewModel.Status;
        previewTimer = DispatcherQueue.CreateTimer(); previewTimer.Interval = TimeSpan.FromMilliseconds(650); previewTimer.IsRepeating = false;
        previewTimer.Tick += async (_, _) => { if ((currentPage == "preview" || LivePreviewToggle.IsChecked == true) && !viewModel.IsBusy) await RefreshQuickPreviewAsync(); };
        viewModel.Edited += (_, _) =>
        {
            if (currentPage != "preview" && LivePreviewToggle.IsChecked != true) return;
            if (pdfPreview is not null) { PreviewNotice.Text = "正文或模板已修改。当前 PDF 为先前导出版本，请重新导出。"; return; }
            previewTimer.Stop(); previewTimer.Start();
        };
        viewModel.ContentReloaded += (_, _) =>
        {
            previewJob?.Cancel(); previewGeneration++;
            quickPreview = null; pdfPreview = null; PreviewBlocks.Children.Clear();
            editingDesign = null; editingRole = null;
            if (currentPage == "template") LoadTemplateEditor();
            if (currentPage == "preview") { previewTimer.Stop(); previewTimer.Start(); }
        };
        externalTimer = DispatcherQueue.CreateTimer(); externalTimer.Interval = TimeSpan.FromSeconds(4);
        externalTimer.Tick += async (_, _) =>
        {
            if (checkingExternal || interactionInProgress || closing || viewModel.IsBusy || viewModel.IsDirty) return;
            checkingExternal = true;
            try { await viewModel.ReloadExternalAsync(); }
            catch (IOException) { /* A writer can be replacing the file; retry on the next tick. */ }
            catch (Exception ex) { viewModel.ShowError(ex); externalTimer.Stop(); }
            finally { checkingExternal = false; }
        };
        externalTimer.Start();
        WorkspaceGrid.SizeChanged += (_, _) => UpdateWritingPanes();
        viewModel.Recent.CollectionChanged += (_, _) => RecentEmpty.Visibility = viewModel.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentEmpty.Visibility = viewModel.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Closed += (_, _) => { externalTimer.Stop(); previewTimer.Stop(); previewJob?.Cancel(); };
    }
    private void ShowPage(string page)
    {
        currentPage = page;
        PageHeader.Visibility = page == "write" ? Visibility.Collapsed : Visibility.Visible;
        NavigationMenu.SelectedItem = NavigationMenu.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => (string)item.Tag == page);
        (PageHeading.Text, PageDescription.Text) = page switch
        {
            "write" => ("写作", "组织大纲，专注当前章节。"),
            "template" => ("模板", "核对学校要求，让格式为内容服务。"),
            "preview" => ("快速预览", "查看内容和主要样式；最终分页以导出的 PDF 为准。"),
            "checks" => ("检查", "逐项定位需要完善或确认的内容。"),
            "history" => ("历史", "对照之前的版本，找回需要的内容。"),
            _ => ("首页", "开始新的论文，或继续上次的写作。")
        };
        foreach (var (element, name) in new (FrameworkElement, string)[] { (HomePanel, "home"), (WorkspaceGrid, "write"), (TemplatePanel, "template"), (PreviewPanel, "preview"), (ChecksPanel, "checks"), (HistoryPanel, "history") })
            element.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
        if (page == "template" && (editingDesign is null || editingDesign.Id != viewModel.Design?.Id || editingDesign.SourcePath != viewModel.Design?.SourcePath)) LoadTemplateEditor();
        if (page == "history") { viewModel.RefreshHistory(); HistoryAfter.Text = viewModel.CurrentText; }
    }
    private async void Navigation_Invoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem { Tag: string page }) return;
        await RunAsync(async () =>
        {
            if (page == "theme") { Theme_Click(this, new RoutedEventArgs()); return; }
            ShowPage(page);
            if (page == "preview") await RefreshQuickPreviewAsync();
            if (page == "checks") await RefreshChecksAsync();
        });
    }
    private async void Recent_Click(object sender, ItemClickEventArgs e) => await RunAsync(async () =>
    {
        await viewModel.OpenAsync(((RecentProject)e.ClickedItem).Path); ShowPage("write"); await OfferRecoveryAsync();
    });
    private async Task OfferRecoveryAsync()
    {
        if (viewModel.HasRecovery && await DialogAsync("发现未保存记录", "上次编辑有一份本地恢复记录。可在“历史”中查看并恢复；当前磁盘项目会保留快照。", "查看恢复") == ContentDialogResult.Primary) ShowPage("history");
    }
    private async void ExportMarkdown_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    { var path = await SaveFileAsync("Markdown 文稿", ".md"); if (path is not null) await viewModel.ExportMarkdownAsync(path); });
    private async void ExportPackage_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    { var path = await SaveFileAsync("论文项目包", ".zip"); if (path is not null) await viewModel.ExportPackageAsync(path); });
    private async void ImportPackage_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var archive = await OpenFileAsync("导入论文项目包", ".zip"); if (archive is null) return;
        var picker = new FolderPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        var folder = await picker.PickSingleFolderAsync(); if (folder is null) return;
        var directory = Path.Combine(folder.Path, Path.GetFileNameWithoutExtension(archive) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var path = await Task.Run(() => ProjectPackage.Import(archive, directory));
        await viewModel.OpenAsync(path); ShowPage("write");
    });
    private async void ConvertMarkdown_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (await DialogAsync("将本章转为 Markdown", "普通文字可以继续编辑；原始图片、公式和复杂表格以 thesis-native 片段保留。本次操作会建立历史快照。", "转换") == ContentDialogResult.Primary) viewModel.ConvertSelectionToMarkdown();
    });
    private async void InsertImage_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!CanInsertObject() || viewModel.ProjectPath is null) return;
        var path = await OpenFileAsync("插入本地图片", ".png", ".jpg", ".jpeg", ".gif", ".bmp"); if (path is null) return;
        var image = await Task.Run(() => ProjectAssetStore.ImportImage(path, viewModel.ProjectPath, Path.GetFileNameWithoutExtension(path)));
        InsertObject(image);
    });
    private async void Find_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var search = new TextBox { PlaceholderText = "搜索章节标题或正文" };
        var results = new ListView { Height = 300, DisplayMemberPath = "Label" };
        search.TextChanged += (_, _) => results.ItemsSource = viewModel.Outline.Where(i => !string.IsNullOrWhiteSpace(search.Text) && (i.Label.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || i.ReadBody().Contains(search.Text, StringComparison.OrdinalIgnoreCase))).ToArray();
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(search); panel.Children.Add(results);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "查找论文内容", Content = panel, PrimaryButtonText = "定位", CloseButtonText = "关闭" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && results.SelectedItem is ViewModels.OutlineItem item)
        {
            viewModel.Selected = item; ShowPage("write");
            var index = viewModel.Body.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                if (sourceEditorActive)
                {
                    SourceBodyEditor.Focus(FocusState.Programmatic);
                    SourceBodyEditor.Select(index, search.Text.Length);
                }
                else
                {
                    BodyEditor.Focus(FocusState.Programmatic);
                    BodyEditor.Document.GetText(TextGetOptions.UseLf, out var editorText);
                    var editorIndex = editorText.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase);
                    if (editorIndex >= 0) BodyEditor.Document.Selection.SetRange(editorIndex, editorIndex + search.Text.Length);
                }
            }
        }
    });
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        Root.RequestedTheme = Root.RequestedTheme switch { ElementTheme.Default => ElementTheme.Light, ElementTheme.Light => ElementTheme.Dark, _ => ElementTheme.Default };
        viewModel.Preferences.Theme = Root.RequestedTheme.ToString(); viewModel.Preferences.Save();
    }
    private void Inspector_Click(object sender, RoutedEventArgs e)
    {
        viewModel.Preferences.ShowInspector = !viewModel.Preferences.ShowInspector; viewModel.Preferences.Save();
        UpdateWritingPanes();
    }
    private async void Check_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshChecksAsync);
    private async Task RefreshChecksAsync()
    {
        ChecksList.Visibility = Visibility.Collapsed;
        ChecksEmptyState.Visibility = Visibility.Visible;
        ChecksProgress.Visibility = Visibility.Visible;
        ChecksProgress.IsActive = true;
        ChecksStatusIcon.Visibility = Visibility.Collapsed;
        ChecksEmptyTitle.Text = "正在检查…";
        ChecksEmptyDetail.Text = "正在检查正文、项目素材和模板要求。";
        try
        {
            await viewModel.CheckAsync();
            if (viewModel.Issues.Count > 0)
            {
                ChecksList.Visibility = Visibility.Visible;
                ChecksEmptyState.Visibility = Visibility.Collapsed;
                return;
            }

            ChecksStatusIcon.Symbol = Symbol.Accept;
            ChecksStatusIcon.Visibility = Visibility.Visible;
            ChecksEmptyTitle.Text = "检查完成，当前没有待处理项";
            ChecksEmptyDetail.Text = "正式交付前仍请检查导出的 PDF 页面和真实分页。";
        }
        catch
        {
            ChecksStatusIcon.Symbol = Symbol.Important;
            ChecksStatusIcon.Visibility = Visibility.Visible;
            ChecksEmptyTitle.Text = "检查未完成";
            ChecksEmptyDetail.Text = "请查看页面上方的操作提示，点击“查看技术详情”定位问题。";
            throw;
        }
        finally
        {
            ChecksProgress.IsActive = false;
            ChecksProgress.Visibility = Visibility.Collapsed;
        }
    }
    private void Issue_Click(object sender, ItemClickEventArgs e)
    {
        var issue = (ProjectIssue)e.ClickedItem;
        if (issue.NodeId?.StartsWith("template:", StringComparison.Ordinal) == true)
        {
            ShowPage("template");
            var role = issue.NodeId["template:".Length..];
            if (role == "requirements")
            {
                Requirements_Click(this, new RoutedEventArgs());
                return;
            }
            if (editingDesign?.Roles.FirstOrDefault(candidate => candidate.Role == role) is { } target)
            {
                RoleList.SelectedItem = target;
                RoleList.ScrollIntoView(target);
            }
            return;
        }
        FlushEditor(); viewModel.Locate(issue); OutlineList.SelectedItem = viewModel.Selected; LoadEditorFromViewModel(); ShowPage("write");
    }
    private void Version_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try { if (VersionList.SelectedItem is ProjectHistoryEntry entry) HistoryBefore.Text = viewModel.HistoryText(entry.Path); }
        catch (Exception ex) { viewModel.ShowError(ex); }
    }
    private async void RestoreVersion_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (VersionList.SelectedItem is ProjectHistoryEntry entry && await DialogAsync("恢复历史版本", "当前内容会先保存为历史快照，再恢复选中的版本。", "恢复") == ContentDialogResult.Primary)
        { await viewModel.RestoreAsync(entry.Path); ShowPage("write"); }
    });
    private async void Recover_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (await DialogAsync("恢复未保存记录", "当前内容会先保留快照，再载入恢复记录。", "恢复") == ContentDialogResult.Primary) { await viewModel.RestoreAsync(); ShowPage("write"); }
    });
    private async void Diagnostics_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var progIds = new[] { "Kwps.Application", "KWPS.Application.9", "Word.Application" };
        var installed = progIds.Where(p => Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(p) is { } key && DisposeKey(key)).ToArray();
        await DialogAsync("运行环境", $"版本：{typeof(App).Assembly.GetName().Version}\n系统：{Environment.OSVersion}\n.NET：{Environment.Version}\n\n内置：Markdown、Open XML 快速预览、PDF 读取与检查。\n最终排版：{(installed.Length > 0 ? string.Join("、", installed) : "未检测到 WPS / Word COM 注册") }\n\n偏好设置：{WorkspacePreferences.FilePath}\n项目历史：项目旁的 .thesis 目录", null);
    });
    private static bool DisposeKey(Microsoft.Win32.RegistryKey key) { key.Dispose(); return true; }
}
