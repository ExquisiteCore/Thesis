using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Thesis.Schema;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Thesis.Gui;

public sealed partial class MainWindow
{
    private CancellationTokenSource? previewJob;
    private DocumentPreview? quickPreview;
    private PdfDocument? pdfPreview;
    private int previewPage;
    private long previewGeneration;
    private const int PreviewChunkSize = 24;
    private async Task RefreshQuickPreviewAsync()
    {
        if (!viewModel.HasProject) return;
        previewJob?.Cancel(); previewJob?.Dispose(); previewJob = new();
        var token = previewJob.Token; var generation = ++previewGeneration;
        try
        {
            PreviewNotice.Text = "正在更新内容预览…";
            var preview = await viewModel.PreviewAsync(token, PreviewCurrent.IsChecked == true);
            if (token.IsCancellationRequested || generation != previewGeneration) return;
            quickPreview = preview; pdfPreview = null; previewPage = 0; await DrawPreviewAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { PreviewNotice.Text = "预览未完成：" + ex.Message; }
    }
    private async Task ShowPdfAsync(string path)
    {
        previewJob?.Cancel(); previewGeneration++;
        pdfPreview = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
        previewPage = 0; ShowPage("preview"); await DrawPreviewAsync();
    }
    private async Task DrawPreviewAsync()
    {
        var generation = ++previewGeneration;
        PreviewBlocks.Children.Clear();
        LivePreviewBlocks.Children.Clear();
        if (pdfPreview is not null)
        {
            previewPage = Math.Clamp(previewPage, 0, (int)pdfPreview.PageCount - 1);
            PreviewPosition.Text = $"{previewPage + 1} / {pdfPreview.PageCount} 页";
            PreviewNotice.Text = "已导出的实际 PDF 页面。修改正文后请重新导出，以更新最终分页。";
            using var page = pdfPreview.GetPage((uint)previewPage);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = (uint)(1000 * PreviewZoom.Value / 100) });
            var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            if (generation == previewGeneration)
            {
                PreviewBlocks.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
                if (LivePreviewToggle.IsChecked == true) LivePreviewBlocks.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
            }
            return;
        }
        if (quickPreview is null) return;
        var chunks = Math.Max(1, (quickPreview.Blocks.Count + PreviewChunkSize - 1) / PreviewChunkSize);
        previewPage = Math.Clamp(previewPage, 0, chunks - 1);
        PreviewPosition.Text = $"内容段 {previewPage + 1} / {chunks}";
        PreviewNotice.Text = string.Join(" ", quickPreview.Notes.Distinct());
        await DrawBlocksAsync(PreviewBlocks, quickPreview.Blocks.Skip(previewPage * PreviewChunkSize).Take(PreviewChunkSize), generation);
        if (LivePreviewToggle.IsChecked == true && currentPage != "preview")
            await DrawBlocksAsync(LivePreviewBlocks, quickPreview.Blocks, generation);
    }

    private async Task DrawBlocksAsync(StackPanel target, IEnumerable<PreviewBlock> blocks, long generation)
    {
        target.Children.Clear();
        var foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"];
        var border = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"];
        foreach (var block in blocks)
        {
            if (generation != previewGeneration) return;
            if (block.Kind == "image" && block.Image is { } bytes)
            {
                try
                {
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); writer.DetachStream(); }
                    stream.Seek(0); var bitmap = new BitmapImage { DecodePixelWidth = 1200 }; await bitmap.SetSourceAsync(stream);
                    if (generation != previewGeneration) return;
                    target.Children.Add(new Image { Source = bitmap, MaxHeight = 650, Stretch = Stretch.Uniform });
                }
                catch (Exception) { target.Children.Add(Text("[此图片格式请在成品 Word / PDF 中查看]", 14)); }
            }
            else if (block.Kind == "table")
            {
                var table = new Grid(); var count = block.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max();
                for (var c = 0; c < count; c++) table.ColumnDefinitions.Add(new ColumnDefinition());
                for (var r = 0; r < block.Rows.Count; r++)
                {
                    table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    for (var c = 0; c < block.Rows[r].Count; c++)
                    {
                        var cell = new Border { BorderBrush = border, BorderThickness = new Thickness(.5), Padding = new Thickness(6), Child = Text(block.Rows[r][c], 14) };
                        Grid.SetRow(cell, r); Grid.SetColumn(cell, c); table.Children.Add(cell);
                    }
                }
                target.Children.Add(table);
            }
            else
            {
                var text = new RichTextBlock { FontFamily = new FontFamily(block.Font), FontSize = block.Size * 4 / 3, Foreground = foreground, IsTextSelectionEnabled = true,
                    TextAlignment = block.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, "both" or "distribute" => TextAlignment.Justify, _ => TextAlignment.Left } };
                var paragraph = new Paragraph();
                foreach (var piece in block.Runs)
                {
                    var run = new Run { Text = piece.Text, FontWeight = piece.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal, FontStyle = piece.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal };
                    if (piece.Strike) run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                    if (Uri.TryCreate(piece.Link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") { var link = new Hyperlink { NavigateUri = uri }; link.Inlines.Add(run); paragraph.Inlines.Add(link); }
                    else paragraph.Inlines.Add(run);
                }
                text.Blocks.Add(paragraph); target.Children.Add(text);
            }
        }
    }
    private static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size, Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"], TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private async void RefreshPreview_Click(object sender, RoutedEventArgs e) => await RefreshQuickPreviewAsync();
    private async void PreviewScope_Changed(object sender, RoutedEventArgs e)
    {
        if (previewTimer is not null && currentPage == "preview") await RefreshQuickPreviewAsync();
    }
    private async void PdfPreview_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if (viewModel.PdfPath is { } path) await ShowPdfAsync(path); else { var selected = await OpenFileAsync("查看 PDF", ".pdf"); if (selected is not null) await ShowPdfAsync(selected); } });
    private async void PreviewPrevious_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { previewPage--; await DrawPreviewAsync(); });
    private async void PreviewNext_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { previewPage++; await DrawPreviewAsync(); });
    private async void PreviewZoom_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (PreviewPaper is null) return;
        PreviewPaper.Width = 794 * e.NewValue / 100;
        if (pdfPreview is not null) await RunAsync(DrawPreviewAsync);
    }
}
