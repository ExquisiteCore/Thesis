using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class ApplyTemplateCommand
{
    public static CliResult Execute(string[] args)
    {
        if (args is ["--help"] or ["-h"])
            return new CliResult { Status = "success", Diagnostics = [new Diagnostic { Severity = "info", Code = "usage", Message = "apply-template --doc source.docx --template template.docx --plan plan.json --out output.docx [--report report.json] [--render-config render.json --pdf output.pdf] [--workdir directory]. Without rendering, ready remains false." }] };
        try
        {
            var options = new CommandOptions(args, "--doc", "--template", "--plan", "--out", "--report", "--render-config", "--pdf", "--workdir");
            var source = options.Path("--doc");
            var template = options.Path("--template");
            var planPath = options.Path("--plan");
            var output = options.Path("--out");
            var reportPath = options.OptionalPath("--report");
            var pdfPath = options.OptionalPath("--pdf");
            var configPath = options.OptionalPath("--render-config");
            var plan = ThesisJson.Deserialize<TemplateApplicationPlan>(File.ReadAllText(planPath));
            foreach (var block in plan.Blocks ?? throw new ArgumentException("Plan blocks are required."))
                if (block.ImagePath is not null)
                    block.ImagePath = Path.GetFullPath(block.ImagePath, Path.GetDirectoryName(planPath)!);
            var inputs = new[] { source, template, planPath, configPath }.OfType<string>()
                .Concat(plan.Blocks.Select(b => b.ImagePath).OfType<string>()).ToArray();
            var outputs = new[] { output, reportPath, pdfPath }.OfType<string>().ToArray();
            if (outputs.Distinct(FileOutputTransaction.PathComparer).Count() != outputs.Length || outputs.Any(p => inputs.Contains(p, FileOutputTransaction.PathComparer)))
                throw new ArgumentException("Outputs must not overwrite any input or each other.");
            LayoutRenderConfig? config = null;
            if (configPath is not null)
            {
                config = ThesisJson.Deserialize<LayoutRenderConfig>(File.ReadAllText(configPath));
                config.LibreOfficePath = Path.GetFullPath(config.LibreOfficePath, Path.GetDirectoryName(configPath)!);
                config.PythonPath = Path.GetFullPath(config.PythonPath, Path.GetDirectoryName(configPath)!);
                if (!File.Exists(config.LibreOfficePath) || !File.Exists(config.PythonPath) || config.TimeoutSeconds is < 1 or > 600 || config.MaxPasses is < 2 or > 6)
                    throw new ArgumentException("Render config requires existing executables, timeoutSeconds 1–600 and maxPasses 2–6.");
            }
            if (pdfPath is not null && config is null) throw new ArgumentException("--pdf requires --render-config.");
            using var transaction = new FileOutputTransaction();
            var staged = transaction.Stage(output);
            var stagedReport = reportPath is null ? null : transaction.Stage(reportPath);
            var stagedPdf = pdfPath is null ? null : transaction.Stage(pdfPath);
            var work = Path.Combine(options.OptionalPath("--workdir") ?? Path.GetTempPath(), "thesis-render-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var report = TemplateRenderWorkflow.Execute(source, template, plan, staged, config, work, stagedPdf);
                report.Output = output;
                if (stagedReport is not null) File.WriteAllText(stagedReport, ThesisJson.Serialize(report));
                transaction.Commit();
                return new CliResult { Status = "success", Document = source, OutputPath = output,
                    Diagnostics = [new Diagnostic { Severity = "info", Code = "template_applied", Message = $"Assembly={report.AssemblyPassed}; structure={report.StructurePassed}; layout={report.LayoutPassed}; ready={report.Ready}. {report.CopiedSourceBlocks.Count} source blocks, {report.Tables} tables, {report.Drawings} drawings." + (options.Get("--workdir") is null ? "" : $" Work: {work}") }] };
            }
            finally
            {
                // Explicit workdirs retain render evidence; default scratch space is cleaned.
                if (options.Get("--workdir") is null)
                {
                    try { Directory.Delete(work, recursive: true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            return new CliResult { Status = "error", Diagnostics = [new Diagnostic { Severity = "error", Code = "template_application_failed", Message = ex.Message }] };
        }
    }
}
