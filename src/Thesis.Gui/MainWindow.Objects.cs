using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Thesis.Application;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Gui;

public sealed partial class MainWindow
{
    private sealed record EditorSegment(RichEditBox? Editor, ThesisContentBlock? Object);
    private readonly List<EditorSegment> editorSegments = [];
    private RichEditBox? focusedEditor;
    private bool blockEditorActive;
    private RichEditBox ActiveEditor => blockEditorActive ? focusedEditor ?? BodyEditor : BodyEditor;

    private void LoadObjectEditor(List<ThesisContentBlock> blocks)
    {
        ObjectEditorPanel.Children.Clear(); editorSegments.Clear(); focusedEditor = null;
        var paragraphs = new List<ThesisContentBlock>();
        void AddParagraphs()
        {
            if (paragraphs.Count == 0) return;
            var editor = new RichEditBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                FontSize = 17, Padding = new Thickness(0, 8, 0, 12), BorderThickness = new Thickness(0),
                Background = Brush("ThesisPaperBrush"), Foreground = Brush("ThesisInkBrush"), MinHeight = 58 };
            NativeContentEditor.LoadBlocks(editor, paragraphs);
            editor.IsReadOnly = viewModel.BodyReadOnly;
            editor.GotFocus += (_, _) => { focusedEditor = editor; UpdateEditorToolbar(); };
            editor.SelectionChanged += (_, _) => UpdateEditorToolbar();
            editor.TextChanged += (_, _) => { if (!loadingEditor && !IgnoreUnchangedLoadNotification()) QueueEditorSync(); };
            editorSegments.Add(new(editor, null)); ObjectEditorPanel.Children.Add(editor); paragraphs.Clear();
        }
        foreach (var block in blocks)
        {
            if (block.Type == "paragraph") { paragraphs.Add(block); continue; }
            AddParagraphs();
            editorSegments.Add(new(null, block));
            var content = new StackPanel { Spacing = 12 };
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var label = block.Type switch { "image" => "图片", "table" => "表格", "equation" => "公式 · LaTeX", _ => "原稿对象 · 保真保留" };
            toolbar.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("ThesisMutedBrush"), FontSize = 12, Margin = new Thickness(0, 0, 12, 0) });
            if (!viewModel.BodyReadOnly && block.Type is "image" or "table" or "equation")
            {
                var edit = new Button { Content = "编辑", Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
                edit.Click += async (_, _) => await RunAsync(async () => { FlushEditor(); await EditObjectAsync(block, false); });
                toolbar.Children.Add(edit);
                toolbar.Children.Add(ObjectButton("上移", () => ChangeObject(block, -1)));
                toolbar.Children.Add(ObjectButton("下移", () => ChangeObject(block, 1)));
                toolbar.Children.Add(ObjectButton("删除", () => ChangeObject(block, 0)));
            }
            content.Children.Add(toolbar);
            switch (block.Type)
            {
                case "image":
                    try
                    {
                        var directory = Path.GetDirectoryName(viewModel.ProjectPath!)!;
                        content.Children.Add(new Image { Source = new BitmapImage(new Uri(Path.GetFullPath(block.Path!, directory))),
                            MaxHeight = 360, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center });
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { content.Children.Add(ObjectText("图片路径待检查")); }
                    break;
                case "table" when block.Table is { } table:
                    content.Children.Add(BuildTableGrid(table, false)); break;
                case "equation":
                    content.Children.Add(MathPreview(block.Latex ?? block.Text ?? "")); break;
                default: content.Children.Add(ObjectText(block.Text ?? "完整对象保留在原稿快照中，请在快速预览或成品中查看。")); break;
            }
            if (!string.IsNullOrWhiteSpace(block.Caption ?? block.Table?.Caption)) content.Children.Add(ObjectText(block.Caption ?? block.Table!.Caption!));
            ObjectEditorPanel.Children.Add(new Border { Child = content, Padding = new Thickness(16), CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1), BorderBrush = Brush("ThesisRuleBrush"), Margin = new Thickness(0, 6, 0, 14) });
        }
        // Every object has an ordinary paragraph after it, so writing can continue with one click.
        if (paragraphs.Count == 0) paragraphs.Add(new() { Text = "" });
        AddParagraphs();
    }

    private List<ThesisContentBlock> ReadObjectEditor() => editorSegments.SelectMany(segment => segment.Editor is { } editor
        ? NativeContentEditor.ReadBlocks(editor) : new List<ThesisContentBlock> { segment.Object! }).ToList();

    private void ChangeObject(ThesisContentBlock block, int direction)
    {
        FlushEditor(); var blocks = ReadObjectEditor(); var index = blocks.IndexOf(block);
        if (index < 0) return;
        if (direction == 0) blocks.RemoveAt(index);
        else { var target = index + direction; if (target < 0 || target >= blocks.Count) return; (blocks[index], blocks[target]) = (blocks[target], blocks[index]); }
        viewModel.SetEditorBlocks(blocks); LoadEditorFromViewModel();
    }

    private void InsertObject(ThesisContentBlock block)
    {
        FlushEditor();
        var blocks = blockEditorActive ? ReadObjectEditor() : NativeContentEditor.ReadBlocks(BodyEditor);
        var editor = ActiveEditor;
        editor.Document.GetText(Microsoft.UI.Text.TextGetOptions.UseLf, out var text);
        var position = Math.Min(editor.Document.Selection.StartPosition, text.Length);
        var paragraph = text[..position].Count(c => c == '\n');
        var index = Math.Min(paragraph + 1, blocks.Count);
        if (blockEditorActive)
        {
            index = 0;
            foreach (var segment in editorSegments)
            {
                if (segment.Editor == editor) { index += Math.Min(paragraph + 1, NativeContentEditor.ReadBlocks(editor).Count); break; }
                index += segment.Editor is { } item ? NativeContentEditor.ReadBlocks(item).Count : 1;
            }
        }
        blocks.Insert(index, block);
        viewModel.SetEditorBlocks(blocks); LoadEditorFromViewModel();
    }

    private async void InsertTable_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!CanInsertObject()) return;
        var rows = new NumberBox { Header = "数据行数", Value = 3, Minimum = 1, Maximum = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var columns = new NumberBox { Header = "列数", Value = 3, Minimum = 1, Maximum = 20, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var panel = new StackPanel { Spacing = 16 }; panel.Children.Add(rows); panel.Children.Add(columns);
        var dialog = ObjectDialog("插入表格", panel);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) InsertObject(ContentEditing.CreateTable((int)rows.Value, (int)columns.Value));
    });

    private async void InsertEquation_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if (CanInsertObject()) await EditObjectAsync(new() { Type = "equation", Latex = @"E = mc^2", DisplayEquation = true }, true); });

    private bool CanInsertObject() => !viewModel.BodyReadOnly && viewModel.Selected?.Kind is "chapter" or "section" && !sourceEditorActive;

    private async Task EditObjectAsync(ThesisContentBlock original, bool insert)
    {
        var block = ThesisJson.Deserialize<ThesisContentBlock>(ThesisJson.Serialize(original));
        var panel = new StackPanel { Spacing = 14, MinWidth = 380 };
        var caption = new TextBox { Header = block.Type == "table" ? "表题" : "题注", Text = block.Caption ?? block.Table?.Caption ?? "", TextWrapping = TextWrapping.Wrap };
        TextBox? latex = null, alt = null; NumberBox? width = null;
        if (block.Type == "equation")
        {
            latex = new TextBox { Header = "LaTeX 公式", Text = block.Latex ?? block.Text ?? "", AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, MinHeight = 100, FontFamily = new FontFamily("Consolas") };
            panel.Children.Add(latex);
            var preview = new ContentControl { Content = MathPreview(latex.Text), HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 64 };
            latex.TextChanged += (_, _) => preview.Content = MathPreview(latex.Text);
            panel.Children.Add(preview);
        }
        if (block.Type == "image")
        {
            width = new NumberBox { Header = "宽度（厘米，0 为自动）", Minimum = 0, Maximum = 40, Value = (block.WidthEmu ?? 0) / 360000d, SmallChange = .5 };
            alt = new TextBox { Header = "替代文字", Text = block.AltText ?? "", TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(width); panel.Children.Add(alt);
        }
        if (block.Type == "table" && block.Table is { } table)
        {
            var gridHost = new ContentControl { Content = BuildTableGrid(table, true) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            void Resize(int row, int column) { ContentEditing.ResizeTable(table, row, column); gridHost.Content = BuildTableGrid(table, true); }
            actions.Children.Add(ObjectButton("＋行", () => { if (table.Rows.Count < 100) Resize(table.Rows.Count + 1, table.Headers.Count); }));
            actions.Children.Add(ObjectButton("＋列", () => { if (table.Headers.Count < 20) Resize(table.Rows.Count, table.Headers.Count + 1); }));
            actions.Children.Add(ObjectButton("－行", () => { if (table.Rows.Count > 1) Resize(table.Rows.Count - 1, table.Headers.Count); }));
            actions.Children.Add(ObjectButton("－列", () => { if (table.Headers.Count > 1) Resize(table.Rows.Count, table.Headers.Count - 1); }));
            panel.Children.Add(actions); panel.Children.Add(new ScrollViewer { Content = gridHost, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 380 });
        }
        panel.Children.Add(caption);
        var dialog = ObjectDialog(block.Type switch { "table" => "编辑表格", "image" => "图片设置", _ => "编辑公式" }, new ScrollViewer { Content = panel, MaxHeight = 580 });
        if (latex is not null) dialog.PrimaryButtonClick += (_, e) => { if (string.IsNullOrWhiteSpace(latex.Text)) { e.Cancel = true; latex.Header = "请输入 LaTeX 公式"; } };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        block.Caption = string.IsNullOrWhiteSpace(caption.Text) ? null : caption.Text;
        if (block.Table is not null) { block.Table.Caption = block.Caption; block.Caption = null; }
        if (latex is not null) block.Latex = latex.Text;
        if (width is not null) { block.WidthEmu = width.Value > 0 && double.IsFinite(width.Value) ? (int)Math.Round(width.Value * 360000) : null; block.HeightEmu = null; block.AltText = alt!.Text; }
        if (insert) InsertObject(block);
        else { var blocks = ReadObjectEditor(); var index = blocks.IndexOf(original); if (index < 0) return; blocks[index] = block; viewModel.SetEditorBlocks(blocks); LoadEditorFromViewModel(); }
    }

    private ContentDialog ObjectDialog(string title, object content) => new() { Title = title, Content = content, XamlRoot = Root.XamlRoot,
        RequestedTheme = Root.RequestedTheme, PrimaryButtonText = "确定", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
    private static Brush Brush(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
    private static TextBlock ObjectText(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = Brush("ThesisMutedBrush"), FontSize = 13 };
    private Button ObjectButton(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(8, 4, 8, 4), FontSize = 12, IsEnabled = !viewModel.BodyReadOnly };
        button.Click += async (_, _) => await RunAsync(() => { action(); return Task.CompletedTask; }); return button;
    }

    private static Grid BuildTableGrid(ThesisTableContent table, bool editable)
    {
        var grid = new Grid();
        var rows = new[] { table.Headers }.Concat(table.Rows).ToArray();
        var columns = rows.Select(r => r.Count).DefaultIfEmpty(1).Max();
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new() { Width = editable ? new GridLength(130) : new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < rows.Length; r++)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var c = 0; c < columns; c++)
            {
                var row = r; var column = c;
                FrameworkElement cell;
                if (editable)
                {
                    var input = new TextBox { Text = c < rows[r].Count ? rows[r][c] : "", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinWidth = 100,
                        PlaceholderText = r == 0 ? "表头" : "", FontSize = 13 };
                    input.TextChanged += (_, _) => { while (rows[row].Count <= column) rows[row].Add(""); rows[row][column] = input.Text; }; cell = input;
                }
                else cell = new Border { Child = ObjectText(c < rows[r].Count ? rows[r][c] : ""), Padding = new Thickness(8),
                    BorderThickness = new Thickness(.5), BorderBrush = Brush("ThesisRuleBrush"), Background = r == 0 ? Brush("ThesisAccentSoftBrush") : Brush("ThesisPaperBrush") };
                Grid.SetRow(cell, r); Grid.SetColumn(cell, c); grid.Children.Add(cell);
            }
        }
        return grid;
    }

    private static FrameworkElement MathPreview(string source)
    {
        try { return RenderMath(LatexMath.Parse(source), 24); }
        catch (InvalidDataException ex) { return ObjectText(ex.Message); }
    }

    private static FrameworkElement RenderMath(MathNode node, double size)
    {
        FrameworkElement Render(MathNode value) => RenderMath(value, size);
        switch (node)
        {
            case MathText text: return new TextBlock { Text = text.Value, FontSize = size, FontFamily = new FontFamily("Cambria Math"), Foreground = Brush("ThesisInkBrush"), VerticalAlignment = VerticalAlignment.Center };
            case MathSequence sequence:
                var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (var child in sequence.Children) line.Children.Add(Render(child)); return line;
            case MathFraction fraction:
                var fractionPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 2, 3, 2) };
                var numerator = RenderMath(fraction.Numerator, size * .85); numerator.HorizontalAlignment = HorizontalAlignment.Center;
                var denominator = RenderMath(fraction.Denominator, size * .85); denominator.HorizontalAlignment = HorizontalAlignment.Center;
                fractionPanel.Children.Add(numerator); fractionPanel.Children.Add(new Border { Height = 1, Background = Brush("ThesisInkBrush"), Margin = new Thickness(0, 2, 0, 2) }); fractionPanel.Children.Add(denominator); return fractionPanel;
            case MathRadical radical:
                var root = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (radical.Degree is not null) root.Children.Add(RenderMath(radical.Degree, size * .6));
                root.Children.Add(Render(new MathText("√")));
                root.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Brush("ThesisInkBrush"), Child = Render(radical.Value), Padding = new Thickness(1, 0, 2, 0) }); return root;
            case MathScript script:
                var scripts = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
                if (script.Superscript is not null) scripts.Children.Add(RenderMath(script.Superscript, size * .65));
                if (script.Subscript is not null) scripts.Children.Add(RenderMath(script.Subscript, size * .65));
                var scriptLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                scriptLine.Children.Add(Render(script.Value));
                scripts.Margin = new Thickness(0, script.Subscript is null ? -size * .45 : 0, 1, script.Superscript is null ? -size * .4 : 0);
                scriptLine.Children.Add(scripts); return scriptLine;
            default: return ObjectText("");
        }
    }
}
