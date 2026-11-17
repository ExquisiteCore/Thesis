using Thesis.Host;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Application;

public static class TemplateApplicationWorkflow
{
    public static TemplateApplicationReport Execute(string source, string template, TemplateApplicationPlan plan, string stagedDocument,
        LayoutRenderConfig? config, string workingDirectory, string? stagedPdf, CancellationToken cancellationToken = default)
    {
        var current = ThesisJson.Deserialize<TemplateApplicationPlan>(ThesisJson.Serialize(plan));
        var engine = config is null ? null : DocumentLayoutEngines.Create(config);
        TemplateApplicationReport report = null!;
        int? previousPageCount = null;
        Dictionary<string, string>? previousPages = null;
        for (var pass = 1; pass <= (config?.MaxPasses ?? 1); pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pass == 1 || !engine!.UpdatesDocument)
                report = OpenXmlTemplateApplicator.Apply(source, template, current, stagedDocument);
            TemplateStructureAudit.Check(stagedDocument, report);
            if (!report.StructurePassed) throw new InvalidDataException("Structure audit failed: " + string.Join("; ", report.StructuralErrors));
            report.RequirementResults = ContentRequirementAudit.Check(stagedDocument, current.Requirements);
            report.PendingReviews = [.. current.PendingReviews, .. report.ManualReviewItems];
            report.ContentPassed = report.RequirementResults.All(r => r.Passed);
            if (!report.ContentPassed) throw new InvalidDataException("Content requirements failed: " + string.Join("; ", report.RequirementResults.Where(r => !r.Passed).Select(r => r.Id + ": " + r.Message)));
            if (config is null)
            {
                report.ManualReviewItems.Add("Layout has not been rendered. Ready remains false.");
                return report;
            }
            var passDirectory = Path.Combine(workingDirectory, "pass-" + pass);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(passDirectory);
            var pdf = Path.Combine(passDirectory, "document.pdf");
            report.LayoutEngine = engine!.Name;
            report.HostApplication = engine.Render(stagedDocument, pdf, passDirectory, config);
            cancellationToken.ThrowIfCancellationRequested();
            // A renderer that saves the DOCX must pass exactly the same integrity/structure gates.
            TemplateStructureAudit.Check(stagedDocument, report);
            if (!report.StructurePassed) throw new InvalidDataException("Post-render structure audit failed: " + string.Join("; ", report.StructuralErrors));
            if (!File.Exists(pdf)) throw new IOException("Layout engine produced no PDF.");
            var manifest = Path.Combine(passDirectory, "manifest.json");
            File.WriteAllText(manifest, ThesisJson.Serialize(LayoutManifestBuilder.Build(stagedDocument, report.Headings, report.HostApplication)));
            var factsPath = Path.Combine(passDirectory, "layout.json");
            if (string.IsNullOrWhiteSpace(config.PythonPath))
                File.WriteAllText(factsPath, ThesisJson.Serialize(PdfLayoutAudit.Analyze(pdf, ThesisJson.Deserialize<PdfLayoutAudit.Manifest>(File.ReadAllText(manifest)), cancellationToken)));
            else
                ExternalProcessRunner.Run(config.PythonPath, [Path.Combine(AppContext.BaseDirectory, "scripts", "audit_layout.py"), pdf, manifest, factsPath], passDirectory, config.TimeoutSeconds);
            cancellationToken.ThrowIfCancellationRequested();
            var facts = ThesisJson.Deserialize<LayoutFacts>(File.ReadAllText(factsPath));
            report.LayoutExecuted = true;
            report.RenderPasses = pass;
            report.RenderedPages = facts.PageCount;
            report.RenderedTocPages = facts.TocPages;
            report.LayoutErrors = facts.Errors;
            report.ManualReviewItems = facts.ReviewItems;
            report.PendingReviews = [.. current.PendingReviews, .. facts.ReviewItems];
            if (facts.Errors.Count > 0) throw new InvalidDataException("Layout audit failed: " + string.Join("; ", facts.Errors));
            var expectedPages = engine.UpdatesDocument ? previousPages : current.TocPages;
            var pageCacheMatches = expectedPages is not null && facts.TocPages.Count == report.Headings.Count
                && facts.TocPages.All(pair => expectedPages.GetValueOrDefault(pair.Key) == pair.Value);
            if (pageCacheMatches && previousPageCount == facts.PageCount)
            {
                report.LayoutPassed = true;
                if (stagedPdf is not null) File.Copy(pdf, stagedPdf);
                return report;
            }
            current.TocPages = facts.TocPages;
            previousPages = facts.TocPages;
            previousPageCount = facts.PageCount;
        }
        throw new InvalidDataException($"Pagination did not stabilize within {config!.MaxPasses} passes.");
    }
}
