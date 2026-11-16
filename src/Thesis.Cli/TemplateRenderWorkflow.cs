using Thesis.Host;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class TemplateRenderWorkflow
{
    public static TemplateApplicationReport Execute(string source, string template, TemplateApplicationPlan plan, string stagedDocument,
        LayoutRenderConfig? config, string workingDirectory, string? stagedPdf)
    {
        var current = ThesisJson.Deserialize<TemplateApplicationPlan>(ThesisJson.Serialize(plan));
        TemplateApplicationReport report = null!;
        int? previousPageCount = null;
        for (var pass = 1; pass <= (config?.MaxPasses ?? 1); pass++)
        {
            report = OpenXmlTemplateApplicator.Apply(source, template, current, stagedDocument);
            TemplateStructureAudit.Check(stagedDocument, report);
            if (!report.StructurePassed) throw new InvalidDataException("Structure audit failed: " + string.Join("; ", report.StructuralErrors));
            if (config is null)
            {
                report.ManualReviewItems.Add("Layout has not been rendered. Ready remains false.");
                return report;
            }
            var passDirectory = Path.Combine(workingDirectory, "pass-" + pass);
            Directory.CreateDirectory(passDirectory);
            var input = Path.Combine(passDirectory, "document.docx");
            File.Copy(stagedDocument, input);
            var profileUri = new Uri(Path.Combine(passDirectory, "lo-profile")).AbsoluteUri;
            ExternalProcessRunner.Run(config.LibreOfficePath,
                ["-env:UserInstallation=" + profileUri, "--headless", "--convert-to", "pdf:writer_pdf_Export", "--outdir", passDirectory, input], passDirectory, config.TimeoutSeconds);
            var pdf = Path.Combine(passDirectory, "document.pdf");
            if (!File.Exists(pdf)) throw new IOException("Layout engine produced no PDF.");
            var manifest = Path.Combine(passDirectory, "manifest.json");
            File.WriteAllText(manifest, ThesisJson.Serialize(LayoutManifestBuilder.Build(stagedDocument, report.Headings)));
            var factsPath = Path.Combine(passDirectory, "layout.json");
            ExternalProcessRunner.Run(config.PythonPath, [Path.Combine(AppContext.BaseDirectory, "scripts", "audit_layout.py"), pdf, manifest, factsPath], passDirectory, config.TimeoutSeconds);
            var facts = ThesisJson.Deserialize<LayoutFacts>(File.ReadAllText(factsPath));
            report.LayoutExecuted = true;
            report.RenderPasses = pass;
            report.RenderedPages = facts.PageCount;
            report.RenderedTocPages = facts.TocPages;
            report.LayoutErrors = facts.Errors;
            report.ManualReviewItems = facts.ReviewItems;
            if (facts.Errors.Count > 0) throw new InvalidDataException("Layout audit failed: " + string.Join("; ", facts.Errors));
            var pageCacheMatches = facts.TocPages.Count == report.Headings.Distinct().Count()
                && facts.TocPages.All(pair => current.TocPages.GetValueOrDefault(pair.Key) == pair.Value);
            if (pageCacheMatches && previousPageCount == facts.PageCount)
            {
                report.LayoutPassed = true;
                if (stagedPdf is not null) File.Copy(pdf, stagedPdf);
                return report;
            }
            current.TocPages = facts.TocPages;
            previousPageCount = facts.PageCount;
        }
        throw new InvalidDataException($"Pagination did not stabilize within {config!.MaxPasses} passes.");
    }
}
