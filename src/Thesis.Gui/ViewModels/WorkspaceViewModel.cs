using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Thesis.Application;
using Thesis.Schema;
using Thesis.Core;
using Thesis.OpenXml;

namespace Thesis.Gui.ViewModels;

public sealed class OutlineItem(string id, string label, string kind, Func<string> readTitle,
    Action<string> writeTitle, Func<string> readBody, Action<string> writeBody, bool readOnly = false,
    Func<List<ThesisContentBlock>>? readBlocks = null, Action<List<ThesisContentBlock>>? writeBlocks = null,
    Func<string?>? readMarkdown = null) : INotifyPropertyChanged
{
    private string labelText = label;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; } = id;
    public string Label { get => labelText; set { labelText = value; PropertyChanged?.Invoke(this, new(nameof(Label))); } }
    public string Kind { get; } = kind;
    public bool ReadOnly { get; } = readOnly;
    public Func<string> ReadTitle { get; } = readTitle;
    public Action<string> WriteTitle { get; } = writeTitle;
    public Func<string> ReadBody { get; } = readBody;
    public Action<string> WriteBody { get; } = writeBody;
    public Func<List<ThesisContentBlock>> ReadBlocks { get; } = readBlocks ?? (() => []);
    public Action<List<ThesisContentBlock>> WriteBlocks { get; } = writeBlocks ?? (_ => { });
    public Func<string?> ReadMarkdown { get; } = readMarkdown ?? (() => null);
}

