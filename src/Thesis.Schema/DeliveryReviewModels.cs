namespace Thesis.Schema;

public sealed class DeliveryReview
{
    public string DocumentSha256 { get; set; } = "";
    public string PdfSha256 { get; set; } = "";
    public string ReportSha256 { get; set; } = "";
    public string Reviewer { get; set; } = "";
    public List<string> CompletedChecks { get; set; } = [];
    public string Notes { get; set; } = "";
}
