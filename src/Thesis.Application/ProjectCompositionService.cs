using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Application;

public sealed record ProjectCompositionResult(string Document, string? Pdf, string Report, string DraftPlan,
    TemplateApplicationReport Audit);

public static class ProjectCompositionService
{
    public static Task<ProjectCompositionResult> ComposeAsync(OpenThesisProject project, string output,
        bool render, bool requireReady = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        // Capture an immutable revision before crossing onto a background thread.
        var snapshot = project with { Document = ThesisJson.Deserialize<ThesisProject>(ThesisJson.Serialize(project.Document)) };
        return Task.Run(() => Compose(snapshot, output, render, requireReady, progress, cancellationToken), cancellationToken);
    }

    public static ProjectCompositionResult Compose(OpenThesisProject project, string output, bool render,
        bool requireReady = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        project = project with { Document = ThesisJson.Deserialize<ThesisProject>(ThesisJson.Serialize(project.Document)) };
        ThesisProjectStore.Validate(project.Document);
        var template = ThesisProjectStore.Resolve(project, project.Document.TemplatePath)
            ?? throw new InvalidDataException("请先选择学校模板或合格论文。");
        var rules = ThesisProjectStore.Resolve(project, project.Document.RulesPath);
        var planPath = ThesisProjectStore.Resolve(project, project.Document.PlanPath);
        var configPath = ThesisProjectStore.Resolve(project, project.Document.RenderConfigPath);
        var config = render ? configPath is null ? new LayoutRenderConfig { Engine = "auto" }
            : RenderConfiguration.Load(configPath) : null;
        var designPath = ThesisProjectStore.Resolve(project, project.Document.TemplateDesignPath);
        var design = designPath is null ? null : TemplateLibraryStore.Load(designPath);
        output = Path.GetFullPath(output);
        var report = Path.ChangeExtension(output, ".report.json");
        var draft = Path.ChangeExtension(output, ".plan.json");
        var pdf = render ? Path.ChangeExtension(output, ".pdf") : null;
        var destinations = new[] { output, report, draft, pdf }.OfType<string>().ToArray();
        var inputs = new[] { project.Path, template, rules, planPath, configPath, designPath, design?.SourcePath }.OfType<string>().Select(Path.GetFullPath).ToArray();
        if (destinations.Distinct(FileOutputTransaction.PathComparer).Count() != destinations.Length
            || destinations.Any(p => inputs.Contains(p, FileOutputTransaction.PathComparer)))
            throw new ArgumentException("输出路径应与项目、模板和配置文件各不相同。");
        ContentPaths.Resolve(project.Document.Content, project.Path);
        foreach (var destination in destinations) ContentPaths.ProtectAssets(project.Document.Content, destination);
        var work = Path.Combine(Path.GetDirectoryName(project.Path)!, ".thesis", "jobs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        if (design is not null)
        {
            template = TemplateLibraryStore.Materialize(design, Path.Combine(Path.GetDirectoryName(project.Path)!, ".thesis", "templates", "compiled"));
        }
        if (destinations.Contains(template, FileOutputTransaction.PathComparer))
            throw new ArgumentException("输出路径应与编译后的模板文件不同。");
        progress?.Report("正在整理正文与模板…");
        var source = ProjectSourceCache.Prepare(project.Document.Content, template, rules, project.Path, work);
        cancellationToken.ThrowIfCancellationRequested();
        var plan = planPath is null ? TemplatePlanDraftBuilder.Build(source, template)
            : ThesisJson.DeserializeStrict<TemplateApplicationPlan>(File.ReadAllText(planPath));
        if (design is not null && planPath is null) TemplateDesigner.Apply(design, plan);
        foreach (var (field, value) in project.Document.CoverFields) plan.TemplateTextReplacements[field] = value;
        plan.TemplateTextReplacements["{{题目}}"] = project.Document.Content.Title;
        plan.TemplateTextReplacements["{{作者}}"] = project.Document.Content.Author ?? "";
        foreach (var block in plan.Blocks)
            if (block.ImagePath is not null) block.ImagePath = Path.GetFullPath(block.ImagePath, Path.GetDirectoryName(planPath ?? project.Path)!);
        // Preserve the generated plan as evidence; never silently mark a draft as reviewed.
        var jobPlan = Path.Combine(work, "plan.json");
        File.WriteAllText(jobPlan, ThesisJson.Serialize(plan));
        progress?.Report(render ? "正在套版、更新目录并检查真实页面…" : "正在套用模板并检查文档结构…");
        var result = TemplateApplicationService.Execute(new(source, template, plan, output, report, pdf, config, work, requireReady, draft), cancellationToken);
        return new(output, pdf, report, draft, result.Report);
    }
}
