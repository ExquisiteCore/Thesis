using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Application;

public sealed record TemplateApplicationRequest(string Source, string Template, TemplateApplicationPlan Plan, string Output,
    string? Report, string? Pdf, LayoutRenderConfig? RenderConfig, string? WorkDirectory, bool RequireReady, string? PlanOutput = null);
public sealed record TemplateApplicationResult(TemplateApplicationReport Report, string WorkDirectory);

/// <summary>One delivery transaction owns assembly, rendering, audits and promotion.</summary>
public static class TemplateApplicationService
{
    public static TemplateApplicationResult Execute(TemplateApplicationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destinations = new[] { request.Output, request.Pdf, request.Report, request.PlanOutput }.OfType<string>().Select(Path.GetFullPath).ToArray();
        var inputs = new[] { request.Source, request.Template }.Concat(request.Plan.Blocks.Select(b => b.ImagePath).OfType<string>()).Select(Path.GetFullPath).ToArray();
        if (destinations.Distinct(FileOutputTransaction.PathComparer).Count() != destinations.Length
            || destinations.Any(p => inputs.Contains(p, FileOutputTransaction.PathComparer)))
            throw new ArgumentException("Delivery outputs must differ from source, template and each other.");
        if (request.Pdf is not null && request.RenderConfig is null) throw new ArgumentException("PDF output requires a render configuration.");
        var work = Path.Combine(request.WorkDirectory ?? Path.GetTempPath(), "thesis-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var succeeded = false;
        try
        {
            using var outputLock = OutputPathLease.Acquire(destinations);
            using var transaction = new FileOutputTransaction();
            var staged = transaction.Stage(request.Output);
            var stagedReport = request.Report is null ? null : transaction.Stage(request.Report);
            var stagedPdf = request.Pdf is null ? null : transaction.Stage(request.Pdf);
            var stagedPlan = request.PlanOutput is null ? null : transaction.Stage(request.PlanOutput);
            try
            {
                var report = TemplateApplicationWorkflow.Execute(request.Source, request.Template, request.Plan, staged, request.RenderConfig, work, stagedPdf, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                report.Output = request.Output;
                if (report.HostApplication is not null) report.HostApplication.Document = request.Output;
                File.WriteAllText(Path.Combine(work, "report.json"), ThesisJson.Serialize(report));
                if (request.RequireReady && !report.Ready) throw new InvalidDataException("Candidate did not pass delivery gates.");
                if (stagedReport is not null) File.WriteAllText(stagedReport, ThesisJson.Serialize(report));
                if (stagedPlan is not null) File.WriteAllText(stagedPlan, ThesisJson.Serialize(request.Plan));
                cancellationToken.ThrowIfCancellationRequested();
                transaction.Commit();
                succeeded = true;
                return new(report, work);
            }
            finally
            {
                if (!succeeded && File.Exists(staged)) File.Copy(staged, Path.Combine(work, "candidate.docx"), overwrite: true);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(work, "failure.json"), ThesisJson.Serialize(new { Error = ex.Message, Detail = ex.ToString() }));
            throw new IOException($"{ex.Message} Evidence: {work}", ex);
        }
        finally
        {
            if (succeeded && request.WorkDirectory is null)
            {
                try { Directory.Delete(work, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
