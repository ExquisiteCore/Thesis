using Thesis.OpenXml;

internal static partial class Program
{
    static void CliApplyRunsRequestAndExportsDocxWithoutManualSession()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var profile = Path.Combine(temp.Path, "profile.json");
        var request = Path.Combine(temp.Path, "request.json");
        var output = Path.Combine(temp.Path, "out.docx");
        WriteFixtureDocx(docx);
        File.WriteAllText(profile, "{}");
        File.WriteAllText(
            request,
            """
            {
              "requestId": "one-shot",
              "operations": [
                {
                  "id": "replace-abstract",
                  "op": "replaceParagraphText",
                  "target": { "type": "paragraphText", "text": "中文摘要", "match": "exact" },
                  "text": "新的中文摘要"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["apply", "--doc", docx, "--profile", profile, "--request", request, "--out", output]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("one-shot", result.RequestId);
        AssertEqual(Path.GetFullPath(docx), result.Document);
        AssertEqual(Path.GetFullPath(output), result.OutputPath);
        AssertEqual(true, File.Exists(output));
        AssertEqual("applied", result.Operations[0].Status);
        AssertEqual("中文摘要", OpenXmlDocumentInspector.Inspect(docx).Paragraphs[0].Text);
        AssertEqual("新的中文摘要", OpenXmlDocumentInspector.Inspect(output).Paragraphs[0].Text);
    }

    static void CliApplyValidatesOptionsAndDoesNotOverwriteSource()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var profile = Path.Combine(temp.Path, "profile.json");
        var request = Path.Combine(temp.Path, "request.json");
        WriteFixtureDocx(docx);
        File.WriteAllText(profile, "{}");
        File.WriteAllText(request, """{"operations":[]}""");

        var missing = RunCli(["apply", "--doc", docx, "--profile", profile, "--request", request]);
        AssertEqual(1, missing.ExitCode);
        AssertEqual("missing_option", missing.Result.Diagnostics[0].Code);

        var overwrite = RunCli(["apply", "--doc", docx, "--profile", profile, "--request", request, "--out", docx]);
        AssertEqual(1, overwrite.ExitCode);
        AssertEqual("apply_output_refused", overwrite.Result.Diagnostics[0].Code);
        AssertEqual("中文摘要", OpenXmlDocumentInspector.Inspect(docx).Paragraphs[0].Text);
    }

    static void CliValidateSupportsDirectDocAndProfile()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");
        WriteFormattedFixtureDocx(docx);
        var profile = TemplateProfileBuilder.Build(OpenXmlDocumentInspector.Inspect(docx), "doc");
        File.WriteAllText(profilePath, ThesisJson.Serialize(profile));

        var (exitCode, result) = RunCli(["validate", "--doc", docx, "--profile", profilePath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(docx), result.Document);
        AssertEqual(true, result.Validation is not null);
        AssertEqual(true, result.Validation!.CheckedParagraphs > 0);
        AssertEqual(true, result.Validation.CheckedTables > 0);
    }

    static void CliProfileExtractReturnsExplanationSummary()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var outputPath = Path.Combine(temp.Path, "profile.json");
        WriteFixtureDocx(docx);

        var (exitCode, result) = RunCli(["profile", "extract", "--doc", docx, "--out", outputPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(outputPath), result.ProfileExplanation!.ProfilePath);
        AssertEqual(true, result.ProfileExplanation.RoleSummaries.Any(role => role.Role == "abstract.zh"));
        AssertEqual(true, result.ProfileExplanation.SourceEvidence.ParagraphCount > 0);
    }
}
