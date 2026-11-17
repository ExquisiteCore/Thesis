using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Application;
using Thesis.Cli;
using Thesis.Gui.ViewModels;
using Thesis.OpenXml;
using Thesis.Schema;
using Xunit;

public sealed class ProjectWorkspaceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thesis-project-tests-" + Guid.NewGuid().ToString("N"));
    public ProjectWorkspaceTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string FilePath(string name) => Path.Combine(directory, name);

    [Fact]
    public void StaleWriterPreservesAgentChangesAndStableChapterIds()
    {
        var original = ThesisProjectStore.Create(FilePath("project.json"), new() { Content = new() { Chapters = [new() { Title = "第一章" }] } });
        var stale = ThesisProjectStore.Open(original.Path);
        original.Document.Content.Title = "Agent 修改";
        var updated = ThesisProjectStore.Save(original);
        stale.Document.Content.Title = "过期修改";
        Assert.Throws<IOException>(() => ThesisProjectStore.Save(stale));
        var reopened = ThesisProjectStore.Open(original.Path);
        Assert.Equal("Agent 修改", reopened.Document.Content.Title);
        Assert.Equal(updated.Document.Content.Chapters[0].Id, reopened.Document.Content.Chapters[0].Id);
        Assert.Equal(2, reopened.Document.Revision);
    }

    [Fact]
    public void CreationAcceptsPickerReservationButPreservesExistingProject()
    {
        var path = FilePath("reserved.json");
        File.WriteAllText(path, "");
        ThesisProjectStore.Create(path, new());
        var before = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => ThesisProjectStore.Create(path, new()));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task NewProjectPersistsTitleAuthorAndDefaultTemplateBeforeReturning()
    {
        var vm = new WorkspaceViewModel(directory);
        await vm.CreateAsync(FilePath("新建论文.thesis.json"), "校园服务系统", "测试作者");
        vm.Body = "**正文**\n\n| 项目 | 结果 |\n| --- | --- |\n| 预览 | 正常 |";
        Assert.Contains("| 项目 | 结果 |", vm.Body);
        await vm.SaveAsync();
        var project = ThesisProjectStore.Open(vm.ProjectPath!);
        Assert.Contains("| 项目 | 结果 |", project.Document.Content.Chapters[0].Markdown);
        Assert.Equal("校园服务系统", project.Document.Content.Title);
        Assert.Equal("测试作者", project.Document.Content.Author);
        Assert.True(File.Exists(ThesisProjectStore.Resolve(project, project.Document.TemplateDesignPath)));
        Assert.Equal("绪论", Assert.Single(project.Document.Content.Chapters).Title);
        Assert.False(vm.IsDirty);
        Assert.Equal("校园服务系统", Assert.Single(vm.Recent).Title);
    }

    [Fact]
    public async Task NewProjectCanBindTemplateAndInitializeEditableOutline()
    {
        var template = FilePath("school-template.docx");
        TemplateDesigner.CreateDefault(template);
        using (var doc = WordprocessingDocument.Open(template, true))
        {
            var body = doc.MainDocumentPart!.Document!.Body!;
            body.RemoveAllChildren();
            body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }), new Run(new Text("第一章 绪论"))));
            body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading2" }), new Run(new Text("1.1 研究背景"))));
            body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Normal" }), new Run(new Text("模板正文样本。"))));
            body.Append(new SectionProperties(new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440 }));
            doc.MainDocumentPart.Document.Save();
        }

        var vm = new WorkspaceViewModel(directory);
        await vm.CreateAsync(FilePath("带模板.thesis.json"), "测试论文", "测试作者", template);

        var project = ThesisProjectStore.Open(vm.ProjectPath!);
        var storedTemplate = ThesisProjectStore.Resolve(project, project.Document.TemplatePath)!;
        Assert.True(File.Exists(storedTemplate));
        Assert.Equal("测试论文", project.Document.Content.Title);
        var chapter = Assert.Single(project.Document.Content.Chapters);
        Assert.Equal("第一章 绪论", chapter.Title);
        Assert.Equal("1.1 研究背景", Assert.Single(chapter.Sections).Title);
    }

    [Fact]
    public void MissingRecentFilesAreHiddenAndErrorsAreAvailableOnEveryPage()
    {
        var preferences = WorkspacePreferences.Load(FilePath("workspace.json"));
        preferences.Remember(FilePath("missing.thesis.json"), "旧项目");
        var vm = new WorkspaceViewModel(directory);
        Assert.Empty(vm.Recent);
        vm.ShowError(new IOException("Structure audit failed: /word/document.xml contains a duplicate shape id"));
        Assert.True(vm.HasError);
        Assert.Contains("结构检查未通过", vm.ErrorMessage);
        Assert.Contains("duplicate shape id", vm.ErrorDetails);
        vm.HasError = false;
        Assert.False(vm.HasError);
    }

    [Fact]
    public void MalformedAndDuplicateNodesAreRejectedBeforeWriting()
    {
        var project = new ThesisProject { Content = new() { Chapters = [new() { Id = "same" }, new() { Id = "same" }] } };
        Assert.Throws<InvalidDataException>(() => ThesisProjectStore.Create(FilePath("invalid.json"), project));
        Assert.False(File.Exists(FilePath("invalid.json")));
        File.WriteAllText(FilePath("content.json"), "{\"documentKind\":\"not-content\"}");
        Assert.Throws<InvalidDataException>(() => ProjectContentImporter.Import(FilePath("content.json"), FilePath("project.json")));
    }

    [Fact]
    public async Task ConcurrentAutosavesKeepLatestTextAndLeaveProjectClean()
    {
        var vm = new WorkspaceViewModel(directory);
        await vm.CreateAsync(FilePath("autosave.json"));
        vm.Body = "修改一";
        var first = vm.SaveAsync();
        vm.Body = "最终内容\n保留中文与段落";
        var second = vm.SaveAsync();
        await Task.WhenAll(first, second);
        Assert.False(vm.IsDirty);
        Assert.Equal("最终内容\n保留中文与段落", ThesisProjectStore.Open(vm.ProjectPath!).Document.Content.Chapters[0].Markdown);
    }

    [Fact]
    public async Task ConflictKeepsCurrentTextAndCanSaveIndependentCopy()
    {
        var vm = new WorkspaceViewModel(directory);
        await vm.CreateAsync(FilePath("conflict.json"));
        vm.Body = "窗口里的未保存内容";
        var agent = ThesisProjectStore.Open(vm.ProjectPath!);
        agent.Document.Content.Title = "外部变更";
        ThesisProjectStore.Save(agent);
        await Assert.ThrowsAsync<IOException>(() => vm.SaveAsync());
        await vm.SaveCopyAsync(FilePath("copy.json"));
        Assert.Equal("窗口里的未保存内容", vm.Body);
        Assert.Equal("外部变更", ThesisProjectStore.Open(FilePath("conflict.json")).Document.Content.Title);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task OutlineReorderAndRemovalKeepIdsAndCanRestoreCheckpoint()
    {
        var vm = new WorkspaceViewModel(directory);
        await vm.CreateAsync(FilePath("outline.json"));
        var first = vm.Selected!.Id;
        vm.Add(false);
        var second = vm.Selected!.Id;
        vm.Move(-1);
        Assert.Equal(second, vm.Outline.First(i => i.Kind == "chapter").Id);
        vm.DeleteSelected();
        Assert.DoesNotContain(vm.Outline, i => i.Id == second);
        vm.UndoStructure();
        Assert.Contains(vm.Outline, i => i.Id == first);
        Assert.Contains(vm.Outline, i => i.Id == second);
        await vm.SaveAsync();
    }

    [Fact]
    public async Task ImportPreservesNativeAssetsAndRejectsAccidentalPlainTextOverwrite()
    {
        var vm = new WorkspaceViewModel(directory);
        await vm.CreateAsync(FilePath("import.json"));
        var source = FilePath("manuscript.docx");
        Template(source);
        await vm.ImportAsync(source);
        vm.Selected = vm.Outline.First(i => i.Kind == "chapter");
        Assert.True(vm.BodyReadOnly);
        var before = vm.Body;
        vm.Body = "意外覆盖";
        Assert.Equal(before, vm.Body);
        await vm.SaveAsync();
        File.Delete(source);
        var project = ThesisProjectStore.Open(vm.ProjectPath!);
        Assert.All(project.Document.Content.Chapters.SelectMany(c => c.Blocks).Where(b => b.Type == "native"), b => Assert.True(File.Exists(b.SourcePath)));
    }

    [Fact]
    public void RepeatedCompositionKeepsReviewedSourceBytesAndChangedContentInvalidatesPlan()
    {
        var template = FilePath("template.docx"); Template(template);
        var project = ThesisProjectStore.Create(FilePath("project.json"), new()
        {
            TemplatePath = template,
            Content = new() { Title = "测试论文", Chapters = [new() { Title = "绪论", Paragraphs = ["这是用于验证稳定正文快照的内容。"] }] }
        });
        var first = ProjectCompositionService.Compose(project, FilePath("first.docx"), false);
        var second = ProjectCompositionService.Compose(project, FilePath("second.docx"), false);
        Assert.Equal(first.Audit.Source, second.Audit.Source);
        var plan = ThesisJson.Deserialize<TemplateApplicationPlan>(File.ReadAllText(first.DraftPlan));
        plan.SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(first.Audit.Source)));
        plan.TemplateSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(template)));
        var bound = FilePath("bound.json"); File.WriteAllText(bound, ThesisJson.Serialize(plan));
        project.Document.PlanPath = bound;
        ProjectCompositionService.Compose(project, FilePath("bound.docx"), false);
        project.Document.Content.Chapters[0].Paragraphs[0] = "正文已有新修改";
        var existing = FilePath("protected.docx"); File.WriteAllText(existing, "previous output");
        Assert.Throws<IOException>(() => ProjectCompositionService.Compose(project, existing, false));
        Assert.Equal("previous output", File.ReadAllText(existing));
    }

    [Fact]
    public void OutputCollisionAndCancellationPreserveProjectAndCandidate()
    {
        var template = FilePath("template.docx"); Template(template);
        var project = ThesisProjectStore.Create(FilePath("project.json"), new() { TemplatePath = template });
        Assert.Throws<ArgumentException>(() => ProjectCompositionService.Compose(project, project.Path, false));
        var output = FilePath("old.docx"); File.WriteAllText(output, "existing");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectCompositionService.Compose(project, output, false, cancellationToken: cancellation.Token));
        Assert.Equal("existing", File.ReadAllText(output));
    }

    [Fact]
    public void CliAndGuiShareProjectFormatAndUpdatesRequireCurrentFingerprint()
    {
        var path = FilePath("shared.json");
        using var writer = new StringWriter();
        Assert.Equal(0, ThesisCli.Run(["project", "create", "--out", path], writer, TextWriter.Null));
        var response = ThesisJson.Deserialize<CliResult>(writer.ToString());
        Assert.NotNull(response.ProjectFingerprint);
        var content = FilePath("new-content.json"); File.WriteAllText(content, ThesisJson.Serialize(new ThesisContent { Title = "Agent 写作" }));
        Assert.Equal(0, ThesisCli.Run(["project", "update", "--project", path, "--content", content, "--expected-sha256", response.ProjectFingerprint!], TextWriter.Null, TextWriter.Null));
        Assert.Equal("Agent 写作", ThesisProjectStore.Open(path).Document.Content.Title);
        Assert.Equal(1, ThesisCli.Run(["project", "update", "--project", path, "--content", content, "--expected-sha256", response.ProjectFingerprint!], TextWriter.Null, TextWriter.Null));
    }

    private static void Template(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles(
            new Style(new StyleName { Val = "Normal" }) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
            new Style(new StyleName { Val = "heading 1" }, new BasedOn { Val = "Normal" }, new StyleParagraphProperties(new OutlineLevel { Val = 0 })) { Type = StyleValues.Paragraph, StyleId = "Heading1" });
        main.Document = new Document(new Body(
            new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }, new OutlineLevel { Val = 0 }), new Run(new Text("1 绪论"))),
            new Paragraph(new Run(new Text("这是学校模板中的正文段落。用于验证中文论文的字体和章节格式。"))),
            new SectionProperties(new PageSize { Width = 11906, Height = 16838 }, new PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440 })));
    }
}
