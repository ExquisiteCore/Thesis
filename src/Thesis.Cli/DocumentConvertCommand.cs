using Thesis.Application;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class DocumentConvertCommand
{
    public static CliResult Execute(string[] args)
    {
        var options = new CommandOptions(args, "--doc", "--out", "--host", "--prog-id");
        var source = options.Path("--doc");
        var output = options.Path("--out");
        if (!output.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Conversion output must be DOCX.");
        if (FileOutputTransaction.PathComparer.Equals(source, output)) throw new ArgumentException("Conversion output must differ from its input.");
        using var transaction = new FileOutputTransaction();
        var staged = transaction.Stage(output);
        var report = DocumentFinalizer.ConvertWithOffice(source, staged, options.Get("--host") ?? "auto", options.Get("--prog-id"));
        transaction.Commit();
        report.Document = output;
        return new CliResult { Status = "success", Document = source, OutputPath = output, HostApplication = report };
    }
}
