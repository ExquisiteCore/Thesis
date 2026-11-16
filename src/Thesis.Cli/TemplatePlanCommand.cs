using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class TemplatePlanCommand
{
    public static CliResult Execute(string[] args)
    {
        if (args is ["--help"] or ["-h"])
            return new CliResult { Status = "success", Diagnostics = [new Diagnostic { Severity = "info", Code = "usage", Message = "template-plan bind|inspect --doc source.docx --template template.docx --plan plan.json --out result.json" }] };
        try { return Run(args); }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new CliResult { Status = "error", Diagnostics = [new Diagnostic { Severity = "error", Code = "template_plan_invalid", Message = ex.Message }] };
        }
    }

    private static CliResult Run(string[] args)
    {
        if (args is not ["bind" or "inspect", ..]) throw new ArgumentException("template-plan bind|inspect --doc source.docx --template template.docx --plan plan.json --out result.json");
        var options = new CommandOptions(args[1..], "--doc", "--template", "--plan", "--out");
        var source = options.Path("--doc");
        var template = options.Path("--template");
        var planPath = options.Path("--plan");
        var output = options.Path("--out");
        var plan = ThesisJson.Deserialize<TemplateApplicationPlan>(File.ReadAllText(planPath));
        // Inspect resolves image paths on a copy; bind keeps the reusable plan's relative paths.
        var checkable = ThesisJson.Deserialize<TemplateApplicationPlan>(ThesisJson.Serialize(plan));
        foreach (var block in checkable.Blocks)
            if (block.ImagePath is not null) block.ImagePath = Path.GetFullPath(block.ImagePath, Path.GetDirectoryName(planPath)!);
        var (resolved, report) = TemplatePlanPreflight.Check(source, template, checkable, rejectUnsupported: false);
        if (new[] { source, template, planPath }.Concat(checkable.Blocks.Select(b => b.ImagePath).OfType<string>()).Contains(output, FileOutputTransaction.PathComparer))
            throw new ArgumentException("Plan output must not overwrite inputs.");
        if (args[0] == "bind")
        {
            if (report.UnsupportedSourceBlocks.Any(index => resolved.Blocks.Any(block => block.Type == "sourceRange" && index >= block.Start && index < block.End)))
                throw new InvalidDataException("The plan includes source blocks requiring an explicit migration mapping; inspect the preflight report before binding.");
            report.Warnings.RemoveAll(w => w.StartsWith("Legacy unpinned", StringComparison.Ordinal));
            plan.SourceSha256 = report.SourceSha256;
            plan.TemplateSha256 = report.TemplateSha256;
            // Rebase external image paths when writing the plan into another directory.
            for (var i = 0; i < plan.Blocks.Count; i++)
                if (checkable.Blocks[i].ImagePath is string image) plan.Blocks[i].ImagePath = Path.GetRelativePath(Path.GetDirectoryName(output)!, image);
        }
        using var transaction = new FileOutputTransaction();
        File.WriteAllText(transaction.Stage(output), args[0] == "bind" ? ThesisJson.Serialize(plan) : ThesisJson.Serialize(report));
        transaction.Commit();
        return new CliResult { Status = "success", OutputPath = output, Diagnostics = report.Warnings.Select(w => new Diagnostic { Severity = "warning", Code = "plan_review", Message = w }).ToList() };
    }
}
