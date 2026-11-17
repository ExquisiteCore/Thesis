using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Thesis.Application;
using Thesis.Core;
using Thesis.Gui.ViewModels;
using Thesis.OpenXml;
using Thesis.Schema;
using Xunit;
using M = DocumentFormat.OpenXml.Math;

public sealed class NativeEditingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thesis-native-" + Guid.NewGuid().ToString("N"));
    public NativeEditingTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public async Task NativeBlocksSurviveGuiSaveCliGenerationAndReload()
    {
        var projectPath = Path.Combine(directory, "paper.thesis.json");
        var viewModel = new WorkspaceViewModel();
        await viewModel.CreateAsync(projectPath);
        var blocks = new List<ThesisContentBlock>
        {
            new() { Runs = [new() { Text = "重点", Bold = true, Underline = true }, new() { Text = "正文" }] },
            ContentEditing.CreateTable(2, 2),
            new() { Type = "equation", Latex = @"\frac{\alpha_i^2}{\sqrt{x+1}}", DisplayEquation = true }
        };
        blocks[1].Table!.Rows[0][0] = "实验值";
        viewModel.SetEditorBlocks(blocks); await viewModel.SaveAsync();
        var revision = ThesisProjectStore.Open(projectPath).Document.Revision;
        viewModel.SetEditorBlocks(blocks); await viewModel.SaveAsync();
        Assert.Equal(revision, ThesisProjectStore.Open(projectPath).Document.Revision);
        await viewModel.OpenAsync(projectPath);
        Assert.False(viewModel.BodyReadOnly);
        Assert.True(viewModel.EditorBlocks[0].Runs[0].Underline);
        Assert.Equal("实验值", viewModel.EditorBlocks[1].Table!.Rows[0][0]);
        Assert.Equal(blocks[2].Latex, viewModel.EditorBlocks[2].Latex);
        var output = Path.Combine(directory, "paper.docx");
        ThesisDocumentGenerator.Generate(ThesisProjectStore.Open(projectPath).Document.Content, new(), output);
        using var doc = WordprocessingDocument.Open(output, false);
        Assert.Single(doc.MainDocumentPart!.Document!.Descendants<M.Fraction>());
        Assert.Empty(new OpenXmlValidator().Validate(doc));
        var before = File.ReadAllBytes(projectPath);
        Assert.Equal(before, File.ReadAllBytes(projectPath));
    }

    [Fact]
    public void MarkdownAdapterPreservesObjectsAndFormattedText()
    {
        var markdown = "**重点**和*斜体*。\n\n| A | B |\n| --- | --- |\n| 1 | 2 |\n\n![实验图](<assets/a.png>)\n\n$$\n\\frac{1}{2}\n$$";
        Assert.True(ContentEditing.TryParseMarkdown(markdown, out var blocks));
        Assert.Equal(new[] { "paragraph", "table", "image", "equation" }, blocks.Select(b => b.Type));
        Assert.True(blocks[0].Runs[0].Bold);
        Assert.Equal("assets/a.png", blocks[2].Path);
        Assert.Equal(@"\frac{1}{2}", blocks[3].Latex);
        var serialized = MarkdownSupport.Body([], blocks, []);
        Assert.Contains("**重点**", serialized);
        Assert.True(ContentEditing.TryParseMarkdown(serialized, out var roundtrip));
        Assert.Equal(blocks[3].Latex, roundtrip[3].Latex);
        Assert.False(ContentEditing.TryParseMarkdown("[需要保留的链接](https://example.com)", out _));
    }

    [Fact]
    public void MarkdownInterchangeRetainsUnderlineWithoutInventingHtml()
    {
        var block = new ThesisContentBlock { Runs = [new() { Text = "划线", Underline = true }] };
        var text = MarkdownSupport.Body([], [block], []);
        Assert.True(ContentEditing.TryParseMarkdown(text, out var parsed));
        Assert.True(parsed[0].Runs[0].Underline);
    }

    [Fact]
    public void ImageImportCopiesAndDeduplicatesProjectAssets()
    {
        var source = Path.Combine(directory, "source.png");
        File.WriteAllBytes(source, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
        var project = Path.Combine(directory, "project", "paper.thesis.json");
        var image = ProjectAssetStore.ImportImage(source, project, "实验图");
        Assert.Equal(image.Path, ProjectAssetStore.ImportImage(source, project).Path);
        File.Delete(source);
        Assert.True(File.Exists(Path.GetFullPath(image.Path!, Path.GetDirectoryName(project)!)));
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "project", "assets")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedImagesResolveRelativeToProjectAssets(bool chaptersOnly)
    {
        var source = Path.Combine(directory, "source.png");
        File.WriteAllBytes(source, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
        var projectDirectory = Path.Combine(directory, "project");
        var project = Path.Combine(projectDirectory, "paper.thesis.json");
        var image = ProjectAssetStore.ImportImage(source, project, "实验图");
        File.Delete(source);
        var content = new ThesisContent
        {
            Title = "图片路径",
            AssetDirectory = projectDirectory,
            Chapters = [new() { Title = "正文", Blocks = [image] }]
        };
        var output = Path.Combine(directory, $"images-{chaptersOnly}.docx");

        ThesisDocumentGenerator.Generate(content, new(), output, chaptersOnly);

        using var doc = WordprocessingDocument.Open(output, false);
        Assert.Single(doc.MainDocumentPart!.ImageParts);
        Assert.Single(doc.MainDocumentPart.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Blip>());
        var preview = DocumentPreviewService.Content(content, cancellationToken: CancellationToken.None, chaptersOnly: chaptersOnly);
        Assert.Contains(preview.Blocks, block => block.Kind == "image" && block.Image is { Length: > 0 });
    }

    [Fact]
    public void LatexWritesValidatedNativeMathAndBoundsMalformedInput()
    {
        var output = Path.Combine(directory, "math.docx");
        var expressions = new[] { @"x_i^2 + \frac{1}{\sqrt[3]{a+b}}", @"\sum_{i=1}^{n} \alpha_i", @"\unknown{x}" };
        var content = new ThesisContent { Title = "公式", Chapters = [new() { Title = "实验", Blocks = expressions.Select(s => new ThesisContentBlock { Type = "equation", Latex = s }).ToList() }] };
        ThesisDocumentGenerator.Generate(content, new(), output);
        using var doc = WordprocessingDocument.Open(output, false);
        Assert.Equal(3, doc.MainDocumentPart!.Document!.Descendants<M.OfficeMath>().Count());
        Assert.Empty(new OpenXmlValidator().Validate(doc));
        Assert.Contains(@"\unknown", doc.MainDocumentPart.Document.InnerText);
        Assert.Throws<InvalidDataException>(() => LatexMath.Parse(@"\frac{1}"));
        Assert.Throws<InvalidDataException>(() => LatexMath.Parse(string.Concat(Enumerable.Repeat(@"\sqrt", 100)) + "x"));
    }

    [Fact]
    public void LegacyTablesAndParagraphsMigrateWithoutMutatingInput()
    {
        var paragraphs = new List<string> { "正文" };
        var table = ContentEditing.CreateTable(2, 2).Table!; table.Rows[0][0] = "原值";
        var blocks = ContentEditing.Read(paragraphs, [], [table]);
        blocks[1].Table!.Rows[0][0] = "编辑值";
        var resized = blocks[1].Table!;
        ContentEditing.ResizeTable(resized, 3, 3);
        Assert.Equal("原值", table.Rows[0][0]);
        Assert.Equal(3, resized.Rows.Count);
        Assert.All(resized.Rows, row => Assert.Equal(3, row.Count));
        Assert.Equal("正文", blocks[0].Text!);
    }
}
