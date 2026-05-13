internal static partial class Program
{
    static void CliRehearsalCompareReportsCandidateGaps()
    {
        using var temp = new TempDirectory();
        var candidate = Path.Combine(temp.Path, "candidate.docx");
        var reference = Path.Combine(temp.Path, "reference.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");

        WriteSimpleDocx(
            candidate,
            """
            <w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:r><w:t>候选正文</w:t></w:r></w:p>
            """);
        WriteSimpleDocx(
            reference,
            """
            <w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:r><w:t>候选正文</w:t></w:r></w:p>
            <w:p><w:r><w:t>第二章 系统设计</w:t></w:r></w:p>
            <w:tbl>
              <w:tr><w:tc><w:p><w:r><w:t>A</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            """);
        var profile = TemplateProfileBuilder.Build(OpenXmlDocumentInspector.Inspect(reference), "doc");
        File.WriteAllText(profilePath, ThesisJson.Serialize(profile));

        var (exitCode, result) = RunCli([
            "rehearsal",
            "compare",
            "--candidate",
            candidate,
            "--reference",
            reference,
            "--profile",
            profilePath
        ]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(candidate), result.Document);
        AssertEqual(true, result.RehearsalComparison is not null);
        AssertEqual(Path.GetFullPath(reference), result.RehearsalComparison!.ReferenceDocument);
        AssertEqual(2, result.RehearsalComparison.Candidate.ParagraphCount);
        AssertEqual(3, result.RehearsalComparison.Reference.ParagraphCount);
        AssertEqual(1, result.RehearsalComparison.Reference.TableCount);
        AssertEqual(false, result.RehearsalComparison.ReadyForFinalReview);
        AssertEqual(true, result.RehearsalComparison.Diagnostics.Any(diagnostic => diagnostic.Code == "paragraph_count_gap"));
        AssertEqual(true, result.RehearsalComparison.Diagnostics.Any(diagnostic => diagnostic.Code == "table_count_gap"));
        AssertEqual(true, result.RehearsalComparison.Diagnostics.Any(diagnostic => diagnostic.Code == "missing_reference_heading"));
        AssertEqual(true, result.RehearsalComparison.Validation is not null);
    }

    static void CliRehearsalCompareWritesReportJson()
    {
        using var temp = new TempDirectory();
        var candidate = Path.Combine(temp.Path, "candidate.docx");
        var reference = Path.Combine(temp.Path, "reference.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");
        var output = Path.Combine(temp.Path, "report.json");

        WriteSimpleDocx(candidate, """<w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>""");
        WriteSimpleDocx(reference, """<w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>""");
        File.WriteAllText(profilePath, "{}");

        var (exitCode, result) = RunCli([
            "rehearsal",
            "compare",
            "--candidate",
            candidate,
            "--reference",
            reference,
            "--profile",
            profilePath,
            "--out",
            output
        ]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(output), result.OutputPath);
        AssertEqual(true, File.Exists(output));
        var report = ThesisJson.Deserialize<RehearsalComparisonReport>(File.ReadAllText(output));
        AssertEqual(Path.GetFullPath(candidate), report.CandidateDocument);
        AssertEqual(Path.GetFullPath(reference), report.ReferenceDocument);
    }

    static void CliRehearsalCompareRefusesOverwritingInputs()
    {
        using var temp = new TempDirectory();
        var candidate = Path.Combine(temp.Path, "candidate.docx");
        var reference = Path.Combine(temp.Path, "reference.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");

        WriteSimpleDocx(candidate, """<w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>""");
        WriteSimpleDocx(reference, """<w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>""");
        File.WriteAllText(profilePath, "{}");

        foreach (var output in new[] { candidate, reference, profilePath })
        {
            var (exitCode, result) = RunCli([
                "rehearsal",
                "compare",
                "--candidate",
                candidate,
                "--reference",
                reference,
                "--profile",
                profilePath,
                "--out",
                output
            ]);

            AssertEqual(1, exitCode);
            AssertEqual("error", result.Status);
            AssertEqual("rehearsal_output_refused", result.Diagnostics[0].Code);
        }
    }

    static void CliRehearsalCompareNormalizesTocPageNumbersAndRepeatedHeadingPrefixes()
    {
        using var temp = new TempDirectory();
        var candidate = Path.Combine(temp.Path, "candidate.docx");
        var reference = Path.Combine(temp.Path, "reference.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");

        WriteSimpleDocx(
            candidate,
            """
            <w:p><w:r><w:t>第一章 第一章 绪论</w:t></w:r></w:p>
            <w:p><w:r><w:t>1.1 1.1 研究背景与意义</w:t></w:r></w:p>
            """);
        WriteSimpleDocx(
            reference,
            """
            <w:p><w:r><w:t>第一章 绪论1</w:t></w:r></w:p>
            <w:p><w:r><w:t>1.1 研究背景与意义2</w:t></w:r></w:p>
            """);
        File.WriteAllText(profilePath, "{}");

        var (exitCode, result) = RunCli([
            "rehearsal",
            "compare",
            "--candidate",
            candidate,
            "--reference",
            reference,
            "--profile",
            profilePath
        ]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.RehearsalComparison is not null);
        AssertEqual(2, result.RehearsalComparison!.ContentCoverage.ReferenceHeadingCount);
        AssertEqual(2, result.RehearsalComparison.ContentCoverage.MatchedHeadingCount);
        AssertEqual(1d, result.RehearsalComparison.ContentCoverage.HeadingCoverage);
        AssertEqual(false, result.RehearsalComparison.Diagnostics.Any(diagnostic => diagnostic.Code == "missing_reference_heading"));
    }

    static void CliRehearsalCompareDoesNotTreatChapterSentencesAsHeadings()
    {
        using var temp = new TempDirectory();
        var candidate = Path.Combine(temp.Path, "candidate.docx");
        var reference = Path.Combine(temp.Path, "reference.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");

        WriteSimpleDocx(
            candidate,
            """
            <w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:r><w:t>第一章介绍了研究背景和主要工作。</w:t></w:r></w:p>
            <w:p><w:r><w:t>第二章 工业控制系统（ICS）安全</w:t></w:r></w:p>
            """);
        WriteSimpleDocx(
            reference,
            """
            <w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:r><w:t>第二章 工业控制系统（ICS）安全</w:t></w:r></w:p>
            """);
        File.WriteAllText(profilePath, "{}");

        var (exitCode, result) = RunCli([
            "rehearsal",
            "compare",
            "--candidate",
            candidate,
            "--reference",
            reference,
            "--profile",
            profilePath
        ]);

        AssertEqual(0, exitCode);
        AssertEqual(true, result.RehearsalComparison is not null);
        AssertEqual(false, result.RehearsalComparison!.Candidate.Headings.Contains("第一章介绍了研究背景和主要工作。"));
        AssertEqual(true, result.RehearsalComparison.Candidate.Headings.Contains("第二章 工业控制系统（ICS）安全"));
        AssertEqual(2, result.RehearsalComparison.ContentCoverage.ReferenceHeadingCount);
        AssertEqual(2, result.RehearsalComparison.ContentCoverage.MatchedHeadingCount);
    }
}
