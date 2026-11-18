using Thesis.Application;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class ProjectCommand
{
    public static CliResult Execute(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            return new() { Status = "success", Diagnostics = [new() { Severity = "info", Code = "usage", Message =
                "project create --out paper.thesis.json [--content content.json|source.docx|draft.md] [--template template.docx]; project inspect --project paper.thesis.json; project update --project paper.thesis.json --expected-sha256 HASH --content content.json|draft.md; project repair-outline --project paper.thesis.json --out repaired.thesis.json; project export-markdown --project paper.thesis.json --out paper.md; project pack --project paper.thesis.json --out paper.zip; project compose --project paper.thesis.json --out candidate.docx [--render true|false] [--require-ready true|false]. Composition requires ready by default; use --require-ready false for review candidates." }] };
        var options = new CommandOptions(args[1..], "--project", "--out", "--content", "--template", "--expected-sha256", "--render", "--require-ready");
        if (args[0] == "create")
        {
            var path = options.Path("--out");
            var content = options.OptionalPath("--content");
            var template = options.OptionalPath("--template");
            if (new[] { content, template }.OfType<string>().Any(p => Thesis.Core.FileOutputTransaction.PathComparer.Equals(p, path)))
                throw new ArgumentException("Project output must differ from input paths.");
            var project = new ThesisProject { TemplatePath = template };
            if (content is not null) project.Content = ProjectContentImporter.Import(content, path).Content;
            return Describe(ThesisProjectStore.Create(path, project));
        }
        var opened = ThesisProjectStore.Open(options.Path("--project"));
        switch (args[0])
        {
            case "inspect": return Describe(opened);
            case "repair-outline":
                var repairedPath = options.Path("--out");
                if (File.Exists(repairedPath)) throw new IOException("大纲修复副本应保存到一个新文件。");
                var repaired = ThesisJson.Deserialize<ThesisProject>(ThesisJson.Serialize(opened.Document));
                repaired.TemplatePath = ThesisProjectStore.Resolve(opened, repaired.TemplatePath)
                    ?? throw new InvalidDataException("请先选择论文模板。");
                repaired.Content = TemplateOutlineBuilder.Rebuild(repaired.Content, repaired.TemplatePath);
                foreach (var destination in new[] { repairedPath }) Thesis.Core.ContentPaths.ProtectAssets(repaired.Content, destination);
                repaired.TemplateDesignPath = ThesisProjectStore.Resolve(opened, repaired.TemplateDesignPath);
                repaired.RulesPath = ThesisProjectStore.Resolve(opened, repaired.RulesPath);
                repaired.RenderConfigPath = ThesisProjectStore.Resolve(opened, repaired.RenderConfigPath);
                repaired.PlanPath = null;
                repaired.Id = Guid.NewGuid().ToString("N"); repaired.Revision = 0;
                return Describe(ThesisProjectStore.Create(repairedPath, repaired));
            case "export-markdown":
                var markdown = options.Path("--out");
                if (Thesis.Core.FileOutputTransaction.PathComparer.Equals(markdown, opened.Path)) throw new ArgumentException("Markdown output must differ from the project path.");
                foreach (var input in new[] { opened.Document.TemplatePath, opened.Document.TemplateDesignPath, opened.Document.PlanPath, opened.Document.RulesPath, opened.Document.RenderConfigPath })
                    if (ThesisProjectStore.Resolve(opened, input) is { } resolved && Thesis.Core.FileOutputTransaction.PathComparer.Equals(markdown, resolved)) throw new ArgumentException("Markdown output must differ from project inputs.");
                MarkdownProjectFormat.Export(opened.Document.Content, markdown);
                return new() { Status = "success", Document = opened.Path, OutputPath = markdown };
            case "pack":
                var archive = options.Path("--out"); ProjectPackage.Export(opened, archive);
                return new() { Status = "success", Document = opened.Path, OutputPath = archive };
            case "update":
                if (!string.Equals(options.Get("--expected-sha256"), opened.Fingerprint, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Project fingerprint changed; inspect and merge the current revision before updating.");
                opened.Document.Content = ProjectContentImporter.Import(options.Path("--content"), opened.Path).Content;
                // A content replacement invalidates source-bound review mappings.
                opened.Document.PlanPath = null;
                if (options.OptionalPath("--template") is { } selectedTemplate) { opened.Document.TemplatePath = selectedTemplate; opened.Document.TemplateDesignPath = null; }
                return Describe(ThesisProjectStore.Save(opened));
            case "compose":
                var result = ProjectCompositionService.Compose(opened, options.Path("--out"), Flag(options, "--render", false), Flag(options, "--require-ready", true));
                return new() { Status = "success", Document = opened.Path, OutputPath = result.Document,
                    Diagnostics = [new() { Severity = "info", Code = "project_composed", Message = $"Ready={result.Audit.Ready}; report={result.Report}; plan={result.DraftPlan}" }] };
            default: throw new ArgumentException("Unknown project command. See project --help.");
        }
    }

    private static bool Flag(CommandOptions options, string name, bool fallback) => options.Get(name) is not { } value ? fallback
        : bool.TryParse(value, out var result) ? result : throw new ArgumentException(name + " must be true or false.");

    private static CliResult Describe(OpenThesisProject project) => new()
    {
        Status = "success", Document = project.Path, Project = project.Document, ProjectFingerprint = project.Fingerprint
    };
}