/// <summary>UI state and editing intent; persistence and composition live in Application.</summary>
public sealed partial class WorkspaceViewModel : INotifyPropertyChanged
{
    private OpenThesisProject? opened;
    private OutlineItem? selected;
    private bool busy;
    private bool dirty;
    private string status = "新建论文项目，或打开已有项目开始写作。";
    private string errorMessage = "";
    private string errorDetails = "";
    private bool hasError;
    private CancellationTokenSource? job;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private long editGeneration;
    private readonly Stack<ThesisContent> history = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Edited;
    public event EventHandler? ContentReloaded;
    public ObservableCollection<OutlineItem> Outline { get; } = [];
    public ObservableCollection<string> Findings { get; } = [];
    public bool HasProject => opened is not null;
    public bool CanEdit => HasProject && !IsBusy;
    public bool IsIdle => !IsBusy;
    public bool CanUndo => CanEdit && history.Count > 0;
    public bool IsBusy { get => busy; private set { busy = value; NotifyAll(); } }
    public bool IsDirty => dirty;
    public string ProjectLabel => opened is null ? "尚未打开项目" : $"{Path.GetFileName(opened.Path)} · 版本 {opened.Document.Revision}{(dirty ? " · 待保存" : " · 已保存")}";
    public string Status { get => status; private set { status = value; Notify(); } }
    public string ErrorMessage { get => errorMessage; private set { errorMessage = value; Notify(); } }
    public string ErrorDetails { get => errorDetails; private set { errorDetails = value; Notify(); } }
    public bool HasError { get => hasError; set { hasError = value; Notify(); } }
    public string Title { get => opened?.Document.Content.Title ?? ""; set { if (!CanEdit || opened is null || Title == value) return; opened.Document.Content.Title = value; Changed(); } }
    public string Author { get => opened?.Document.Content.Author ?? ""; set { if (!CanEdit || opened is null || Author == value) return; opened.Document.Content.Author = value; Changed(); } }
    public string Keywords { get => string.Join("；", opened?.Document.Content.KeywordsZh ?? []); set { if (!CanEdit || opened is null || Keywords == value) return; opened.Document.Content.KeywordsZh = value.Split(['；', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); Changed(); } }
    public string TemplateLabel => Design?.Name ?? (Path.GetFileName(opened?.Document.TemplatePath) is { Length: > 0 } name ? name : "选择学校模板或合格论文");
    public string PlanLabel => Path.GetFileName(opened?.Document.PlanPath) is { Length: > 0 } name ? name : "首次生成将保留待校准方案";
    public string RenderLabel => Path.GetFileName(opened?.Document.RenderConfigPath) is { Length: > 0 } name ? name : "尚未配置真实分页检查";
    public string CharacterCount => $"当前 {Body.Count(c => !char.IsWhiteSpace(c)):N0} 字符";
    public string EditorHint => Selected?.ReadOnly == true
        ? "原稿中的图片、表格、公式和格式已保留。当前区域为原稿预览；新增章节可直接编辑。"
        : "正文以原生段落和行内格式保存；Markdown 可通过导入、导出与 Agent 工作流交换。";
    public bool EditorUsesMarkdown => Selected?.ReadMarkdown() is not null;
    public List<ThesisContentBlock> EditorBlocks => Selected?.Kind == "front"
        ? Body.Split('\n').Select(text => new ThesisContentBlock { Text = text }).ToList() : Selected?.ReadBlocks() ?? [];
    public bool BodyReadOnly => Selected is null || Selected.ReadOnly || IsBusy;
    public bool HeadingReadOnly => Selected is null || Selected.Kind == "front" || Selected.ReadOnly || IsBusy;
    public bool CanDelete => Selected?.Kind is "chapter" or "section" && !Selected.ReadOnly && CanEdit;
    public string Heading
    {
        get => Selected?.ReadTitle() ?? "";
        set
        {
            if (Selected is null || HeadingReadOnly || Heading == value) return;
            Selected.WriteTitle(value);
            Changed();
        }
    }
    public string Body
    {
        get => Selected?.ReadBody() ?? "";
        set
        {
            value = value.Replace("\r\n", "\n").Replace('\r', '\n');
            if (Selected is null || BodyReadOnly || Body == value) return;
            Selected.WriteBody(value);
            Changed();
            Notify(nameof(CharacterCount));
        }
    }
    public void SetEditorBlocks(List<ThesisContentBlock> blocks)
    {
        if (Selected is null || BodyReadOnly) return;
        if (Selected.Kind == "front") { Body = string.Join("\n", blocks.Select(ContentEditing.Text)); return; }
        if (Selected.ReadMarkdown() is null && ThesisJson.Serialize(Selected.ReadBlocks()) == ThesisJson.Serialize(blocks)) return;
        Selected.WriteBlocks(blocks);
        Changed();
        Notify(nameof(CharacterCount));
    }
    public OutlineItem? Selected
    {
        get => selected;
        set
        {
            selected = value;
            foreach (var property in new[] { nameof(Selected), nameof(Heading), nameof(Body), nameof(CharacterCount), nameof(EditorHint), nameof(BodyReadOnly), nameof(HeadingReadOnly), nameof(CanDelete) }) Notify(property);
        }
    }
    public string? DocumentPath { get; private set; }
    public string? PdfPath { get; private set; }
    public string? ProjectPath => opened?.Path;
    public LayoutRenderConfig? RenderSettings { get; private set; }

    public async Task CreateAsync(string path, string title = "未命名论文", string author = "", string? templatePath = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await SaveAsync();
            var design = templatePath is null
                ? await Task.Run(() => TemplateLibraryStore.Default(userDataDirectory))
                : await Task.Run(() => TemplateDesigner.Inspect(Path.GetFullPath(templatePath)));
            var project = new ThesisProject { Content = new() { Title = title, Author = author, Chapters = [new() { Title = "绪论" }] } };
            Accept(await Task.Run(() => ThesisProjectStore.Create(path, project)));
            await AttachDesignAsync(design, initializeOutline: templatePath is not null);
            await SaveAsync();
        }
        finally { IsBusy = false; }
    }

