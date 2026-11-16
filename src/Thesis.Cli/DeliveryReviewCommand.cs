using Thesis.Application;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class DeliveryReviewCommand
{
    public static CliResult Execute(string[] args)
    {
        var options = new CommandOptions(args, "--doc", "--pdf", "--report", "--review", "--out", "--out-pdf", "--out-report");
        var review = ThesisJson.DeserializeStrict<DeliveryReview>(File.ReadAllText(options.Path("--review")));
        if (new[] { options.Path("--out"), options.Path("--out-pdf"), options.Path("--out-report") }.Contains(options.Path("--review"), Thesis.Core.FileOutputTransaction.PathComparer))
            throw new ArgumentException("Delivery outputs must preserve the review input.");
        DeliveryReviewService.Approve(options.Path("--doc"), options.Path("--pdf"), options.Path("--report"), review,
            options.Path("--out"), options.Path("--out-pdf"), options.Path("--out-report"));
        return new CliResult { Status = "success", OutputPath = options.Path("--out") };
    }
}
