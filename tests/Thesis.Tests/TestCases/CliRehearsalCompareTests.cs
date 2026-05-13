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
            <w:p><w:r><w:t>系统设计正文缺失。</w:t></w:r></w:p>
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
        AssertEqual(4, result.RehearsalComparison.Reference.ParagraphCount);
        AssertEqual(1, result.RehearsalComparison.Reference.TableCount);
        AssertEqual(false, result.RehearsalComparison.ReadyForFinalReview);
        AssertEqual(true, result.RehearsalComparison.Diagnostics.Any(diagnostic => diagnostic.Code == "paragraph_count_gap"));
        AssertEqual(true, result.RehearsalComparison.Diagnostics.Any(diagnostic => diagnostic.Code == "table_count_gap"));
        AssertEqual(true, result.RehearsalComparison.Diagnostics.Any(diagnostic => diagnostic.Code == "missing_reference_heading"));
        AssertEqual(true, result.RehearsalComparison.Validation is not null);

        AssertEqual(1, result.RehearsalComparison.ContentCoverage.MissingReferenceParagraphCount);
        AssertEqual(1, result.RehearsalComparison.ContentCoverage.MissingReferenceTableCount);
        AssertEqual(2, result.RehearsalComparison.ContentCoverage.Gaps.Count);
        var paragraphGap = result.RehearsalComparison.ContentCoverage.Gaps.First(gap => gap.GapType == "paragraph");
        AssertEqual(3, paragraphGap.ReferenceIndex);
        AssertEqual("第二章 系统设计", paragraphGap.ReferenceContext);
        AssertEqual("系统设计正文缺失。", paragraphGap.ReferenceTextPreview);
        var tableGap = result.RehearsalComparison.ContentCoverage.Gaps.First(gap => gap.GapType == "table");
        AssertEqual(0, tableGap.ReferenceIndex);
        AssertEqual("第二章 系统设计", tableGap.ReferenceContext);
        AssertEqual("A", tableGap.ReferenceTextPreview);
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

    static void CliRehearsalCompareIgnoresFieldCodesAndTocLinesWhenFindingContentGaps()
    {
        using var temp = new TempDirectory();
        var candidate = Path.Combine(temp.Path, "candidate.docx");
        var reference = Path.Combine(temp.Path, "reference.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");

        WriteSimpleDocx(
            candidate,
            """
            <w:p><w:r><w:t>摘要</w:t></w:r></w:p>
            <w:p><w:r><w:t>Abstract</w:t></w:r></w:p>
            <w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:r><w:t>研究内容[1]。</w:t></w:r></w:p>
            """);
        WriteSimpleDocx(
            reference,
            """
            <w:p><w:r><w:t>目    录</w:t></w:r></w:p>
            <w:p><w:r><w:t>TOC \o &quot;1-3&quot; \h \z \u 摘   要I</w:t></w:r></w:p>
            <w:p><w:r><w:t>AbstractII</w:t></w:r></w:p>
            <w:p><w:r><w:t>第一章 绪论1</w:t></w:r></w:p>
            <w:p><w:r><w:t>研究内容 REF BibRef_1 \r \h \* MERGEFORMAT [1]。</w:t></w:r></w:p>
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
        AssertEqual(0, result.RehearsalComparison!.ContentCoverage.MissingReferenceParagraphCount);
        AssertEqual(false, result.RehearsalComparison.ContentCoverage.Gaps.Any(gap => gap.GapType == "paragraph"));
    }

    static void CliRehearsalCompareScopesContentGapsToThesisBody()
    {
        using var temp = new TempDirectory();
        var candidate = Path.Combine(temp.Path, "candidate.docx");
        var reference = Path.Combine(temp.Path, "reference.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");

        WriteSimpleDocx(
            candidate,
            """
            <w:p><w:r><w:t>摘要</w:t></w:r></w:p>
            <w:p><w:r><w:t>本文研究工业控制系统安全防护。</w:t></w:r></w:p>
            <w:tbl>
              <w:tr><w:tc><w:p><w:r><w:t>正文表格</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            """);
        WriteSimpleDocx(
            reference,
            """
            <w:p><w:r><w:t>题 目：工业控制系统安全防护方案</w:t></w:r></w:p>
            <w:tbl>
              <w:tr><w:tc><w:p><w:r><w:t>学生情况</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            <w:p><w:r><w:t>学院：计算机学院</w:t></w:r></w:p>
            <w:p><w:r><w:t>摘要</w:t></w:r></w:p>
            <w:p><w:r><w:t>本文研究工业控制系统安全防护。</w:t></w:r></w:p>
            <w:tbl>
              <w:tr><w:tc><w:p><w:r><w:t>正文表格</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
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
        AssertEqual(0, result.RehearsalComparison!.ContentCoverage.MissingReferenceParagraphCount);
        AssertEqual(0, result.RehearsalComparison.ContentCoverage.MissingReferenceTableCount);
        AssertEqual(0, result.RehearsalComparison.ContentCoverage.Gaps.Count);
    }

    static void CliRehearsalCompareKeepsEnglishBodyParagraphsEndingWithDigits()
    {
        using var temp = new TempDirectory();
        var candidate = Path.Combine(temp.Path, "candidate.docx");
        var reference = Path.Combine(temp.Path, "reference.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");

        WriteSimpleDocx(
            candidate,
            """
            <w:p><w:r><w:t>Abstract</w:t></w:r></w:p>
            <w:p><w:r><w:t>The candidate discusses network safety.</w:t></w:r></w:p>
            """);
        WriteSimpleDocx(
            reference,
            """
            <w:p><w:r><w:t>Abstract</w:t></w:r></w:p>
            <w:p><w:r><w:t>The Modbus TCP service listens on port 502</w:t></w:r></w:p>
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
        AssertEqual(1, result.RehearsalComparison!.ContentCoverage.MissingReferenceParagraphCount);
        var paragraphGap = result.RehearsalComparison.ContentCoverage.Gaps.First(gap => gap.GapType == "paragraph");
        AssertEqual("The Modbus TCP service listens on port 502", paragraphGap.ReferenceTextPreview);
    }
}
