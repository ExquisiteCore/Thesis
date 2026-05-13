internal static partial class Program
{
    static void CliAssemblePreservesTemplatePackageAndWritesContent()
    {
        using var temp = new TempDirectory();
        var templatePath = Path.Combine(temp.Path, "template.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "final-rules.json");
        var outputPath = Path.Combine(temp.Path, "assembled.docx");

        WriteFixtureDocx(templatePath);
        var rules = new TemplateProfile
        {
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "title",
                    Format = new ParagraphFormatSample { StyleId = "Title", Alignment = "center" }
                },
                new ProfileStyleRole
                {
                    Role = "heading1",
                    Format = new ParagraphFormatSample { StyleId = "Heading1", Alignment = "center" }
                },
                new ProfileStyleRole
                {
                    Role = "body",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Normal",
                        FirstLineIndentTwips = 420,
                        LineSpacing = "360",
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "21", EastAsiaFont = "宋体" }
                    }
                }
            ],
            TablePolicy = new ProfileTablePolicy
            {
                Default = new ProfileTableSample
                {
                    Format = new TableFormatSample
                    {
                        WidthTwips = 8307,
                        WidthType = "dxa",
                        Alignment = "center",
                        HeaderRowCount = 1,
                        Borders = new TableBordersSample
                        {
                            Top = new TableBorderLineSample { Value = "single", Size = "12", Color = "000000" },
                            Bottom = new TableBorderLineSample { Value = "single", Size = "12", Color = "000000" },
                            Left = new TableBorderLineSample { Value = "nil" },
                            Right = new TableBorderLineSample { Value = "nil" },
                            InsideHorizontal = new TableBorderLineSample { Value = "single", Size = "4", Color = "000000" },
                            InsideVertical = new TableBorderLineSample { Value = "nil" }
                        }
                    }
                }
            }
        };
        File.WriteAllText(rulesPath, ThesisJson.Serialize(rules));
        File.WriteAllText(
            contentPath,
            """
            {
              "schemaVersion": "1.0",
              "documentKind": "thesisContent",
              "title": "论文题目",
              "abstractZh": "中文摘要正文。",
              "keywordsZh": ["模板", "装配"],
              "chapters": [
                {
                  "title": "第一章 绪论",
                  "paragraphs": ["第一段正文。"],
                  "tables": [
                    {
                      "caption": "表1-1 模块说明",
                      "headers": ["模块", "说明"],
                      "rows": [["装配", "保留模板"]]
                    }
                  ]
                }
              ],
              "references": ["[1] 张三. 模板论文研究[J]. 学术期刊, 2026."]
            }
            """);

        var (exitCode, result) = RunCli([
            "assemble",
            "--doc",
            templatePath,
            "--content",
            contentPath,
            "--profile",
            rulesPath,
            "--out",
            outputPath
        ]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(templatePath), result.Document);
        AssertEqual(Path.GetFullPath(outputPath), result.OutputPath);
        AssertEqual(true, OpenXmlDocumentInspector.TryInspect(outputPath, out var map, out var diagnostic));
        AssertEqual(null, diagnostic);

        AssertEqual(1, map!.Sections.Count);
        AssertEqual("rIdHeader1", map.Sections[0].Headers[0].RelationshipId);
        AssertEqual(1800, map.Sections[0].PageMargin!.LeftTwips);
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text == "论文题目"));
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text == "目录"));
        AssertEqual(null, map.Paragraphs.First(paragraph => paragraph.Text == "目录").StyleId);
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text == "第一章 绪论"));
        AssertEqual(false, map.Paragraphs.Any(paragraph => paragraph.Text == "第一章 第一章 绪论"));
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text == "第一段正文。"));
        AssertEqual(false, map.Paragraphs.Any(paragraph => paragraph.Text == "列表项"));
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text == "[1] 张三. 模板论文研究[J]. 学术期刊, 2026."));
        AssertEqual(false, map.Paragraphs.Any(paragraph => paragraph.Text == "[1] [1] 张三. 模板论文研究[J]. 学术期刊, 2026."));
        AssertEqual(1, map.Tables.Count);
        AssertEqual(2, map.Tables[0].RowCount);
        AssertEqual("single", map.Tables[0].Format.Borders!.Top!.Value);
        AssertEqual(true, map.RequiresFinalization);
        AssertEqual(true, map.FinalizationReasons.Contains("toc"));
    }

    static void CliAssembleRefusesUnsafeOutputPaths()
    {
        using var temp = new TempDirectory();
        var templatePath = Path.Combine(temp.Path, "template.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "final-rules.json");

        WriteFixtureDocx(templatePath);
        File.WriteAllText(contentPath, """{"documentKind":"thesisContent","title":"论文题目"}""");
        File.WriteAllText(rulesPath, ThesisJson.Serialize(new TemplateProfile()));

        foreach (var output in new[] { templatePath, contentPath, rulesPath })
        {
            var (exitCode, result) = RunCli([
                "assemble",
                "--doc",
                templatePath,
                "--content",
                contentPath,
                "--profile",
                rulesPath,
                "--out",
                output
            ]);

            AssertEqual(1, exitCode);
            AssertEqual("error", result.Status);
            AssertEqual("assemble_output_refused", result.Diagnostics[0].Code);
        }
    }
}
