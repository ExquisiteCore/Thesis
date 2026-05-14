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
        AssertEqual(false, map.Paragraphs.Any(paragraph => paragraph.Text == "中文摘要"));
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

    static void CliAssemblePreservesTemplatePrefixReplacesBodyAndDropsTemplateTail()
    {
        using var temp = new TempDirectory();
        var templatePath = Path.Combine(temp.Path, "template.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "final-rules.json");
        var outputPath = Path.Combine(temp.Path, "assembled.docx");

        WriteMultiSectionThesisTemplateDocx(templatePath);
        File.WriteAllText(rulesPath, ThesisJson.Serialize(new TemplateProfile
        {
            StyleRoles =
            [
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
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "24", EastAsiaFont = "宋体" }
                    }
                }
            ]
        }));
        File.WriteAllText(
            contentPath,
            """
            {
              "schemaVersion": "1.0",
              "documentKind": "thesisContent",
              "title": "论文题目",
              "abstractZh": "正式中文摘要。",
              "chapters": [
                {
                  "title": "第一章 绪论",
                  "paragraphs": ["正式正文段落。"]
                }
              ],
              "references": ["张三. 模板论文研究[J]. 学术期刊, 2026."]
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
        AssertEqual(true, OpenXmlDocumentInspector.TryInspect(outputPath, out var map, out var diagnostic));
        AssertEqual(null, diagnostic);

        AssertEqual(true, map!.Paragraphs.Any(paragraph => paragraph.Text == "封面保留"));
        AssertEqual(false, map.Paragraphs.Any(paragraph => paragraph.Text == "模板摘要占位"));
        AssertEqual(false, map.Paragraphs.Any(paragraph => paragraph.Text == "模板正文占位"));
        AssertEqual(false, map.Paragraphs.Any(paragraph => paragraph.Text == "格式说明保留"));
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text == "正式中文摘要。"));
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text == "正式正文段落。"));
        AssertEqual(2, map.Sections.Count);
        AssertEqual("rIdCoverHeader", map.Sections[0].Headers[0].RelationshipId);
        AssertEqual("rIdBodyHeader", map.Sections[1].Headers[0].RelationshipId);
    }

    static void CliAssembleUsesLastBodySectionWhenTemplateHasNoTailSection()
    {
        using var temp = new TempDirectory();
        var templatePath = Path.Combine(temp.Path, "template.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "final-rules.json");
        var outputPath = Path.Combine(temp.Path, "assembled.docx");

        WriteMultiSectionTemplateWithoutTailSectionDocx(templatePath);
        File.WriteAllText(rulesPath, ThesisJson.Serialize(new TemplateProfile
        {
            StyleRoles =
            [
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
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "24", EastAsiaFont = "宋体" }
                    }
                }
            ]
        }));
        File.WriteAllText(
            contentPath,
            """
            {
              "schemaVersion": "1.0",
              "documentKind": "thesisContent",
              "title": "论文题目",
              "abstractZh": "正式中文摘要。",
              "chapters": [
                {
                  "title": "第一章 绪论",
                  "paragraphs": ["正式正文段落。"]
                }
              ],
              "references": ["张三. 模板论文研究[J]. 学术期刊, 2026."]
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
        AssertEqual(true, OpenXmlDocumentInspector.TryInspect(outputPath, out var map, out var diagnostic));
        AssertEqual(null, diagnostic);

        AssertEqual(2, map!.Sections.Count);
        AssertEqual("rIdCoverHeader", map.Sections[0].Headers[0].RelationshipId);
        AssertEqual("rIdBodyHeader", map.Sections[1].Headers[0].RelationshipId);
        AssertEqual(1701, map.Sections[1].PageMargin!.LeftTwips);
        AssertEqual(false, map.Paragraphs.Any(paragraph => paragraph.Text == "正文格式说明"));
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text == "正式正文段落。"));
    }

    static void CliAssembleCanInsertFrontMatterDocumentsBeforeThesisBody()
    {
        using var temp = new TempDirectory();
        var templatePath = Path.Combine(temp.Path, "template.docx");
        var taskbookPath = Path.Combine(temp.Path, "taskbook.docx");
        var proposalPath = Path.Combine(temp.Path, "proposal.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "final-rules.json");
        var outputPath = Path.Combine(temp.Path, "assembled.docx");

        WriteMultiSectionThesisTemplateDocx(templatePath);
        WriteFrontMatterDocx(taskbookPath, "毕业设计(论文)任务书", "任务书正文", "任务书表格值");
        WriteFrontMatterDocx(proposalPath, "开题报告", "开题报告正文", "开题报告表格值");
        File.WriteAllText(rulesPath, ThesisJson.Serialize(new TemplateProfile
        {
            StyleRoles =
            [
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
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "24", EastAsiaFont = "宋体" }
                    }
                }
            ]
        }));
        File.WriteAllText(
            contentPath,
            """
            {
              "schemaVersion": "1.0",
              "documentKind": "thesisContent",
              "title": "论文题目",
              "abstractZh": "正式中文摘要。",
              "chapters": [
                {
                  "title": "第一章 绪论",
                  "paragraphs": ["正式正文段落。"]
                }
              ],
              "references": ["张三. 模板论文研究[J]. 学术期刊, 2026."]
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
            "--front-matter-doc",
            taskbookPath,
            "--front-matter-doc",
            proposalPath,
            "--out",
            outputPath
        ]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, OpenXmlDocumentInspector.TryInspect(outputPath, out var map, out var diagnostic));
        AssertEqual(null, diagnostic);

        var texts = map!.Paragraphs.Select(paragraph => paragraph.Text).ToList();
        AssertEqual(true, texts.IndexOf("封面保留") < texts.IndexOf("毕业设计(论文)任务书"));
        AssertEqual(true, texts.IndexOf("毕业设计(论文)任务书") < texts.IndexOf("开题报告"));
        AssertEqual(true, texts.IndexOf("开题报告") < texts.IndexOf("正式中文摘要。"));
        AssertEqual(true, map.Tables.Any(table => table.TextPreview.Contains("任务书表格值", StringComparison.Ordinal)));
        AssertEqual(true, map.Tables.Any(table => table.TextPreview.Contains("开题报告表格值", StringComparison.Ordinal)));
    }

    static void CliAssembleCanInsertContentImages()
    {
        using var temp = new TempDirectory();
        var templatePath = Path.Combine(temp.Path, "template.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "final-rules.json");
        var imagePath = Path.Combine(temp.Path, "figure.png");
        var outputPath = Path.Combine(temp.Path, "assembled.docx");

        WriteMultiSectionThesisTemplateDocx(templatePath);
        WriteSinglePixelPng(imagePath);
        File.WriteAllText(rulesPath, ThesisJson.Serialize(new TemplateProfile
        {
            StyleRoles =
            [
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
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "24", EastAsiaFont = "宋体" }
                    }
                },
                new ProfileStyleRole
                {
                    Role = "figureCaption",
                    Format = new ParagraphFormatSample
                    {
                        Alignment = "center",
                        FirstLineIndentTwips = 0,
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "21", EastAsiaFont = "宋体" }
                    }
                }
            ]
        }));
        File.WriteAllText(
            contentPath,
            $$"""
            {
              "schemaVersion": "1.0",
              "documentKind": "thesisContent",
              "title": "论文题目",
              "chapters": [
                {
                  "title": "第一章 绪论",
                  "sections": [
                    {
                      "title": "1.1 研究背景",
                      "blocks": [
                        { "type": "paragraph", "text": "图前正文。" },
                        {
                          "type": "image",
                          "path": "{{imagePath.Replace("\\", "\\\\")}}",
                          "caption": "图 1-1 测试图片",
                          "altText": "测试图片",
                          "widthEmu": 914400,
                          "heightEmu": 914400
                        },
                        { "type": "paragraph", "text": "图后正文。" }
                      ]
                    }
                  ]
                }
              ]
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

        using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(outputPath, false);
        AssertEqual(1, document.MainDocumentPart!.ImageParts.Count());
        var imagePart = document.MainDocumentPart.ImageParts.Single();
        var imagePartUri = imagePart.Uri.ToString();
        var imageRelationship = document.MainDocumentPart.Parts.Single(part => ReferenceEquals(part.OpenXmlPart, imagePart));
        AssertEqual(true, imagePartUri.StartsWith("/word/media/", StringComparison.Ordinal));
        AssertEqual(false, imageRelationship.RelationshipId.StartsWith("/", StringComparison.Ordinal));

        AssertEqual(true, OpenXmlDocumentInspector.TryInspect(outputPath, out var map, out var diagnostic));
        AssertEqual(null, diagnostic);
        AssertEqual(1, map!.Package.ImageCount);
        AssertEqual(0, map.Package.UnresolvedImageReferenceCount);
        AssertEqual(true, map.Package.Relationships.Any(relationship =>
            relationship.Type == "image"
            && relationship.Target.StartsWith("media/", StringComparison.Ordinal)
            && !relationship.Target.StartsWith("/", StringComparison.Ordinal)));
        var texts = map!.Paragraphs.Select(paragraph => paragraph.Text).ToList();
        AssertEqual(true, texts.IndexOf("图前正文。") < texts.IndexOf("图 1-1 测试图片"));
        AssertEqual(true, texts.IndexOf("图 1-1 测试图片") < texts.IndexOf("图后正文。"));
        AssertEqual("center", map.Paragraphs.First(paragraph => paragraph.Text == "图 1-1 测试图片").Format.Alignment);
    }

    static void CliAssembleKeepsDistinctImagesWhenMediaBasenamesCollide()
    {
        using var temp = new TempDirectory();
        var templatePath = Path.Combine(temp.Path, "template.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "final-rules.json");
        var imagePath = Path.Combine(temp.Path, "image1.png");
        var outputPath = Path.Combine(temp.Path, "assembled.docx");

        WriteTemplateDocxWithExistingMediaImage(templatePath);
        WriteSinglePixelPng(imagePath);
        File.WriteAllText(rulesPath, ThesisJson.Serialize(new TemplateProfile()));
        File.WriteAllText(
            contentPath,
            $$"""
            {
              "schemaVersion": "1.0",
              "documentKind": "thesisContent",
              "title": "论文题目",
              "chapters": [
                {
                  "title": "第一章 绪论",
                  "sections": [
                    {
                      "title": "1.1 研究背景",
                      "blocks": [
                        {
                          "type": "image",
                          "path": "{{imagePath.Replace("\\", "\\\\")}}",
                          "caption": "图 1-1 新图片",
                          "widthEmu": 914400,
                          "heightEmu": 914400
                        }
                      ]
                    }
                  ]
                }
              ]
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

        using var archive = ZipFile.OpenRead(outputPath);
        var mediaEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal))
            .Select(entry => entry.FullName)
            .ToList();
        AssertEqual(true, mediaEntries.Contains("word/media/image1.png"));
        AssertEqual(true, mediaEntries.Any(entry => entry != "word/media/image1.png"));
        AssertEqual(false, archive.Entries.Any(entry => entry.FullName.StartsWith("media/", StringComparison.Ordinal)));

        AssertEqual(true, OpenXmlDocumentInspector.TryInspect(outputPath, out var map, out var diagnostic));
        AssertEqual(null, diagnostic);
        AssertEqual(2, map!.Package.ImageCount);
        AssertEqual(0, map.Package.UnresolvedImageReferenceCount);
        AssertEqual(true, map.Package.Relationships
            .Where(relationship => relationship.Type == "image")
            .All(relationship => relationship.Target.StartsWith("media/", StringComparison.Ordinal)));
        AssertEqual(2, map.Package.Relationships
            .Where(relationship => relationship.Type == "image")
            .Select(relationship => relationship.Target)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
    }

    static void CliAssembleNormalizesSharedAndParentRelativeImageTargets()
    {
        using var temp = new TempDirectory();
        var templatePath = Path.Combine(temp.Path, "template.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "final-rules.json");
        var outputPath = Path.Combine(temp.Path, "assembled.docx");

        WriteTemplateDocxWithSharedRootImageRelationships(templatePath);
        File.WriteAllText(rulesPath, ThesisJson.Serialize(new TemplateProfile()));
        File.WriteAllText(
            contentPath,
            """
            {
              "schemaVersion": "1.0",
              "documentKind": "thesisContent",
              "title": "论文题目",
              "chapters": [
                {
                  "title": "第一章 绪论",
                  "paragraphs": ["正文内容。"]
                }
              ]
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

        using var archive = ZipFile.OpenRead(outputPath);
        AssertEqual(false, archive.Entries.Any(entry => entry.FullName.StartsWith("media/", StringComparison.Ordinal)));

        AssertEqual(true, OpenXmlDocumentInspector.TryInspect(outputPath, out var map, out var diagnostic));
        AssertEqual(null, diagnostic);
        AssertEqual(0, map!.Package.UnresolvedImageReferenceCount);
        var imageTargets = map.Package.Relationships
            .Where(relationship => relationship.Type == "image")
            .Select(relationship => relationship.Target)
            .ToList();
        AssertEqual(3, imageTargets.Count);
        AssertEqual(true, imageTargets.All(target => target.StartsWith("media/", StringComparison.Ordinal)));
        AssertEqual(2, imageTargets.Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
