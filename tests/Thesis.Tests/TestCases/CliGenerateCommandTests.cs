internal static partial class Program
{
    static void CliGenerateCreatesThesisDocxFromContentAndFinalRules()
    {
        using var temp = new TempDirectory();
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "final-rules.json");
        var outputPath = Path.Combine(temp.Path, "thesis.docx");

        var rules = new TemplateProfile
        {
            SourceType = "merged",
            SourceDocument = "template.docx",
            PageSetup = new ProfilePageSetup
            {
                PageSize = new PageSizeInfo { WidthTwips = 11906, HeightTwips = 16838 },
                Margins = new PageMarginInfo { TopTwips = 1440, RightTwips = 1701, BottomTwips = 1440, LeftTwips = 1701 }
            },
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "title",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Title",
                        Alignment = "center",
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "32", EastAsiaFont = "黑体" }
                    }
                },
                new ProfileStyleRole
                {
                    Role = "heading1",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Heading1",
                        Alignment = "center",
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "28", EastAsiaFont = "黑体" }
                    }
                },
                new ProfileStyleRole
                {
                    Role = "heading2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Heading2",
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "24", EastAsiaFont = "黑体" }
                    }
                },
                new ProfileStyleRole
                {
                    Role = "body",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Normal",
                        FirstLineIndentTwips = 480,
                        LineSpacing = "360",
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "24", EastAsiaFont = "宋体" }
                    }
                },
                new ProfileStyleRole
                {
                    Role = "references",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Heading1",
                        Alignment = "center",
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "28", EastAsiaFont = "黑体" }
                    }
                }
            ],
            TablePolicy = new ProfileTablePolicy
            {
                Detected = true,
                TableCount = 1,
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
              "author": "学生姓名",
              "abstractZh": "中文摘要正文。",
              "keywordsZh": ["论文", "格式"],
              "abstractEn": "English abstract body.",
              "keywordsEn": ["thesis", "format"],
              "chapters": [
                {
                  "title": "绪论",
                  "paragraphs": ["第一段正文。"],
                  "sections": [
                    {
                      "title": "研究背景",
                      "paragraphs": ["小节正文。"],
                      "tables": [
                        {
                          "caption": "表1-1 系统模块",
                          "headers": ["模块", "说明"],
                          "rows": [["生成", "生成论文"], ["检查", "检查格式"]]
                        }
                      ]
                    }
                  ]
                }
              ],
              "references": ["张三. 论文格式研究[J]. 学术期刊, 2024."],
              "acknowledgements": "感谢老师指导。"
            }
            """);

        var (exitCode, result) = RunCli(["generate", "--content", contentPath, "--rules", rulesPath, "--out", outputPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(outputPath), result.OutputPath);
        AssertEqual(true, File.Exists(outputPath));
        AssertEqual(true, OpenXmlDocumentInspector.TryInspect(outputPath, out var map, out var diagnostic));
        AssertEqual(null, diagnostic);

        var paragraphs = map!.Paragraphs;
        AssertEqual(true, paragraphs.Any(paragraph => paragraph.Text == "论文题目"));
        AssertEqual(true, paragraphs.Any(paragraph => paragraph.Text == "摘要"));
        AssertEqual(true, paragraphs.Any(paragraph => paragraph.Text == "中文摘要正文。"));
        AssertEqual(true, paragraphs.Any(paragraph => paragraph.Text == "关键词：论文；格式"));
        AssertEqual(true, paragraphs.Any(paragraph => paragraph.Text == "Abstract"));
        AssertEqual(true, paragraphs.Any(paragraph => paragraph.Text == "第一章 绪论"));
        AssertEqual(true, paragraphs.Any(paragraph => paragraph.Text == "1.1 研究背景"));
        AssertEqual(true, paragraphs.Any(paragraph => paragraph.Text == "参考文献"));
        AssertEqual(true, paragraphs.Any(paragraph => paragraph.Text == "[1] 张三. 论文格式研究[J]. 学术期刊, 2024."));
        AssertEqual(true, paragraphs.Any(paragraph => paragraph.Text == "致谢"));
        AssertEqual(1701, map.Sections[0].PageMargin!.LeftTwips);

        var bodyParagraph = paragraphs.Single(paragraph => paragraph.Text == "第一段正文。");
        AssertEqual(480, bodyParagraph.Format.FirstLineIndentTwips);
        AssertEqual("360", bodyParagraph.Format.LineSpacing);
        AssertEqual("24", bodyParagraph.Format.RunFormat!.FontSizeHalfPoints);
        AssertEqual("宋体", bodyParagraph.Format.RunFormat.EastAsiaFont);

        AssertEqual(1, map.Tables.Count);
        AssertEqual(3, map.Tables[0].RowCount);
        AssertEqual(8307, map.Tables[0].Format.WidthTwips);
        var borders = map.Tables[0].Format.Borders ?? throw new UnreachableException("Generated table borders were not inspected.");
        AssertEqual("single", borders.Top!.Value);
        AssertEqual("nil", borders.Left!.Value);
    }

    static void CliGenerateRefusesUnsafeOutputPaths()
    {
        using var temp = new TempDirectory();
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "final-rules.json");
        File.WriteAllText(contentPath, """{"documentKind":"thesisContent","title":"论文题目"}""");
        File.WriteAllText(rulesPath, ThesisJson.Serialize(new TemplateProfile()));

        var contentOverwrite = RunCli(["generate", "--content", contentPath, "--rules", rulesPath, "--out", contentPath]);
        var rulesOverwrite = RunCli(["generate", "--content", contentPath, "--rules", rulesPath, "--out", rulesPath]);
        var missingParent = RunCli(["generate", "--content", contentPath, "--rules", rulesPath, "--out", Path.Combine(temp.Path, "missing", "thesis.docx")]);

        AssertEqual(1, contentOverwrite.ExitCode);
        AssertEqual("generate_output_refused", contentOverwrite.Result.Diagnostics[0].Code);
        AssertEqual(1, rulesOverwrite.ExitCode);
        AssertEqual("generate_output_refused", rulesOverwrite.Result.Diagnostics[0].Code);
        AssertEqual(1, missingParent.ExitCode);
        AssertEqual("generate_output_directory_missing", missingParent.Result.Diagnostics[0].Code);
    }
}
