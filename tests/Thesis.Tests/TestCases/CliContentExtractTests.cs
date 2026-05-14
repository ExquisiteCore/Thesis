internal static partial class Program
{
    static void CliContentExtractWritesThesisContentAndReport()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");
        var reportPath = Path.Combine(temp.Path, "content-extract-report.json");

        WriteSimpleDocx(
            docx,
            """
            <w:p><w:pPr><w:pStyle w:val="Title"/></w:pPr><w:r><w:t>工业控制系统安全防护方案设计与验证</w:t></w:r></w:p>
            <w:p><w:r><w:t>摘要</w:t></w:r></w:p>
            <w:p><w:r><w:t>本文围绕工业控制系统安全防护展开研究。</w:t></w:r></w:p>
            <w:p><w:r><w:t>关键词：工业控制系统；安全防护；入侵检测</w:t></w:r></w:p>
            <w:p><w:r><w:t>Abstract</w:t></w:r></w:p>
            <w:p><w:r><w:t>This thesis studies ICS security protection.</w:t></w:r></w:p>
            <w:p><w:r><w:t>Keywords: ICS; security; detection</w:t></w:r></w:p>
            <w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:r><w:t>绪论正文 REF BibRef_1 \r \h \* MERGEFORMAT [1]。</w:t></w:r></w:p>
            <w:p><w:r><w:t>1.1 研究背景</w:t></w:r></w:p>
            <w:p><w:r><w:t>研究背景正文。</w:t></w:r></w:p>
            <w:p><w:r><w:t>表1-1 模块说明</w:t></w:r></w:p>
            <w:tbl>
              <w:tr><w:tc><w:p><w:r><w:t>模块</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>说明</w:t></w:r></w:p></w:tc></w:tr>
              <w:tr><w:tc><w:p><w:r><w:t>采集</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>抓取网络流量</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            <w:p><w:r><w:t>参考文献</w:t></w:r></w:p>
            <w:p><w:r><w:t>[1] 张三. 工控安全研究[J]. 学术期刊, 2026.</w:t></w:r></w:p>
            """);

        var (exitCode, result) = RunCli([
            "content",
            "extract",
            "--doc",
            docx,
            "--out",
            contentPath,
            "--report",
            reportPath
        ]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(docx), result.Document);
        AssertEqual(Path.GetFullPath(contentPath), result.OutputPath);
        AssertEqual(true, File.Exists(contentPath));
        AssertEqual(true, File.Exists(reportPath));

        var content = ThesisJson.Deserialize<ThesisContent>(File.ReadAllText(contentPath));
        AssertEqual("thesisContent", content.DocumentKind);
        AssertEqual("工业控制系统安全防护方案设计与验证", content.Title);
        AssertEqual("本文围绕工业控制系统安全防护展开研究。", content.AbstractZh);
        AssertEqual(3, content.KeywordsZh.Count);
        AssertEqual("This thesis studies ICS security protection.", content.AbstractEn);
        AssertEqual(3, content.KeywordsEn.Count);
        AssertEqual(1, content.Chapters.Count);
        AssertEqual("第一章 绪论", content.Chapters[0].Title);
        AssertEqual("绪论正文。", content.Chapters[0].Paragraphs[0]);
        AssertEqual(1, content.Chapters[0].Sections.Count);
        AssertEqual("1.1 研究背景", content.Chapters[0].Sections[0].Title);
        AssertEqual("研究背景正文。", content.Chapters[0].Sections[0].Paragraphs[0]);
        AssertEqual(1, content.Chapters[0].Sections[0].Tables.Count);
        AssertEqual("表1-1 模块说明", content.Chapters[0].Sections[0].Tables[0].Caption);
        AssertEqual("模块", content.Chapters[0].Sections[0].Tables[0].Headers[0]);
        AssertEqual("抓取网络流量", content.Chapters[0].Sections[0].Tables[0].Rows[0][1]);
        AssertEqual(1, content.References.Count);
        AssertEqual("[1] 张三. 工控安全研究[J]. 学术期刊, 2026.", content.References[0]);

        var reportJson = File.ReadAllText(reportPath);
        AssertContains(reportJson, "\"extractKind\":\"contentExtract\"");
        AssertContains(reportJson, "\"ready\":true");
        AssertContains(reportJson, "\"chapterCount\":1");
        AssertContains(reportJson, "\"tableCount\":1");
    }

    static void CliContentExtractUsesCoverTitleBeforeSchoolName()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");

        WriteSimpleDocx(
            docx,
            """
            <w:p><w:r><w:t>北京信息科技大学</w:t></w:r></w:p>
            <w:p><w:r><w:t>毕 业 设 计（论 文）</w:t></w:r></w:p>
            <w:p><w:r><w:t>题    目：工业控制系统（ICS）安全防护方案</w:t></w:r></w:p>
            <w:p><w:r><w:t>设计与验证</w:t></w:r></w:p>
            <w:p><w:r><w:t>学    院：计算机学院</w:t></w:r></w:p>
            <w:p><w:r><w:t>学生姓名：陶与柯    班级/学号    信安2201 / 2022010082</w:t></w:r></w:p>
            <w:p><w:r><w:t>摘   要</w:t></w:r></w:p>
            <w:p><w:r><w:t>摘要正文。</w:t></w:r></w:p>
            <w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:r><w:t>绪论正文。</w:t></w:r></w:p>
            """);

        var (exitCode, result) = RunCli([
            "content",
            "extract",
            "--doc",
            docx,
            "--out",
            contentPath
        ]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        var content = ThesisJson.Deserialize<ThesisContent>(File.ReadAllText(contentPath));
        AssertEqual("工业控制系统（ICS）安全防护方案设计与验证", content.Title);
        AssertEqual("陶与柯", content.Author);
    }

    static void CliContentExtractRefusesUnsafeOutputPaths()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");
        var reportPath = Path.Combine(temp.Path, "report.json");

        WriteSimpleDocx(docx, """<w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>""");
        File.WriteAllText(profilePath, """{"profileKind":"templateProfile"}""");
        var before = File.ReadAllBytes(docx);

        var (exitCode, result) = RunCli([
            "content",
            "extract",
            "--doc",
            docx,
            "--out",
            docx,
            "--report",
            reportPath
        ]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("content_output_refused", result.Diagnostics[0].Code);
        AssertBytesEqual(before, File.ReadAllBytes(docx));
        AssertEqual(false, File.Exists(reportPath));

        var profileBefore = File.ReadAllText(profilePath);
        var profileOverwrite = RunCli([
            "content",
            "extract",
            "--doc",
            docx,
            "--out",
            profilePath,
            "--profile",
            profilePath
        ]);

        AssertEqual(1, profileOverwrite.ExitCode);
        AssertEqual("error", profileOverwrite.Result.Status);
        AssertEqual("content_output_refused", profileOverwrite.Result.Diagnostics[0].Code);
        AssertEqual(profileBefore, File.ReadAllText(profilePath));
    }
}
