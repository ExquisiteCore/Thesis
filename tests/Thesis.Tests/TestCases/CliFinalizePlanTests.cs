internal static partial class Program
{
    static void CliFinalizePlanReportsHostApplicationStepsForFields()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        WriteFixtureDocx(docx);

        var (exitCode, output) = RunCliRaw(["finalize", "plan", "--doc", docx]);
        var result = ThesisJson.Deserialize<CliResult>(output);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(docx), result.Document);
        AssertEqual(true, result.FinalizationPlan!.Required);
        AssertEqual(true, result.FinalizationPlan.Reasons.Contains("fields", StringComparer.Ordinal));
        AssertEqual(true, result.FinalizationPlan.Steps.Any(step =>
            step.Id == "updateFields" && step.Capability == "hostApplication"));
        AssertEqual(true, result.FinalizationPlan.Steps.Any(step =>
            step.Id == "updateTableOfContents" && step.Capability == "hostApplication"));
        AssertEqual(true, result.FinalizationPlan.Steps.Any(step =>
            step.Id == "repaginate" && step.Capability == "hostApplication"));
        AssertEqual(true, result.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "finalization_requires_host_application"));
        AssertContains(output, "\"finalizationPlan\"");
        AssertContains(output, "\"capability\":\"hostApplication\"");
    }

    static void CliFinalizePlanDistinguishesGenericFieldsFromToc()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "date-field.docx");
        WriteSimpleDocx(
            docx,
            """
            <w:p><w:r><w:t>封面日期：</w:t></w:r><w:fldSimple w:instr="DATE \@ &quot;yyyy-MM-dd&quot;"><w:r><w:t>2026-05-11</w:t></w:r></w:fldSimple></w:p>
            """);

        var (exitCode, result) = RunCli(["finalize", "plan", "--doc", docx]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.FinalizationPlan!.Required);
        AssertEqual(true, result.FinalizationPlan.Reasons.Contains("fields", StringComparer.Ordinal));
        AssertEqual(false, result.FinalizationPlan.Reasons.Contains("toc", StringComparer.Ordinal));
        AssertEqual(true, result.FinalizationPlan.Steps.Any(step => step.Id == "updateFields"));
        AssertEqual(false, result.FinalizationPlan.Steps.Any(step => step.Id == "updateTableOfContents"));
    }

    static void CliFinalizePlanIsQuietForCleanDocuments()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "clean.docx");
        WriteSimpleDocx(docx, """<w:p><w:r><w:t>普通正文</w:t></w:r></w:p>""");

        var (exitCode, result) = RunCli(["finalize", "plan", "--doc", docx]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(false, result.FinalizationPlan!.Required);
        AssertEqual(0, result.FinalizationPlan.Reasons.Count);
        AssertEqual(false, result.FinalizationPlan.Steps.Any(step => step.Required));
        AssertEqual(0, result.Diagnostics.Count);
    }

}
