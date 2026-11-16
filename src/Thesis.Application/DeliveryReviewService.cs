using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Application;

public static class DeliveryReviewService
{
    public static void Approve(string document, string pdf, string reportPath, DeliveryReview review, string output, string outputPdf, string outputReport)
    {
        var inputs = new[] { document, pdf, reportPath }.Select(Path.GetFullPath).ToArray();
        var outputs = new[] { output, outputPdf, outputReport }.Select(Path.GetFullPath).ToArray();
        if (outputs.Distinct(FileOutputTransaction.PathComparer).Count() != 3 || outputs.Any(p => inputs.Contains(p, FileOutputTransaction.PathComparer)))
            throw new ArgumentException("Delivery outputs must differ from all inputs and each other.");
        using var outputLock = OutputPathLease.Acquire(outputs);
        using var transaction = new FileOutputTransaction();
        var stagedDocument = transaction.Stage(output);
        var stagedPdf = transaction.Stage(outputPdf);
        var stagedReport = transaction.Stage(outputReport);
        File.Copy(document, stagedDocument);
        File.Copy(pdf, stagedPdf);
        File.Copy(reportPath, stagedReport);
        if (!Matches(stagedDocument, review.DocumentSha256) || !Matches(stagedPdf, review.PdfSha256) || !Matches(stagedReport, review.ReportSha256))
            throw new InvalidDataException("Review fingerprints do not match the exact candidate DOCX, PDF and report.");
        var report = ThesisJson.Deserialize<TemplateApplicationReport>(File.ReadAllText(stagedReport));
        if (!report.AutomatedChecksPassed) throw new InvalidDataException("Automated checks must pass before delivery review.");
        if (string.IsNullOrWhiteSpace(review.Reviewer) || string.IsNullOrWhiteSpace(review.Notes) || review.CompletedChecks is null
            || report.PendingReviews.Any(item => !review.CompletedChecks.Contains(item, StringComparer.Ordinal)))
            throw new InvalidDataException("Review needs a reviewer, notes and every pending check completed.");
        if (DocumentIntegrityAudit.Check(stagedDocument).Count > 0) throw new InvalidDataException("Candidate integrity check failed.");
        report.PendingReviews.Clear();
        report.Review = review;
        report.Output = output;
        if (report.HostApplication is not null) report.HostApplication.Document = output;
        File.WriteAllText(stagedReport, ThesisJson.Serialize(report));
        transaction.Commit();
    }

    private static bool Matches(string path, string expected) => TemplatePlanPreflight.Fingerprint(path).Equals(expected, StringComparison.OrdinalIgnoreCase);
}