    public async Task OpenAsync(string path)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await SaveAsync(); Accept(await Task.Run(() => ThesisProjectStore.Open(path))); }
        finally { IsBusy = false; }
    }

    private void Accept(OpenThesisProject project)
    {
        opened = project;
        Design = null;
        string? templateError = null;
        try { Design = ThesisProjectStore.Resolve(project, project.Document.TemplateDesignPath) is { } designPath ? TemplateLibraryStore.Load(designPath) : null; }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Text.Json.JsonException) { templateError = "模板需要重新选择：" + ex.Message; }
        try { Preferences.Remember(project.Path, project.Document.Content.Title); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Optional recent-file state must not block opening a project. */ }
        ReloadRecent();
        dirty = false;
        history.Clear();
        RenderSettings = null;
        Findings.Clear();
        if (templateError is not null) Findings.Add(templateError);
        DocumentPath = PdfPath = null;
        RebuildOutline();
        NotifyAll();
        Status = "项目已打开。修改内容后将自动保存。";
        ContentReloaded?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync()
    {
        await saveGate.WaitAsync();
        try
        {
            if (opened is null || !dirty) return;
            var document = opened.Document;
            var snapshot = opened with { Document = ThesisJson.Deserialize<ThesisProject>(ThesisJson.Serialize(document)) };
            var generation = editGeneration;
            await Task.Run(() => ProjectHistoryStore.WriteRecovery(snapshot));
            var saved = await Task.Run(() => ThesisProjectStore.Save(snapshot));
            dirty = generation != editGeneration;
            document.Revision = saved.Document.Revision;
            opened = saved with { Document = document };
            if (!dirty) ProjectHistoryStore.ClearRecovery(opened);
            Notify(nameof(ProjectLabel));
            Notify(nameof(IsDirty));
            Status = dirty ? "仍有新修改，稍后继续保存。" : "已自动保存到本地项目。";
        }
        finally { saveGate.Release(); }
    }

    public async Task SaveCopyAsync(string path)
    {
        if (!CanEdit || opened is null) return;
        IsBusy = true;
        await saveGate.WaitAsync();
        try
        {
            var copy = ThesisJson.Deserialize<ThesisProject>(ThesisJson.Serialize(opened.Document));
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Revision = 0;
            copy.TemplatePath = ThesisProjectStore.Resolve(opened, copy.TemplatePath);
            copy.PlanPath = ThesisProjectStore.Resolve(opened, copy.PlanPath);
            copy.RulesPath = ThesisProjectStore.Resolve(opened, copy.RulesPath);
            copy.RenderConfigPath = ThesisProjectStore.Resolve(opened, copy.RenderConfigPath);
            copy.TemplateDesignPath = ThesisProjectStore.Resolve(opened, copy.TemplateDesignPath);
            Accept(await Task.Run(() => ThesisProjectStore.Create(path, copy)));
        }
        finally { saveGate.Release(); IsBusy = false; }
    }

    public async Task ImportAsync(string path)
    {
        if (!CanEdit || opened is null) return;
        IsBusy = true;
        try
        {
            await SaveAsync();
            ProjectHistoryStore.Checkpoint(opened);
            var imported = await Task.Run(() => ProjectContentImporter.Import(path, opened.Path));
            Remember();
            opened.Document.Content = imported.Content;
            // A source-bound assembly plan is invalid after replacement of the manuscript.
            opened.Document.PlanPath = null;
            Findings.Clear();
            if (imported.Report is not null)
                foreach (var finding in imported.Report.Findings) Findings.Add($"{finding.Code}：{finding.Message}");
            RebuildOutline();
            NotifyAll();
            Changed();
            ContentReloaded?.Invoke(this, EventArgs.Empty);
            Status = "正文已导入，原始复杂内容保存在项目的 assets 目录中。请检查大纲和识别结果。";
        }
        finally { IsBusy = false; }
    }

    public void SetFile(string kind, string path)
    {
        if (!CanEdit || opened is null) return;
        var relative = Path.GetRelativePath(Path.GetDirectoryName(opened.Path)!, path);
        switch (kind)
        {
            case "template": opened.Document.TemplatePath = relative; opened.Document.PlanPath = null; opened.Document.TemplateDesignPath = null; Design = null; break;
            case "plan": opened.Document.PlanPath = relative; break;
            case "render": opened.Document.RenderConfigPath = relative; break;
            case "rules": opened.Document.RulesPath = relative; break;
        }
        Changed();
        NotifyAll();
    }

    public void Add(bool section)
    {
        if (!CanEdit || opened is null) return;
        var chapter = opened.Document.Content.Chapters.FirstOrDefault(c => c.Id == Selected?.Id || c.Sections.Any(s => s.Id == Selected?.Id));
        string id;
        if (section && chapter is not null)
        {
            if (chapter.OrderedContentIncludesSections)
            {
                Status = "此章保留了原稿的完整顺序。请先新增一个可编辑章节，再添加小节。";
                return;
            }
            var item = new ThesisSectionContent { Title = "新小节" };
            Remember();
            chapter.Sections.Add(item);
            id = item.Id;
        }
        else
        {
            var item = new ThesisChapterContent { Title = "新章节" };
            Remember();
            opened.Document.Content.Chapters.Add(item);
            id = item.Id;
        }
        RebuildOutline(id);
        Changed();
    }

    public void Move(int delta)
    {
        if (!CanEdit || opened is null || Selected is null || Selected.ReadOnly) return;
        var chapters = opened.Document.Content.Chapters;
        var chapter = chapters.FirstOrDefault(c => c.Id == Selected.Id || c.Sections.Any(s => s.Id == Selected.Id));
        if (chapter is null) return;
        var id = Selected.Id;
        Remember();
        if (Selected.Kind == "chapter") Swap(chapters, chapters.IndexOf(chapter), delta);
        else Swap(chapter.Sections, chapter.Sections.FindIndex(s => s.Id == id), delta);
        RebuildOutline(id);
        Changed();
    }

    public void DeleteSelected()
    {
        if (opened is null || Selected is null || !CanDelete) return;
        var id = Selected.Id;
        Remember();
        opened.Document.Content.Chapters.RemoveAll(c => c.Id == id);
        foreach (var chapter in opened.Document.Content.Chapters) chapter.Sections.RemoveAll(s => s.Id == id);
        RebuildOutline();
        Changed();
    }

    public void RefreshLabels()
    {
        if (opened is null) return;
        var labels = opened.Document.Content.Chapters.SelectMany((c, i) =>
            new[] { (c.Id, Label: $"{i + 1}  {c.Title}") }.Concat(c.Sections.Select((s, j) => (s.Id, Label: $"    {i + 1}.{j + 1}  {s.Title}"))))
            .ToDictionary(pair => pair.Id, pair => pair.Label);
        foreach (var item in Outline)
            if (labels.TryGetValue(item.Id, out var label)) item.Label = label;
    }

    public void UndoStructure()
    {
        if (!CanUndo || opened is null) return;
        opened.Document.Content = history.Pop();
        opened.Document.PlanPath = null;
        RebuildOutline();
        Changed();
        NotifyAll();
    }

    public async Task ConfigureRenderAsync(LayoutRenderConfig settings)
    {
        if (!CanEdit || opened is null) return;
        var path = Path.Combine(Path.GetDirectoryName(opened.Path)!, ".thesis", opened.Document.Id, "render.json");
        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var lease = Thesis.Core.OutputPathLease.Acquire([path]);
                using var transaction = new Thesis.Core.FileOutputTransaction();
                File.WriteAllText(transaction.Stage(path), ThesisJson.Serialize(settings));
                transaction.Commit();
            });
            opened.Document.RenderConfigPath = Path.GetRelativePath(Path.GetDirectoryName(opened.Path)!, path);
            RenderSettings = settings;
            Changed();
            await SaveAsync();
        }
        finally { IsBusy = false; }
    }

    public async Task ComposeAsync(string output, bool render)
    {
        if (opened is null || IsBusy) return;
        IsBusy = true;
        Notify(nameof(BodyReadOnly));
        Notify(nameof(HeadingReadOnly));
        job = new CancellationTokenSource();
        try
        {
            await SaveAsync();
            DocumentPath = PdfPath = null;
            Findings.Clear();
            var result = await ProjectCompositionService.ComposeAsync(opened, output, render, progress: new Progress<string>(s => Status = s), cancellationToken: job.Token);
            DocumentPath = result.Document;
            PdfPath = result.Pdf;
            foreach (var message in result.Audit.StructuralErrors.Concat(result.Audit.LayoutErrors).Concat(result.Audit.PendingReviews).Concat(result.Audit.ManualReviewItems).Distinct()) Findings.Add(message);
            foreach (var requirement in result.Audit.RequirementResults.Where(r => !r.Passed)) Findings.Add(requirement.Message);
            Status = $"候选已生成{(result.Audit.RenderedPages is int pages ? $" · {pages} 页" : "")}。{(result.Audit.Ready ? "已通过交付检查。" : "请按检查结果完成模板校准与人工复核。")}";
            Findings.Add("套版方案：" + result.DraftPlan);
        }
        catch (OperationCanceledException) { Status = "任务已取消，已有成品已保留。"; }
        finally
        {
            job.Dispose(); job = null; IsBusy = false;
            Notify(nameof(BodyReadOnly)); Notify(nameof(HeadingReadOnly)); Notify(nameof(CanDelete));
        }
    }

    public void Cancel()
    {
        job?.Cancel();
        Status = "已请求取消；当前排版调用结束后停止，候选文件将保留在工作目录。";
    }

    public void ShowError(Exception error)
    {
        var message = error.GetBaseException().Message;
        ErrorMessage = message.Contains("Structure audit failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Post-render structure audit failed", StringComparison.OrdinalIgnoreCase)
                ? "Word 文档结构检查未通过，文件尚未交付。模板和论文项目都已保留；查看详情可定位具体问题。"
            : message.Contains("Content requirements failed", StringComparison.OrdinalIgnoreCase)
                ? "论文内容未满足模板中的一项或多项要求，请到“检查”页处理相关项目。"
            : message.Contains("Image file not found", StringComparison.OrdinalIgnoreCase)
                ? "正文引用的图片文件不存在，请在项目中重新插入或关联图片后再生成。"
            : "操作没有完成。查看技术详情可定位问题后重试。";
        ErrorDetails = error.ToString(); HasError = true;
        Status = "操作未完成，请查看页面上方提示。";
        Findings.Insert(0, ErrorMessage);
    }

    private void RebuildOutline(string? selectedId = null)
    {
        if (opened is null) return;
        var content = opened.Document.Content;
        Outline.Clear();
        Outline.Add(new("abstract.zh", "中文摘要", "front", () => "中文摘要", _ => { }, () => content.AbstractZh ?? "", t => content.AbstractZh = t));
        Outline.Add(new("abstract.en", "英文摘要", "front", () => "英文摘要", _ => { }, () => content.AbstractEn ?? "", t => content.AbstractEn = t));
        for (var i = 0; i < content.Chapters.Count; i++)
        {
            var chapter = content.Chapters[i];
            var protectedContent = chapter.OrderedContentIncludesSections || chapter.Blocks.Any(b => b.Type == "native");
            Outline.Add(new(chapter.Id, $"{i + 1}  {chapter.Title}", "chapter", () => chapter.Title, t => chapter.Title = t,
                () => chapter.Markdown ?? Read(chapter.Paragraphs, chapter.Blocks, chapter.Tables), t => chapter.Markdown = t, protectedContent,
                () => ContentEditing.Read(chapter.Paragraphs, chapter.Blocks, chapter.Tables), blocks => { chapter.Markdown = null; chapter.Paragraphs.Clear(); chapter.Tables.Clear(); chapter.Blocks = blocks; }, () => chapter.Markdown));
            for (var j = 0; j < chapter.Sections.Count; j++)
            {
                var section = chapter.Sections[j];
                Outline.Add(new(section.Id, $"    {i + 1}.{j + 1}  {section.Title}", "section", () => section.Title, t => section.Title = t,
                    () => section.Markdown ?? Read(section.Paragraphs, section.Blocks, section.Tables), t => section.Markdown = t,
                    protectedContent || section.Blocks.Any(b => b.Type == "native"),
                    () => ContentEditing.Read(section.Paragraphs, section.Blocks, section.Tables), blocks => { section.Markdown = null; section.Paragraphs.Clear(); section.Tables.Clear(); section.Blocks = blocks; }, () => section.Markdown));
            }
        }
        Outline.Add(new("references", "参考文献", "front", () => "参考文献", _ => { }, () => string.Join("\n", content.References), t => content.References = Lines(t)));
        Outline.Add(new("acknowledgements", "致谢", "front", () => "致谢", _ => { }, () => content.Acknowledgements ?? "", t => content.Acknowledgements = t));
        Selected = Outline.FirstOrDefault(i => i.Id == selectedId) ?? Outline.FirstOrDefault(i => i.Kind == "chapter") ?? Outline[0];
    }

    private static string Read(List<string> paragraphs, List<ThesisContentBlock> blocks, List<ThesisTableContent> tables) => MarkdownSupport.Body(paragraphs, blocks, tables);
    private static List<string> Lines(string text) => text.Split('\n').ToList();
    private static void Swap<T>(List<T> items, int index, int delta)
    {
        var target = index + delta;
        if (index >= 0 && target >= 0 && target < items.Count) (items[index], items[target]) = (items[target], items[index]);
    }
    private void Changed()
    {
        editGeneration++;
        dirty = true;
        DocumentPath = PdfPath = null;
        Notify(nameof(ProjectLabel)); Notify(nameof(IsDirty));
        Notify(nameof(CanUndo));
        foreach (var property in new[] { nameof(Body), nameof(Heading), nameof(Title), nameof(Author), nameof(Keywords), nameof(KeywordsEnglish), nameof(TotalCharacters) }) Notify(property);
        Edited?.Invoke(this, EventArgs.Empty);
    }
    private void Remember()
    {
        if (opened is not null)
        {
            if (history.Count >= 20)
            {
                var recent = history.Take(19).Reverse().ToArray(); history.Clear();
                foreach (var item in recent) history.Push(item);
            }
            history.Push(ThesisJson.Deserialize<ThesisContent>(ThesisJson.Serialize(opened.Document.Content)));
        }
    }
    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
