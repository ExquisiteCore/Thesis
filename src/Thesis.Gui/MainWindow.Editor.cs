using Markdig.Syntax;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Thesis.Gui.ViewModels;
using Windows.UI;

namespace Thesis.Gui;

public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? editorSyncTimer;
    private bool loadingEditor;
    private bool sourceEditorActive;
    private bool forceSourceEditor;

    private void InitializeEditor()
    {
        editorSyncTimer = DispatcherQueue.CreateTimer();
        editorSyncTimer.Interval = TimeSpan.FromMilliseconds(260);
        editorSyncTimer.IsRepeating = false;
        editorSyncTimer.Tick += (_, _) => FlushEditor();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(WorkspaceViewModel.BodyReadOnly))
            {
                BodyEditor.IsReadOnly = viewModel.BodyReadOnly;
                SourceBodyEditor.IsReadOnly = viewModel.BodyReadOnly;
            }
        };
        viewModel.ContentReloaded += (_, _) => LoadEditorFromViewModel();
        Closed += (_, _) => editorSyncTimer?.Stop();
        LoadEditorFromViewModel();
    }

    private void LoadEditorFromViewModel()
    {
        if (loadingEditor || !viewModel.HasProject) return;
        editorSyncTimer?.Stop();
        loadingEditor = true;
        try
        {
            var markdown = viewModel.Body;
            var supportsRichText = RichMarkdownEditor.Supports(markdown, out var reason);
            sourceEditorActive = forceSourceEditor || !supportsRichText;
            if (supportsRichText) RichMarkdownEditor.Load(BodyEditor, markdown);
            SourceBodyEditor.Text = markdown;
            BodyEditor.Visibility = sourceEditorActive ? Visibility.Collapsed : Visibility.Visible;
            SourceBodyEditor.Visibility = sourceEditorActive ? Visibility.Visible : Visibility.Collapsed;
            SourceModeButton.Content = sourceEditorActive ? "富文本编辑" : "Markdown 源码";
            SourceModeButton.IsEnabled = supportsRichText;
            EditorModeNote.Text = sourceEditorActive
                ? $"{reason} Markdown 源码视图会保留这些对象和格式。"
                : "直接写作；选中文字后可设置格式。Ctrl+B 加粗，Ctrl+I 斜体，Ctrl+Z 撤销。";
            EditorBlockStyle.IsEnabled = !sourceEditorActive && !viewModel.BodyReadOnly;
        foreach (var button in new[] { FormatBoldButton, FormatItalicButton, FormatStrikeButton, BulletListButton, NumberedListButton })
                button.IsEnabled = !sourceEditorActive && !viewModel.BodyReadOnly;
            EditorPlaceholder.Visibility = markdown.Length == 0 && !sourceEditorActive && !viewModel.BodyReadOnly ? Visibility.Visible : Visibility.Collapsed;
            BodyEditor.IsReadOnly = viewModel.BodyReadOnly;
            SourceBodyEditor.IsReadOnly = viewModel.BodyReadOnly;
            if (OutlineList.SelectedItem != viewModel.Selected) OutlineList.SelectedItem = viewModel.Selected;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            forceSourceEditor = true;
            sourceEditorActive = true;
            BodyEditor.Visibility = Visibility.Collapsed;
            SourceBodyEditor.Visibility = Visibility.Visible;
            SourceBodyEditor.Text = viewModel.Body;
            EditorModeNote.Text = "当前内容需要源码视图以完整保留格式。";
            viewModel.ShowError(ex);
        }
        finally { loadingEditor = false; }
    }

    private void BodyEditor_TextChanged(object sender, RoutedEventArgs e)
    {
        if (!loadingEditor) { editorSyncTimer?.Stop(); editorSyncTimer?.Start(); }
    }

    private void SourceBody_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!loadingEditor) { editorSyncTimer?.Stop(); editorSyncTimer?.Start(); }
    }

    private void FlushEditor()
    {
        editorSyncTimer?.Stop();
        if (loadingEditor || !viewModel.HasProject || viewModel.BodyReadOnly) return;
        var value = sourceEditorActive ? SourceBodyEditor.Text : RichMarkdownEditor.ReadMarkdown(BodyEditor);
        if (value != viewModel.Body) viewModel.Body = value;
        EditorPlaceholder.Visibility = value.Length == 0 && !sourceEditorActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Outline_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingEditor || OutlineList.SelectedItem is not OutlineItem selected) return;
        FlushEditor();
        viewModel.Selected = selected;
        LoadEditorFromViewModel();
    }

    private void SourceMode_Click(object sender, RoutedEventArgs e)
    {
        FlushEditor();
        forceSourceEditor = !sourceEditorActive;
        LoadEditorFromViewModel();
        if (sourceEditorActive) SourceBodyEditor.Focus(FocusState.Programmatic);
        else BodyEditor.Focus(FocusState.Programmatic);
    }

    private void EditorBlockStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loadingEditor || BodyEditor is null || sourceEditorActive || EditorBlockStyle.SelectedItem is not ComboBoxItem item) return;
        var range = BodyEditor.Document.Selection;
        range.ParagraphFormat.Style = (string)item.Tag switch
        {
            "heading1" => ParagraphStyle.Heading1,
            "heading2" => ParagraphStyle.Heading2,
            "heading3" => ParagraphStyle.Heading3,
            _ => ParagraphStyle.Normal
        };
        QueueEditorSync();
    }

    private enum EditorFormat { Bold, Italic, Strike }
    private void FormatBold_Click(object sender, RoutedEventArgs e) => ToggleFormat(EditorFormat.Bold);
    private void FormatItalic_Click(object sender, RoutedEventArgs e) => ToggleFormat(EditorFormat.Italic);
    private void FormatStrike_Click(object sender, RoutedEventArgs e) => ToggleFormat(EditorFormat.Strike);

    private void ToggleFormat(EditorFormat kind)
    {
        if (sourceEditorActive || viewModel.BodyReadOnly) return;
        var format = BodyEditor.Document.Selection.CharacterFormat;
        switch (kind)
        {
            case EditorFormat.Bold: format.Bold = format.Bold == FormatEffect.On ? FormatEffect.Off : FormatEffect.Toggle; break;
            case EditorFormat.Italic: format.Italic = format.Italic == FormatEffect.On ? FormatEffect.Off : FormatEffect.Toggle; break;
            case EditorFormat.Strike: format.Strikethrough = format.Strikethrough == FormatEffect.On ? FormatEffect.Off : FormatEffect.Toggle; break;
        }
        BodyEditor.Focus(FocusState.Programmatic);
        QueueEditorSync();
    }

    private void BulletList_Click(object sender, RoutedEventArgs e) => ToggleList(MarkerType.Bullet);
    private void NumberedList_Click(object sender, RoutedEventArgs e) => ToggleList(MarkerType.Arabic);

    private void ToggleList(MarkerType marker)
    {
        if (sourceEditorActive || viewModel.BodyReadOnly) return;
        var paragraph = BodyEditor.Document.Selection.ParagraphFormat;
        paragraph.ListType = paragraph.ListType == marker ? MarkerType.None : marker;
        BodyEditor.Focus(FocusState.Programmatic);
        QueueEditorSync();
    }

    private void QueueEditorSync()
    {
        editorSyncTimer?.Stop();
        editorSyncTimer?.Start();
    }

    private void LivePreview_Changed(object sender, RoutedEventArgs e)
    {
        if (LivePreviewColumn is null) return;
        var split = LivePreviewToggle.IsChecked == true;
        LivePreviewColumn.Width = new GridLength(split ? 400 : 0);
        InspectorColumn.Width = new GridLength(!split && WorkspaceGrid.ActualWidth >= 1100 && viewModel.Preferences.ShowInspector ? 280 : 0);
        if (split) _ = RefreshQuickPreviewAsync();
    }
}
