using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class ApplyTemplateCommand
{
    public static CliResult Execute(string[] args)
    {
        if (args is ["--help"] or ["-h"])
            return new CliResult { Status = "success", Diagnostics = [new Diagnostic { Severity = "info", Code = "usage", Message = "compose (--doc source.docx | --content content.json [--project-rules rules.json]) --template template.docx [--plan bound-plan.json] --out final.docx [--report report.json] [--render-config render.json --pdf final.pdf] [--workdir directory] [--require-ready true|false]. Omitted plans generate reviewable drafts. compose requires ready by default; apply-template supports previews." }] };
        try
        {
            var options = new CommandOptions(args, "--doc", "--content", "--project-rules", "--template", "--plan", "--out", "--report", "--render-config", "--pdf", "--workdir", "--require-ready");
            var requireReady = false;
            if (options.Get("--require-ready") is string ready && !bool.TryParse(ready, out requireReady))
                throw new ArgumentException("--require-ready must be true or false.");
            if ((options.Get("--doc") is null) == (options.Get("--content") is null)) throw new ArgumentException("Choose exactly one of --doc and --content.");
            var template = options.Path("--template");
            var contentPath = options.OptionalPath("--content");
            var rulesPath = options.OptionalPath("--project-rules");
            var preparation = options.OptionalPath("--workdir") ?? Path.Combine(Path.GetTempPath(), "thesis-input-" + Guid.NewGuid().ToString("N"));
            var source = options.OptionalPath("--doc") ?? Thesis.Application.CompositionSourceBuilder.FromContent(contentPath!, template, rulesPath, preparation);
            var planPath = options.OptionalPath("--plan");
            var output = options.Path("--out");
            var reportPath = options.OptionalPath("--report");
            var pdfPath = options.OptionalPath("--pdf");
            var configPath = options.OptionalPath("--render-config");
            var plan = planPath is null ? TemplatePlanDraftBuilder.Build(source, template) : ThesisJson.DeserializeStrict<TemplateApplicationPlan>(File.ReadAllText(planPath));
            if (planPath is null)
            {
                Directory.CreateDirectory(preparation);
                planPath = Path.Combine(preparation, "draft-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(planPath, ThesisJson.Serialize(plan));
            }
            foreach (var block in plan.Blocks ?? throw new ArgumentException("Plan blocks are required."))
            {
                if (block is null) throw new ArgumentException("Plan contains a null block.");
                if (block.ImagePath is not null) block.ImagePath = Path.GetFullPath(block.ImagePath, Path.GetDirectoryName(planPath)!);
            }
            var inputs = new[] { source, template, planPath, configPath, contentPath, rulesPath }.OfType<string>()
                .Concat(plan.Blocks.Select(b => b.ImagePath).OfType<string>()).ToArray();
            var outputs = new[] { output, reportPath, pdfPath }.OfType<string>().ToArray();
            if (outputs.Distinct(FileOutputTransaction.PathComparer).Count() != outputs.Length || outputs.Any(p => inputs.Contains(p, FileOutputTransaction.PathComparer)))
                throw new ArgumentException("Outputs must not overwrite any input or each other.");
            var config = configPath is null ? null : Thesis.Application.RenderConfiguration.Load(configPath);
            if (pdfPath is not null && config is null) throw new ArgumentException("--pdf requires --render-config.");
            var result = Thesis.Application.TemplateApplicationService.Execute(new(source, template, plan, output, reportPath, pdfPath,
                config, options.OptionalPath("--workdir"), requireReady));
            var report = result.Report;
            return new CliResult { Status = "success", Document = source, OutputPath = output, HostApplication = report.HostApplication,
                Diagnostics = [new Diagnostic { Severity = "info", Code = "template_applied",
                    Message = $"Assembly={report.AssemblyPassed}; structure={report.StructurePassed}; layout={report.LayoutPassed}; ready={report.Ready}. {report.CopiedSourceBlocks.Count} source blocks, {report.Tables} tables, {report.Drawings} drawings."
                        + (options.Get("--workdir") is null ? "" : $" Work: {result.WorkDirectory}") }] };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or Thesis.Host.HostApplicationException)
        {
            return new CliResult { Status = "error", Diagnostics = [new Diagnostic { Severity = "error", Code = "template_application_failed", Message = ex.Message }] };
        }
    }
}
