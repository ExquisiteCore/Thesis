using System.Collections.ObjectModel;
using Thesis.Application;
using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Gui.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public WorkspacePreferences Preferences { get; }
    private readonly string? userDataDirectory;
    public ObservableCollection<RecentProject> Recent { get; } = [];
    public ObservableCollection<ProjectHistoryEntry> Versions { get; } = [];
    public ObservableCollection<ProjectIssue> Issues { get; } = [];
    public TemplateDesign? Design { get; private set; }
    public bool HasRecovery => opened is not null && ProjectHistoryStore.HasRecovery(opened);
    public string KeywordsEnglish { get => string.Join("; ", opened?.Document.Content.KeywordsEn ?? []); set { if (!CanEdit || opened is null || KeywordsEnglish == value) return; opened.Document.Content.KeywordsEn = value.Split(['；', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); Changed(); } }
    public string? TemplatePath => opened is null ? null : ThesisProjectStore.Resolve(opened, opened.Document.TemplatePath);
    public Dictionary<string, string> CoverFields => opened?.Document.CoverFields ?? [];
    public string TotalCharacters => opened is null ? "0 字" : $"全文 {MarkdownProjectFormat.Serialize(opened.Document.Content).Count(c => !char.IsWhiteSpace(c)):N0} 字符";

    public WorkspaceViewModel(string? userDataDirectory = null)
    {
        this.userDataDirectory = userDataDirectory;
        Preferences = WorkspacePreferences.Load(userDataDirectory is null ? null : Path.Combine(userDataDirectory, "workspace.json"));
        ReloadRecent();
    }
    private void ReloadRecent() { Recent.Clear(); foreach (var item in Preferences.RecentProjects) Recent.Add(item); }

    public async Task UseDesignAsync(TemplateDesign design, bool saveToLibrary = false)
    {
        if (!CanEdit || opened is null) return;
        IsBusy = true;
        try
        {
            await AttachDesignAsync(design);
            if (saveToLibrary) await Task.Run(() => TemplateLibraryStore.Save(design, TemplateLibraryStore.DirectoryPath));
            await SaveAsync();
            Status = "模板设计已保存，后续生成使用这套规则。";
        }
        finally { IsBusy = false; }
    }
    private async Task AttachDesignAsync(TemplateDesign design)
    {
        if (opened is null) return;
        ProjectHistoryStore.Checkpoint(opened);
        var path = await Task.Run(() => TemplateLibraryStore.Save(design, Path.Combine(Path.GetDirectoryName(opened.Path)!, ".thesis", "templates"), immutable: true));
        Design = TemplateLibraryStore.Load(path);
        opened.Document.TemplateDesignPath = Path.GetRelativePath(Path.GetDirectoryName(opened.Path)!, path);
        opened.Document.TemplatePath = Path.GetRelativePath(Path.GetDirectoryName(opened.Path)!, Design.SourcePath);
        opened.Document.PlanPath = null;
        Changed(); NotifyAll();
    }
    public void SetCoverFields(Dictionary<string, string> fields)
    {
        if (!CanEdit || opened is null) return;
        opened.Document.CoverFields = fields; Changed();
    }
    public async Task ExportMarkdownAsync(string path)
    {
        if (opened is null) return;
        if (FileOutputTransaction.PathComparer.Equals(Path.GetFullPath(path), opened.Path)) throw new ArgumentException("Markdown 文件请使用独立路径。");
        foreach (var input in new[] { opened.Document.TemplatePath, opened.Document.TemplateDesignPath, opened.Document.PlanPath, opened.Document.RulesPath, opened.Document.RenderConfigPath })
            if (ThesisProjectStore.Resolve(opened, input) is { } resolved && FileOutputTransaction.PathComparer.Equals(Path.GetFullPath(path), resolved)) throw new ArgumentException("Markdown 输出路径与项目输入文件重复。");
        await SaveAsync();
        var snapshot = SnapshotContent();
        await Task.Run(() => MarkdownProjectFormat.Export(snapshot, path)); Status = "Markdown 与图片资源已导出。";
    }
    public async Task ExportPackageAsync(string path)
    {
        if (opened is null) return;
        await SaveAsync(); var project = opened with { Document = ThesisJson.Deserialize<ThesisProject>(ThesisJson.Serialize(opened.Document)) };
        await Task.Run(() => ProjectPackage.Export(project, path)); Status = "项目包已导出，包含正文、模板与素材。";
    }
    public ThesisContent SnapshotContent() => opened is null ? new() : ThesisJson.Deserialize<ThesisContent>(ThesisJson.Serialize(opened.Document.Content));
    public async Task<DocumentPreview> PreviewAsync(CancellationToken token, bool currentSection = false)
    {
        var content = SnapshotContent();
        var design = Design is null ? null : ThesisJson.Deserialize<TemplateDesign>(ThesisJson.Serialize(Design));
        if (currentSection && Selected is { ReadOnly: false, Kind: "chapter" or "section" } item)
            content = new() { Title = item.ReadTitle(), AssetDirectory = content.AssetDirectory, Chapters = [new() { Title = item.ReadTitle(), Markdown = item.ReadBody() }] };
        var chaptersOnly = currentSection && Selected is { Kind: "chapter" or "section" };
        return await Task.Run(() => DocumentPreviewService.Content(content, design, token, chaptersOnly), token);
    }
    public void RefreshHistory()
    {
        Versions.Clear();
        if (opened is not null) foreach (var entry in ProjectHistoryStore.List(opened)) Versions.Add(entry);
    }
    public string HistoryText(string path) => opened is null ? "" : MarkdownProjectFormat.Serialize(ProjectHistoryStore.Read(opened, path).Content);
    public string CurrentText => opened is null ? "" : MarkdownProjectFormat.Serialize(opened.Document.Content);
    public async Task RestoreAsync(string? path = null)
    {
        if (!CanEdit || opened is null) return;
        var restored = ProjectHistoryStore.Read(opened, path ?? ProjectHistoryStore.RecoveryPath(opened));
        var design = ThesisProjectStore.Resolve(opened, restored.TemplateDesignPath) is { } designPath ? TemplateLibraryStore.Load(designPath) : null;
        IsBusy = true;
        try
        {
            await SaveAsync();
            ProjectHistoryStore.Checkpoint(opened);
            restored.Revision = opened.Document.Revision;
            Remember(); opened = opened with { Document = restored }; Design = design;
            RebuildOutline(); Changed(); NotifyAll(); ContentReloaded?.Invoke(this, EventArgs.Empty);
            await SaveAsync(); Status = "历史内容已恢复；恢复前的内容也已保留快照。";
        }
        finally { IsBusy = false; }
    }
    public async Task ReloadExternalAsync()
    {
        if (opened is null || IsBusy || dirty) return;
        var observed = opened;
        var current = await Task.Run(() => ThesisProjectStore.Open(observed.Path));
        if (dirty || !ReferenceEquals(opened, observed)) return;
        if (current.Fingerprint == opened.Fingerprint) return;
        var id = Selected?.Id; Accept(current); Selected = Outline.FirstOrDefault(i => i.Id == id) ?? Selected;
        Status = "已载入 CLI / 其他程序保存的新版本。";
    }
    public void ConvertSelectionToMarkdown()
    {
        if (!CanEdit || opened is null || Selected is null) return;
        var chapter = opened.Document.Content.Chapters.FirstOrDefault(c => c.Id == Selected.Id);
        if (chapter is null) { Status = "请先选择整章进行 Markdown 转换。"; return; }
        ProjectHistoryStore.Checkpoint(opened); Remember();
        chapter.Markdown = EditableMarkdownBuilder.Convert(chapter.Paragraphs, chapter.Blocks, chapter.Tables);
        if (chapter.OrderedContentIncludesSections) chapter.Sections.Clear();
        chapter.OrderedContentIncludesSections = false;
        RebuildOutline(chapter.Id); Changed();
        Status = "本章已切换为 Markdown；thesis-native 代码块保留原稿复杂对象，请保持其中的路径与校验值。";
    }
    public async Task CheckAsync()
    {
        if (opened is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var snapshot = opened with { Document = ThesisJson.Deserialize<ThesisProject>(ThesisJson.Serialize(opened.Document)) };
            var issues = await Task.Run(() => ProjectChecks.Check(snapshot));
            Issues.Clear(); foreach (var issue in issues) Issues.Add(issue);
            Status = issues.Count == 0 ? "内容预检通过。正式交付仍需检查导出 PDF。" : $"预检发现 {issues.Count} 项，点击条目可定位内容。";
        }
        finally { IsBusy = false; }
    }
    public void Locate(ProjectIssue issue) { if (issue.NodeId is not null) Selected = Outline.FirstOrDefault(i => i.Id == issue.NodeId) ?? Selected; }
}
