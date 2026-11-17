using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Thesis.Core;
using Thesis.Gui.ViewModels;
using Thesis.Schema;

namespace Thesis.Gui;

public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? editorSyncTimer;
    private bool loadingEditor;
    private bool sourceEditorActive;
    private bool forceSourceEditor;
    private bool updatingToolbar;
    private string? loadedEditorState;

    private void InitializeEditor()
    {
        BodyEditor.GotFocus += (_, _) => { focusedEditor = BodyEditor; UpdateEditorToolbar(); };
        editorSyncTimer = DispatcherQueue.CreateTimer();
        editorSyncTimer.Interval = TimeSpan.FromMilliseconds(260);
        editorSyncTimer.IsRepeating = false;
        editorSyncTimer.Tick += (_, _) => FlushEditor();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is null or nameof(WorkspaceViewModel.BodyReadOnly))
            {
                BodyEditor.IsReadOnly = viewModel.BodyReadOnly;
                SourceBodyEditor.IsReadOnly = viewModel.BodyReadOnly;
                foreach (var segment in editorSegments) if (segment.Editor is { } editor) editor.IsReadOnly = viewModel.BodyReadOnly;
                UpdateEditorToolbar();
            }
        };
        viewModel.ContentReloaded += (_, _) => LoadEditorFromViewModel();
        Closed += (_, _) => editorSyncTimer?.Stop();
        LoadEditorFromViewModel();
    }

    private void LoadEditorFromViewModel()
    {
        if (loadingEditor || !viewModel.HasProject) return;
        editorSyncTimer?.Stop(); loadingEditor = true; loadedEditorState = null;
        try
        {
            var markdown = viewModel.Body;
            var usesMarkdown = viewModel.EditorUsesMarkdown;
            var blocks = viewModel.EditorBlocks;
            var reason = "";
            var supported = !usesMarkdown || ContentEditing.TryParseMarkdown(markdown, out blocks);
            if (!supported) reason = "这部分 Markdown 包含保留语法，可在交换视图中继续编辑。";
            sourceEditorActive = usesMarkdown && (forceSourceEditor || !supported);
            blockEditorActive = !sourceEditorActive && !NativeContentEditor.SupportsBlocks(blocks);
            if (!sourceEditorActive)
            {
                if (blockEditorActive) LoadObjectEditor(blocks);
                else
                {
                    editorSegments.Clear(); focusedEditor = null;
                    BodyEditor.IsReadOnly = false;
                    try { NativeContentEditor.LoadBlocks(BodyEditor, blocks); }
                    finally { BodyEditor.IsReadOnly = viewModel.BodyReadOnly; }
                }
            }
            SourceBodyEditor.Text = markdown;
            BodyEditor.Visibility = sourceEditorActive || blockEditorActive ? Visibility.Collapsed : Visibility.Visible;
            ObjectEditorScroll.Visibility = blockEditorActive ? Visibility.Visible : Visibility.Collapsed;
            SourceBodyEditor.Visibility = sourceEditorActive ? Visibility.Visible : Visibility.Collapsed;
            SourceModeButton.Visibility = usesMarkdown ? Visibility.Visible : Visibility.Collapsed;
            SourceModeButton.Content = sourceEditorActive ? "返回正文" : "Markdown";
            SourceModeButton.IsEnabled = supported;
            ToolTipService.SetToolTip(SourceModeButton, sourceEditorActive ? "返回原生正文编辑" : "查看导入的 Markdown");
            EditorModeNote.Text = viewModel.BodyReadOnly ? viewModel.EditorHint : reason;
            EditorModeNote.Visibility = sourceEditorActive || viewModel.Selected?.ReadOnly == true ? Visibility.Visible : Visibility.Collapsed;
            EditorPlaceholder.Visibility = blocks.Count == 0 && !sourceEditorActive && !blockEditorActive && !viewModel.BodyReadOnly ? Visibility.Visible : Visibility.Collapsed;
            BodyEditor.IsReadOnly = viewModel.BodyReadOnly;
            SourceBodyEditor.IsReadOnly = viewModel.BodyReadOnly;
            if (OutlineList.SelectedItem != viewModel.Selected) OutlineList.SelectedItem = viewModel.Selected;
            loadedEditorState = CaptureEditorState();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            loadedEditorState = null;
            BodyEditor.Visibility = ObjectEditorScroll.Visibility = Visibility.Collapsed;
            SourceBodyEditor.Visibility = Visibility.Visible;
            SourceBodyEditor.IsReadOnly = true;
            SourceBodyEditor.Text = viewModel.Body;
            sourceEditorActive = true;
            EditorModeNote.Text = "内容已保留，请检查对象信息。";
            viewModel.ShowError(ex);
        }
        finally { loadingEditor = false; UpdateEditorToolbar(); }
    }

    private void BodyEditor_TextChanged(object sender, RoutedEventArgs e)
    {
        if (!loadingEditor && !IgnoreUnchangedLoadNotification()) { editorSyncTimer?.Stop(); editorSyncTimer?.Start(); UpdateEditorToolbar(); }
    }

    private void BodyEditor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateEditorToolbar();

    private void SourceBody_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!loadingEditor && sourceEditorActive && !IgnoreUnchangedLoadNotification())
        {
            editorSyncTimer?.Stop(); editorSyncTimer?.Start();
            var supportsRichText = ContentEditing.TryParseMarkdown(SourceBodyEditor.Text, out _);
            SourceModeButton.IsEnabled = supportsRichText;
            EditorModeNote.Text = supportsRichText ? "Markdown 交换视图" : "当前内容包含保留语法，请在交换视图中编辑。";
            ToolTipService.SetToolTip(SourceModeButton, "返回富文本编辑");
        }
    }

    private string CaptureEditorState() => sourceEditorActive
        ? "source:" + SourceBodyEditor.Text
        : blockEditorActive
            ? "objects:" + ThesisJson.Serialize(ReadObjectEditor())
            : "rich:" + ThesisJson.Serialize(NativeContentEditor.ReadBlocks(BodyEditor));

    private bool IgnoreUnchangedLoadNotification()
    {
        if (loadedEditorState is null) return false;
        if (string.Equals(loadedEditorState, CaptureEditorState(), StringComparison.Ordinal)) return true;
        loadedEditorState = null;
        return false;
    }

    private void FlushEditor()
    {
        editorSyncTimer?.Stop();
        if (loadingEditor || !viewModel.HasProject || viewModel.BodyReadOnly) return;
        if (sourceEditorActive)
        {
            var sourceValue = NormalizeSourceText(SourceBodyEditor.Text);
            if (sourceValue != viewModel.Body) viewModel.Body = sourceValue;
        }
        else if (blockEditorActive) viewModel.SetEditorBlocks(ReadObjectEditor());
        else viewModel.SetEditorBlocks(NativeContentEditor.ReadBlocks(BodyEditor));
        var value = sourceEditorActive ? SourceBodyEditor.Text : viewModel.Body;
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
        if (sourceEditorActive) FlushEditor();
        if (!viewModel.EditorUsesMarkdown) return;
        if (sourceEditorActive && !ContentEditing.TryParseMarkdown(viewModel.Body, out _)) return;
        forceSourceEditor = !sourceEditorActive;
        LoadEditorFromViewModel();
        if (sourceEditorActive) SourceBodyEditor.Focus(FocusState.Programmatic);
        else BodyEditor.Focus(FocusState.Programmatic);
    }

    private void EditorBlockStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loadingEditor || updatingToolbar || BodyEditor is null || sourceEditorActive || EditorBlockStyle.SelectedItem is not ComboBoxItem item) return;
        var range = ActiveEditor.Document.Selection;
        range.ParagraphFormat.Style = (string)item.Tag switch
        {
            "heading1" => ParagraphStyle.Heading1,
            "heading2" => ParagraphStyle.Heading2,
            "heading3" => ParagraphStyle.Heading3,
            _ => ParagraphStyle.Normal
        };
        QueueEditorSync();
    }

    private enum EditorFormat { Bold, Italic, Underline, Strike, Superscript, Subscript }
    private void FormatBold_Click(object sender, RoutedEventArgs e) => ToggleFormat(EditorFormat.Bold);
    private void FormatItalic_Click(object sender, RoutedEventArgs e) => ToggleFormat(EditorFormat.Italic);
    private void FormatUnderline_Click(object sender, RoutedEventArgs e) => ToggleFormat(EditorFormat.Underline);
    private void FormatStrike_Click(object sender, RoutedEventArgs e) => ToggleFormat(EditorFormat.Strike);
    private void FormatSuperscript_Click(object sender, RoutedEventArgs e) => ToggleFormat(EditorFormat.Superscript);
    private void FormatSubscript_Click(object sender, RoutedEventArgs e) => ToggleFormat(EditorFormat.Subscript);

    private void UndoEditor_Click(object sender, RoutedEventArgs e)
    {
        if (sourceEditorActive || viewModel.BodyReadOnly || !ActiveEditor.Document.CanUndo()) return;
        ActiveEditor.Document.Undo();
        QueueEditorSync();
        UpdateEditorToolbar();
        ActiveEditor.Focus(FocusState.Programmatic);
    }

    private void RedoEditor_Click(object sender, RoutedEventArgs e)
    {
        if (sourceEditorActive || viewModel.BodyReadOnly || !ActiveEditor.Document.CanRedo()) return;
        ActiveEditor.Document.Redo();
        QueueEditorSync();
        UpdateEditorToolbar();
        ActiveEditor.Focus(FocusState.Programmatic);
    }

    private void ToggleFormat(EditorFormat kind)
    {
        if (sourceEditorActive || viewModel.BodyReadOnly) return;
        var format = ActiveEditor.Document.Selection.CharacterFormat;
        switch (kind)
        {
            case EditorFormat.Bold: format.Bold = format.Bold == FormatEffect.On ? FormatEffect.Off : FormatEffect.On; break;
            case EditorFormat.Italic: format.Italic = format.Italic == FormatEffect.On ? FormatEffect.Off : FormatEffect.On; break;
            case EditorFormat.Underline: format.Underline = format.Underline == UnderlineType.Single ? UnderlineType.None : UnderlineType.Single; break;
            case EditorFormat.Strike: format.Strikethrough = format.Strikethrough == FormatEffect.On ? FormatEffect.Off : FormatEffect.On; break;
            case EditorFormat.Superscript:
                format.Superscript = format.Superscript == FormatEffect.On ? FormatEffect.Off : FormatEffect.On;
                if (format.Superscript == FormatEffect.On) format.Subscript = FormatEffect.Off;
                break;
            case EditorFormat.Subscript:
                format.Subscript = format.Subscript == FormatEffect.On ? FormatEffect.Off : FormatEffect.On;
                if (format.Subscript == FormatEffect.On) format.Superscript = FormatEffect.Off;
                break;
        }
        ActiveEditor.Focus(FocusState.Programmatic);
        QueueEditorSync();
        UpdateEditorToolbar();
    }

    private void BulletList_Click(object sender, RoutedEventArgs e) => ToggleList(MarkerType.Bullet);
    private void NumberedList_Click(object sender, RoutedEventArgs e) => ToggleList(MarkerType.Arabic);

    private void ToggleList(MarkerType marker)
    {
        if (sourceEditorActive || viewModel.BodyReadOnly) return;
        var paragraph = ActiveEditor.Document.Selection.ParagraphFormat;
        paragraph.ListType = paragraph.ListType == marker ? MarkerType.None : marker;
        ActiveEditor.Focus(FocusState.Programmatic);
        QueueEditorSync();
        UpdateEditorToolbar();
    }

    private void UpdateEditorToolbar()
    {
        if (loadingEditor || BodyEditor is null || EditorBlockStyle is null) return;
        updatingToolbar = true;
        try
        {
            var editable = viewModel.HasProject && !viewModel.BodyReadOnly && !sourceEditorActive;
            InsertObjectButton.IsEnabled = CanInsertObject();
            var canUndo = editable && ActiveEditor.Document.CanUndo();
            var canRedo = editable && ActiveEditor.Document.CanRedo();
            UndoEditorButton.IsEnabled = canUndo;
            RedoEditorButton.IsEnabled = canRedo;
            EditorBlockStyle.IsEnabled = editable;
            foreach (var button in new[] { FormatBoldButton, FormatItalicButton, FormatUnderlineButton, FormatStrikeButton, FormatSuperscriptButton, FormatSubscriptButton, BulletListButton, NumberedListButton })
                button.IsEnabled = editable;
            if (!editable) return;

            var character = ActiveEditor.Document.Selection.CharacterFormat;
            FormatBoldButton.IsChecked = FormatState(character.Bold);
            FormatItalicButton.IsChecked = FormatState(character.Italic);
            FormatUnderlineButton.IsChecked = character.Underline switch { UnderlineType.Single => true, UnderlineType.None => false, _ => null };
            FormatStrikeButton.IsChecked = FormatState(character.Strikethrough);
            FormatSuperscriptButton.IsChecked = FormatState(character.Superscript);
            FormatSubscriptButton.IsChecked = FormatState(character.Subscript);

            var paragraph = ActiveEditor.Document.Selection.ParagraphFormat;
            BulletListButton.IsChecked = paragraph.ListType is MarkerType.Bullet or MarkerType.BlackCircleWingding or MarkerType.WhiteCircleWingding;
            NumberedListButton.IsChecked = paragraph.ListType is not MarkerType.None and not MarkerType.Undefined
                and not MarkerType.Bullet and not MarkerType.BlackCircleWingding and not MarkerType.WhiteCircleWingding;
            EditorBlockStyle.SelectedIndex = paragraph.Style switch
            {
                ParagraphStyle.Heading1 => 1,
                ParagraphStyle.Heading2 => 2,
                ParagraphStyle.Heading3 => 3,
                _ => 0
            };
        }
        finally { updatingToolbar = false; }
    }

    private static bool? FormatState(FormatEffect effect) => effect switch
    {
        FormatEffect.On => true,
        FormatEffect.Off => false,
        _ => null
    };

    private static string NormalizeSourceText(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');

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
