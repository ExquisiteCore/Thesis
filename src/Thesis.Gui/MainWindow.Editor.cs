using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Thesis.Gui.ViewModels;

namespace Thesis.Gui;

public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? editorSyncTimer;
    private bool loadingEditor;
    private bool sourceEditorActive;
    private bool forceSourceEditor;
    private bool updatingToolbar;

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
        editorSyncTimer?.Stop();
        loadingEditor = true;
        try
        {
            var markdown = viewModel.Body;
            var supportsRichText = RichMarkdownEditor.Supports(markdown, out var reason);
            sourceEditorActive = forceSourceEditor || !supportsRichText;
            if (supportsRichText)
            {
                // WinUI rejects programmatic document writes while IsReadOnly is set.
                // Project loads also happen while the workspace is busy or read-only.
                BodyEditor.IsReadOnly = false;
                try { RichMarkdownEditor.Load(BodyEditor, markdown); }
                finally { BodyEditor.IsReadOnly = viewModel.BodyReadOnly; }
            }
            SourceBodyEditor.Text = markdown;
            BodyEditor.Visibility = sourceEditorActive ? Visibility.Collapsed : Visibility.Visible;
            SourceBodyEditor.Visibility = sourceEditorActive ? Visibility.Visible : Visibility.Collapsed;
            SourceModeButton.Content = sourceEditorActive ? "富文本" : "源码";
            SourceModeButton.IsEnabled = supportsRichText;
            ToolTipService.SetToolTip(SourceModeButton, sourceEditorActive ? "返回富文本编辑" : "打开源码视图");
            EditorModeNote.Text = sourceEditorActive
                ? string.IsNullOrWhiteSpace(reason) ? "源码视图" : reason
                : "富文本编辑";
            EditorModeNote.Visibility = sourceEditorActive ? Visibility.Visible : Visibility.Collapsed;
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
        finally
        {
            loadingEditor = false;
            UpdateEditorToolbar();
        }
    }

    private void BodyEditor_TextChanged(object sender, RoutedEventArgs e)
    {
        if (!loadingEditor) { editorSyncTimer?.Stop(); editorSyncTimer?.Start(); UpdateEditorToolbar(); }
    }

    private void BodyEditor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateEditorToolbar();

    private void SourceBody_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!loadingEditor && sourceEditorActive)
        {
            editorSyncTimer?.Stop(); editorSyncTimer?.Start();
            var supportsRichText = RichMarkdownEditor.Supports(SourceBodyEditor.Text, out var reason);
            SourceModeButton.IsEnabled = supportsRichText;
            EditorModeNote.Text = supportsRichText ? "源码视图" : reason;
            ToolTipService.SetToolTip(SourceModeButton, "返回富文本编辑");
        }
    }

    private void FlushEditor()
    {
        editorSyncTimer?.Stop();
        if (loadingEditor || !viewModel.HasProject || viewModel.BodyReadOnly) return;
        var value = sourceEditorActive ? NormalizeSourceText(SourceBodyEditor.Text) : RichMarkdownEditor.ReadMarkdown(BodyEditor);
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
        if (sourceEditorActive && !RichMarkdownEditor.Supports(viewModel.Body, out _)) return;
        forceSourceEditor = !sourceEditorActive;
        LoadEditorFromViewModel();
        if (sourceEditorActive) SourceBodyEditor.Focus(FocusState.Programmatic);
        else BodyEditor.Focus(FocusState.Programmatic);
    }

    private void EditorBlockStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loadingEditor || updatingToolbar || BodyEditor is null || sourceEditorActive || EditorBlockStyle.SelectedItem is not ComboBoxItem item) return;
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

    private void UndoEditor_Click(object sender, RoutedEventArgs e)
    {
        if (sourceEditorActive || viewModel.BodyReadOnly || !BodyEditor.Document.CanUndo()) return;
        BodyEditor.Document.Undo();
        QueueEditorSync();
        UpdateEditorToolbar();
        BodyEditor.Focus(FocusState.Programmatic);
    }

    private void RedoEditor_Click(object sender, RoutedEventArgs e)
    {
        if (sourceEditorActive || viewModel.BodyReadOnly || !BodyEditor.Document.CanRedo()) return;
        BodyEditor.Document.Redo();
        QueueEditorSync();
        UpdateEditorToolbar();
        BodyEditor.Focus(FocusState.Programmatic);
    }

    private void ToggleFormat(EditorFormat kind)
    {
        if (sourceEditorActive || viewModel.BodyReadOnly) return;
        var format = BodyEditor.Document.Selection.CharacterFormat;
        switch (kind)
        {
            case EditorFormat.Bold: format.Bold = format.Bold == FormatEffect.On ? FormatEffect.Off : FormatEffect.On; break;
            case EditorFormat.Italic: format.Italic = format.Italic == FormatEffect.On ? FormatEffect.Off : FormatEffect.On; break;
            case EditorFormat.Strike: format.Strikethrough = format.Strikethrough == FormatEffect.On ? FormatEffect.Off : FormatEffect.On; break;
        }
        BodyEditor.Focus(FocusState.Programmatic);
        QueueEditorSync();
        UpdateEditorToolbar();
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
        UpdateEditorToolbar();
    }

    private void UpdateEditorToolbar()
    {
        if (loadingEditor || BodyEditor is null || EditorBlockStyle is null) return;
        updatingToolbar = true;
        try
        {
            var editable = viewModel.HasProject && !viewModel.BodyReadOnly && !sourceEditorActive;
            var canUndo = editable && BodyEditor.Document.CanUndo();
            var canRedo = editable && BodyEditor.Document.CanRedo();
            UndoEditorButton.IsEnabled = canUndo;
            RedoEditorButton.IsEnabled = canRedo;
            EditorBlockStyle.IsEnabled = editable;
            foreach (var button in new[] { FormatBoldButton, FormatItalicButton, FormatStrikeButton, BulletListButton, NumberedListButton })
                button.IsEnabled = editable;
            if (!editable) return;

            var character = BodyEditor.Document.Selection.CharacterFormat;
            FormatBoldButton.IsChecked = FormatState(character.Bold);
            FormatItalicButton.IsChecked = FormatState(character.Italic);
            FormatStrikeButton.IsChecked = FormatState(character.Strikethrough);

            var paragraph = BodyEditor.Document.Selection.ParagraphFormat;
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
