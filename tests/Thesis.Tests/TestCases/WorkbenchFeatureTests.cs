using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Application;
using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

public sealed class WorkbenchFeatureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thesis-workbench-" + Guid.NewGuid().ToString("N"));
    public WorkbenchFeatureTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string FilePath(string name) => Path.Combine(directory, name);

    [Fact]
    public void MarkdownRoundTripPreservesSyntaxMetadataAndUnheadedProse()
    {
        var content = new ThesisContent { Title = "算法研究", Author = "作者", AbstractZh = "摘要文字", KeywordsZh = ["模型", "验证"],
            Chapters = [new() { Title = "绪论", Markdown = "**粗体**和*斜体*。\n\n- 第一项\n- 第二项\n\n```csharp\nvar x = 1;\n```", Sections = [new() { Title = "方法", Markdown = "| A | B |\n| --- | --- |\n| 1 | 2 |" }] }], References = ["[1] Example[J]."], Acknowledgements = "谢谢。" };
        var parsed = MarkdownProjectFormat.Parse(MarkdownProjectFormat.Serialize(content));
        Assert.Equal(content.Title, parsed.Title); Assert.Equal(content.Author, parsed.Author); Assert.Equal(content.KeywordsZh, parsed.KeywordsZh);
        Assert.Equal(content.Chapters[0].Markdown, parsed.Chapters[0].Markdown);
        Assert.Equal(content.Chapters[0].Sections[0].Markdown, parsed.Chapters[0].Sections[0].Markdown);
        Assert.Equal("前置正文", MarkdownProjectFormat.Parse("# 论文\n\n前置正文\n\n## 第一章\n\n正文").Chapters[0].Markdown);
    }

    [Fact]
    public void MarkdownProducesActualEmphasisTablesLinksAndImagesAndOwnsCopiedAssets()
    {
        var png = FilePath("figure.png"); File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1ZkAAAAASUVORK5CYII="));
        var markdown = FilePath("draft.md"); File.WriteAllText(markdown, "# 论文\n\n## 绪论\n\n**粗体** 和 *斜体* [链接](https://example.com)\n\n![图](figure.png)\n\n| 列一 | 列二 |\n| --- | --- |\n| 甲 | 乙 |\n");
        var content = MarkdownProjectFormat.Import(markdown, FilePath("paper.json")); File.Delete(png);
        var asset = Assert.Single(ContentPaths.Assets(content)); Assert.True(File.Exists(asset));
        var doc = FilePath("render.docx"); ThesisDocumentGenerator.Generate(content, new(), doc);
        using (var word = WordprocessingDocument.Open(doc, false))
        {
            Assert.Single(word.MainDocumentPart!.Document!.Body!.Elements<Table>());
            Assert.Single(word.MainDocumentPart.ImageParts);
            Assert.Contains(word.MainDocumentPart.Document.Descendants<Run>(), r => r.InnerText == "粗体" && r.RunProperties?.Bold is not null);
            Assert.Contains(word.MainDocumentPart.HyperlinkRelationships, r => r.Uri.Host == "example.com");
        }
        var preview = DocumentPreviewReader.Read(doc);
        Assert.Contains(preview.Blocks, b => b.Kind == "image"); Assert.Contains(preview.Blocks, b => b.Kind == "table");
        var exported = FilePath("export.md"); MarkdownProjectFormat.Export(content, exported);
        Assert.True(Directory.Exists(FilePath("export.assets")));
        Assert.Single(ContentPaths.Assets(MarkdownProjectFormat.Import(exported, FilePath("again.json"))));
        Assert.Throws<InvalidDataException>(() => MarkdownProjectFormat.Export(content, asset));
    }

    [Fact]
    public void TemplateDesignRoundTripChangesGeneratedStyleWithoutChangingOriginal()
    {
        var template = FilePath("template.docx"); TemplateDesigner.CreateDefault(template);
        var before = File.ReadAllBytes(template);
        var design = TemplateDesigner.Inspect(template);
        var body = Assert.Single(design.Roles, r => r.Role == "body");
        body.OverrideFormat = true; body.Font = "仿宋"; body.Size = 14; body.Confirmed = true;
        var path = TemplateLibraryStore.Save(design, FilePath("templates"));
        var loaded = TemplateLibraryStore.Load(path); Assert.Equal("仿宋", loaded.Roles.Single(r => r.Role == "body").Font);
        var project = ThesisProjectStore.Create(FilePath("paper.json"), new() { TemplatePath = template, TemplateDesignPath = path,
            CoverFields = new() { ["{{学校}}"] = "示例大学" }, Content = new() { Title = "测试题目", Author = "测试作者", Chapters = [new() { Title = "绪论", Markdown = "这是测试正文。" }] } });
        var result = ProjectCompositionService.Compose(project, FilePath("result.docx"), false);
        using var doc = WordprocessingDocument.Open(result.Document, false);
        var style = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().Single(s => s.StyleId == "Thesis_body");
        Assert.Equal("仿宋", style.StyleRunProperties!.GetFirstChild<RunFonts>()!.EastAsia!.Value);
        Assert.Contains("测试题目", doc.MainDocumentPart.Document!.InnerText);
        Assert.Equal(before, File.ReadAllBytes(template));
        File.AppendAllText(loaded.SourcePath, "tampered"); Assert.Throws<InvalidDataException>(() => TemplateLibraryStore.Load(path));
    }

    [Fact]
    public void TemplateCheckGroupsUnconfirmedRolesIntoOneActionableIssue()
    {
        var template = FilePath("check-template.docx"); TemplateDesigner.CreateDefault(template);
        var design = TemplateDesigner.Inspect(template);
        foreach (var role in design.Roles) { role.Confidence = .9; role.Confirmed = true; }
        foreach (var role in design.Roles.Where(role => role.Role is "heading2" or "heading3"))
        {
            role.Confidence = .2;
            role.Confirmed = false;
        }
        Assert.Contains(design.Roles, role => role.Role == "heading2" && role.Confidence < .5 && !role.Confirmed);
        var designPath = TemplateLibraryStore.Save(design, FilePath("templates"));
        Assert.Contains(TemplateLibraryStore.Load(designPath).Roles, role => role.Role == "heading2" && role.Confidence < .5 && !role.Confirmed);
        var project = ThesisProjectStore.Create(FilePath("check-project.json"), new()
        {
            TemplatePath = template,
            TemplateDesignPath = designPath,
            Content = new() { Title = "论文", Chapters = [new() { Title = "绪论", Markdown = "正文" }] }
        });

        var issue = Assert.Single(ProjectChecks.Check(project), candidate => candidate.NodeId == "template:heading2");

        Assert.Contains("2 项", issue.Title);
        Assert.Contains("二级标题", issue.Message);
        Assert.Contains("三级标题", issue.Message);
        Assert.Contains("暂用正文格式", issue.Message);
    }

    [Fact]
    public void HistoryKeepsPreviousRevisionAndRecoveryDoesNotOverwriteConcurrentWriter()
    {
        var project = ThesisProjectStore.Create(FilePath("paper.json"), new() { Content = new() { Title = "原文" } });
        project.Document.Content.Title = "新版"; project = ThesisProjectStore.Save(project);
        var history = Assert.Single(ProjectHistoryStore.List(project)); Assert.Equal("原文", ProjectHistoryStore.Read(project, history.Path).Content.Title);
        var stale = ThesisProjectStore.Open(project.Path); stale.Document.Content.Title = "未保存修改"; ProjectHistoryStore.WriteRecovery(stale);
        project.Document.Content.Title = "外部版本"; ThesisProjectStore.Save(project);
        Assert.Throws<IOException>(() => ThesisProjectStore.Save(stale));
        Assert.Equal("外部版本", ThesisProjectStore.Open(project.Path).Document.Content.Title);
        Assert.Equal("未保存修改", ProjectHistoryStore.Read(stale, ProjectHistoryStore.RecoveryPath(stale)).Content.Title);
    }

    [Fact]
    public void RecoveryOnlyOffersChangesNotAlreadySaved()
    {
        var project = ThesisProjectStore.Create(FilePath("paper.json"), new() { Content = new() { Title = "原文" } });
        project.Document.Content.Title = "已编辑";
        ProjectHistoryStore.WriteRecovery(project);
        Assert.True(ProjectHistoryStore.HasRecovery(ThesisProjectStore.Open(project.Path)));
        ThesisProjectStore.Save(project);
        Assert.False(ProjectHistoryStore.HasRecovery(ThesisProjectStore.Open(project.Path)));
        project.Document.Content.Title = "尚未保存";
        ProjectHistoryStore.WriteRecovery(project);
        Assert.True(ProjectHistoryStore.HasRecovery(ThesisProjectStore.Open(project.Path)));
    }

    [Fact]
    public void ComposingProtectsTheMaterializedTemplate()
    {
        var template = FilePath("template.docx"); TemplateDesigner.CreateDefault(template);
        var design = TemplateDesigner.Inspect(template); design.ApplyMarginsToAllSections = true;
        var designPath = TemplateLibraryStore.Save(design, FilePath("templates"));
        var project = ThesisProjectStore.Create(FilePath("paper.json"), new() { TemplatePath = template, TemplateDesignPath = designPath,
            Content = new() { Title = "论文", Chapters = [new() { Title = "绪论", Markdown = "正文" }] } });
        var compiled = TemplateLibraryStore.Materialize(TemplateLibraryStore.Load(designPath), FilePath(".thesis/templates/compiled"));
        var bytes = File.ReadAllBytes(compiled);
        Assert.Throws<ArgumentException>(() => ProjectCompositionService.Compose(project, compiled, false));
        Assert.Equal(bytes, File.ReadAllBytes(compiled));
    }

    [Fact]
    public void PackageRemainsUsableAfterOriginalInputsAreRemovedAndRejectsTraversal()
    {
        var template = FilePath("template.docx"); TemplateDesigner.CreateDefault(template);
        var project = ThesisProjectStore.Create(FilePath("paper.json"), new() { TemplatePath = template, Content = new() { Title = "便携项目", Chapters = [new() { Title = "绪论", Markdown = "正文" }] } });
        var archive = FilePath("paper.zip"); ProjectPackage.Export(project, archive); File.Delete(template);
        var restored = ThesisProjectStore.Open(ProjectPackage.Import(archive, FilePath("unpacked")));
        Assert.True(File.Exists(ThesisProjectStore.Resolve(restored, restored.Document.TemplatePath)));
        ProjectCompositionService.Compose(restored, FilePath("portable.docx"), false);
        var bad = FilePath("bad.zip");
        using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create)) { zip.CreateEntry("../outside"); zip.CreateEntry("paper.thesis.json"); }
        Assert.Throws<InvalidDataException>(() => ProjectPackage.Import(bad, FilePath("bad-extracted")));
        Assert.False(File.Exists(FilePath("outside")));
    }

    [Fact]
    public async Task TemplateHistoryRestoresTheOriginalRulesAndSurvivesAutosave()
    {
        var vm = new Thesis.Gui.ViewModels.WorkspaceViewModel(directory);
        await vm.CreateAsync(FilePath("history-project.json"));
        vm.Body = "原始正文"; await vm.SaveAsync();
        var original = ThesisProjectStore.Open(vm.ProjectPath!);
        ProjectHistoryStore.Checkpoint(original);
        var snapshot = ProjectHistoryStore.List(original).First();
        var oldDesignPath = ThesisProjectStore.Resolve(original, original.Document.TemplateDesignPath)!;
        var design = TemplateLibraryStore.Load(oldDesignPath);
        var originalSize = design.Roles.Single(r => r.Role == "body").Size;
        design.Roles.Single(r => r.Role == "body").Size = 19;
        await vm.UseDesignAsync(design);
        Assert.Equal(originalSize, TemplateLibraryStore.Load(oldDesignPath).Roles.Single(r => r.Role == "body").Size);
        vm.Body = "更新后的正文";
        var autosave = vm.SaveAsync();
        await vm.RestoreAsync(snapshot.Path); await autosave;
        Assert.Equal("原始正文", vm.Body);
        Assert.Equal(originalSize, vm.Design!.Roles.Single(r => r.Role == "body").Size);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task ExternalUpdatesNotifyPreviewWithoutDirtyingProject()
    {
        var vm = new Thesis.Gui.ViewModels.WorkspaceViewModel(directory);
        await vm.CreateAsync(FilePath("external.json")); await vm.SaveAsync();
        var notifications = 0; vm.ContentReloaded += (_, _) => notifications++;
        var external = ThesisProjectStore.Open(vm.ProjectPath!); external.Document.Content.Chapters[0].Markdown = "Agent 的新内容";
        ThesisProjectStore.Save(external); await vm.ReloadExternalAsync();
        Assert.Equal(1, notifications); Assert.Equal("Agent 的新内容", vm.Body); Assert.False(vm.IsDirty);
        vm.Body = "本地未保存内容";
        await vm.ReloadExternalAsync(); Assert.Equal(1, notifications); Assert.Equal("本地未保存内容", vm.Body);
    }

    [Fact]
    public void MarkdownConversionKeepsNativeFormulaAndMakesOrdinaryTextEditable()
    {
        var template = FilePath("manuscript.docx"); TemplateDesigner.CreateDefault(template);
        using (var doc = WordprocessingDocument.Open(template, true))
        {
            var body = doc.MainDocumentPart!.Document!.Body!;
            body.InsertAfter(new Paragraph(new DocumentFormat.OpenXml.Math.OfficeMath(new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Text("x=1")))), body.Elements<Paragraph>().First(p => p.InnerText.StartsWith("此处是正文")));
            doc.MainDocumentPart.Document.Save();
        }
        var content = ProjectContentImporter.Import(template, FilePath("native.json")).Content;
        var markdown = string.Join("\n", content.Chapters.Select(c => EditableMarkdownBuilder.Convert(c.Paragraphs, c.Blocks, c.Tables)));
        Assert.Contains("正文示例", markdown); Assert.Contains("thesis-native", markdown);
        var docx = FilePath("converted.docx");
        ThesisDocumentGenerator.Generate(new() { Title = "转换", AssetDirectory = content.AssetDirectory, Chapters = [new() { Title = "绪论", Markdown = markdown }] }, new(), docx);
        using var converted = WordprocessingDocument.Open(docx, false);
        Assert.Contains(converted.MainDocumentPart!.Document!.Descendants(), element => element.LocalName == "oMath");
    }

    [Fact]
    public void PackageWithExternalAssetFailsBeforeCreatingDestination()
    {
        var archive = FilePath("external.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("paper.thesis.json").Open());
            writer.Write(ThesisJson.Serialize(new ThesisProject { TemplatePath = "../outside.docx" }));
        }
        Assert.Throws<InvalidDataException>(() => ProjectPackage.Import(archive, FilePath("rejected")));
        Assert.False(Directory.Exists(FilePath("rejected")));
    }

    private LayoutFacts Audit((double X, double Y, string Text)[][] pages, PdfLayoutAudit.Manifest manifest)
    {
        using var writer = new PdfDocumentBuilder(); var font = writer.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = writer.AddPage(595, 842);
            foreach (var line in lines) page.AddText(line.Text, 11, new PdfPoint(line.X, 842 - line.Y), font);
        }
        var path = FilePath(Guid.NewGuid() + ".pdf"); File.WriteAllBytes(path, writer.Build());
        return PdfLayoutAudit.Analyze(path, manifest);
    }
    [Fact]
    public void ManagedPdfAuditUsesRenderedFooterAndIgnoresBottomTableNumbers()
    {
        var facts = Audit([[(70, 100, "Heading"), (70, 150, "Body"), (150, 730, "41"), (290, 810, "- 17 -")]], new() { Headings = ["Heading"] });
        Assert.Empty(facts.Errors); Assert.Equal("17", facts.TocPages["Heading"]);
    }
    [Fact]
    public void ManagedPdfAuditRejectsOrphansMissingLabelsAmbiguityAndOverflow()
    {
        Assert.Contains(Audit([[(70, 680, "Heading"), (290, 810, "1")]], new() { Headings = ["Heading"] }).Errors, e => e.Contains("no following body"));
        Assert.Contains(Audit([[(70, 100, "Heading"), (70, 150, "Body")]], new() { Headings = ["Heading"] }).Errors, e => e.Contains("label is missing"));
        Assert.Contains(Audit([[(70, 100, "Heading"), (290, 810, "1")], [(70, 100, "Heading"), (290, 810, "2")]], new() { Headings = ["Heading"] }).Errors, e => e.Contains("2 matches"));
        Assert.Contains(Audit([[(590, 100, "Overflow")]], new()).Errors, e => e.Contains("outside"));
    }
    [Fact]
    public void ManagedPdfAuditUsesSectionFormatsAndOfficeBookmarksForRepeatedHeadings()
    {
        var roman = Audit([[(70, 100, "Abstract"), (70, 150, "Body"), (290, 810, "I")], [(70, 100, "Abstract"), (70, 150, "Table"), (290, 810, "1")]], new() { Headings = ["Abstract"], PageFormats = new() { ["Abstract"] = "upperRoman" } });
        Assert.Empty(roman.Errors); Assert.Equal("I", roman.TocPages["Abstract"]);
        var host = Audit([[(70, 100, "Summary"), (70, 150, "Body"), (290, 810, "1")], [(70, 100, "Summary"), (70, 150, "Body"), (290, 810, "2")]], new()
        {
            HeadingEntries = [new() { Key = "a", Text = "Summary" }, new() { Key = "b", Text = "Summary" }],
            HostPages = new() { ["a"] = new() { PhysicalPage = 1 }, ["b"] = new() { PhysicalPage = 2 } }
        });
        Assert.Empty(host.Errors); Assert.Equal("2", host.TocPages["b"]);
    }
}
