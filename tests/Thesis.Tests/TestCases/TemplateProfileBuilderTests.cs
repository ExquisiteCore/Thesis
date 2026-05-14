internal static partial class Program
{
    static void TemplateProfileBuilderReturnsTypedProfileWithSemanticRoles()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("sample.docx"),
            RequiresFinalization = true,
            FinalizationReasons = ["fields"],
            Styles =
            [
                new DocumentStyle { StyleId = "Title", Name = "Title", Type = "paragraph" },
                new DocumentStyle { StyleId = "Heading1", Name = "heading 1", Type = "paragraph" },
                new DocumentStyle { StyleId = "Normal", Name = "Normal", Type = "paragraph" }
            ],
            Paragraphs =
            [
                new DocumentParagraph { Index = 0, Text = "论文题目", StyleId = "Title" },
                new DocumentParagraph { Index = 1, Text = "中文摘要", StyleId = "Heading1" },
                new DocumentParagraph { Index = 2, Text = "本文研究系统实现。", StyleId = "Normal" },
                new DocumentParagraph { Index = 3, Text = "摘 要", StyleId = "Heading1" },
                new DocumentParagraph { Index = 4, Text = "This thesis studies implementation.", StyleId = "Normal" },
                new DocumentParagraph { Index = 5, Text = "1 Abstract", StyleId = "Heading1" },
                new DocumentParagraph { Index = 6, Text = "Contents", StyleId = "Heading1" },
                new DocumentParagraph { Index = 7, Text = "参考文献", StyleId = "Heading1" }
            ],
            Sections =
            [
                new DocumentSection
                {
                    Index = 0,
                    PageSize = new PageSizeInfo { WidthTwips = 11906, HeightTwips = 16838 },
                    PageMargin = new PageMarginInfo { TopTwips = 1440, RightTwips = 1800, BottomTwips = 1440, LeftTwips = 1800 }
                }
            ]
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");

        AssertEqual("templateProfile", profile.ProfileKind);
        AssertEqual("doc", profile.SourceType);
        AssertEqual(Path.GetFullPath("sample.docx"), profile.SourceDocument);
        AssertEqual(true, profile.RequiresFinalization);
        AssertEqual(11906, profile.PageSetup.PageSize!.WidthTwips);
        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "body" && role.StyleId == "Normal"));
        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "abstract.zh" && role.StyleId == "Heading1"));
        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "abstract.en" && role.StyleId == "Heading1"));
        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "toc.title" && role.StyleId == "Heading1"));
        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "references" && role.StyleId == "Heading1"));
        AssertEqual(true, profile.SourceEvidence.ParagraphSamples.Any(sample => sample.TextPreview == "论文题目"));

        var sourcePageSize = map.Sections[0].PageSize ?? throw new UnreachableException("Expected fixture page size.");
        sourcePageSize.WidthTwips = 1;
        map.Numbering.Add(new DocumentNumbering { NumberingId = "late" });
        AssertEqual(11906, profile.PageSetup.PageSize!.WidthTwips);
        AssertEqual(0, profile.NumberingPolicy.Instances.Count);
    }

    static void TemplateProfileBuilderCopiesRoleFormatSamples()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("sample.docx"),
            Styles =
            [
                new DocumentStyle { StyleId = "Heading1", Name = "heading 1", Type = "paragraph" }
            ],
            Paragraphs =
            [
                new DocumentParagraph
                {
                    Index = 0,
                    Text = "摘要",
                    StyleId = "Heading1",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Heading1",
                        Alignment = "center",
                        SpacingAfterTwips = 120,
                        RunFormat = new RunFormatSample
                        {
                            Bold = true,
                            FontSizeHalfPoints = "28",
                            EastAsiaFont = "黑体"
                        }
                    }
                }
            ]
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");
        var role = profile.StyleRoles.Single(candidate => candidate.Role == "abstract.zh");

        AssertEqual("center", role.Format!.Alignment);
        AssertEqual(120, role.Format.SpacingAfterTwips);
        AssertEqual(true, role.Format.RunFormat!.Bold);
        AssertEqual("28", role.Format.RunFormat.FontSizeHalfPoints);
        AssertEqual("黑体", role.Format.RunFormat.EastAsiaFont);

        map.Paragraphs[0].Format.Alignment = "left";
        map.Paragraphs[0].Format.RunFormat!.EastAsiaFont = "宋体";
        AssertEqual("center", role.Format.Alignment);
        AssertEqual("黑体", role.Format.RunFormat.EastAsiaFont);
    }

    static void TemplateProfileBuilderInfersRolePolicies()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("sample.docx"),
            Styles =
            [
                new DocumentStyle { StyleId = "Heading1", Name = "heading 1", Type = "paragraph", UsageCount = 3 },
                new DocumentStyle { StyleId = "Normal", Name = "Normal", Type = "paragraph", UsageCount = 5 }
            ],
            Paragraphs =
            [
                new DocumentParagraph
                {
                    Index = 0,
                    Text = "第一章 绪论",
                    StyleId = "Heading1",
                    OutlineLevel = 0,
                    Format = new ParagraphFormatSample { StyleId = "Heading1", Alignment = "center" }
                },
                new DocumentParagraph
                {
                    Index = 1,
                    Text = "正文内容",
                    StyleId = "Normal",
                    Format = new ParagraphFormatSample { StyleId = "Normal", FirstLineIndentTwips = 480 }
                }
            ]
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");

        var headingPolicy = profile.RolePolicies.Single(policy => policy.Role == "heading1");
        AssertEqual("paragraph", headingPolicy.AppliesTo);
        AssertEqual(true, headingPolicy.Priority > 0);
        AssertEqual("Heading1", headingPolicy.Match.StyleIds[0]);
        AssertEqual(0, headingPolicy.Match.OutlineLevels[0]);
        AssertEqual("center", headingPolicy.Format!.Alignment);

        var bodyPolicy = profile.RolePolicies.Single(policy => policy.Role == "body");
        AssertEqual("Normal", bodyPolicy.Match.StyleIds[0]);
        AssertEqual(480, bodyPolicy.Format!.FirstLineIndentTwips);

        map.Paragraphs.Add(new DocumentParagraph { Index = 2, Text = "摘要", StyleId = "Heading1" });
        var semanticProfile = TemplateProfileBuilder.Build(map, "doc");
        var abstractPolicy = semanticProfile.RolePolicies.Single(policy => policy.Role == "abstract.zh");
        AssertEqual("^摘要$", abstractPolicy.Match.TextPatterns[0]);
    }

    static void TemplateProfileBuilderInfersDirectFormatRolesWithoutSemanticStyles()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("plain-template.docx"),
            Styles =
            [
                new DocumentStyle { StyleId = "Heading1", Name = "heading 1", Type = "paragraph", UsageCount = 0 },
                new DocumentStyle { StyleId = "Normal", Name = "Normal", Type = "paragraph", UsageCount = 0 },
                new DocumentStyle { StyleId = "2", Name = "Plain Text", Type = "paragraph", UsageCount = 7 }
            ],
            Paragraphs =
            [
                new DocumentParagraph
                {
                    Index = 0,
                    Text = "第一章绪论",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        Alignment = "center",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        FirstLineIndentTwips = 420,
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "32" }
                    }
                },
                new DocumentParagraph
                {
                    Index = 1,
                    Text = "1.1  研究背景",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        SpacingBeforeTwips = 240,
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "24" }
                    }
                },
                new DocumentParagraph
                {
                    Index = 2,
                    Text = "1.1.1 研究意义",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "21" }
                    }
                },
                new DocumentParagraph
                {
                    Index = 3,
                    Text = "本文围绕系统设计与实现展开研究。",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        FirstLineIndentTwips = 420,
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "21" }
                    }
                },
                new DocumentParagraph
                {
                    Index = 4,
                    Text = "第二章  需求分析",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        Alignment = "center",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        FirstLineIndentTwips = 420,
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "32" }
                    }
                },
                new DocumentParagraph
                {
                    Index = 5,
                    Text = "2.1  功能需求",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        SpacingBeforeTwips = 240,
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "24" }
                    }
                },
                new DocumentParagraph
                {
                    Index = 6,
                    Text = "正文第二段内容。",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        FirstLineIndentTwips = 420,
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "21" }
                    }
                }
            ]
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");

        var heading1 = profile.RolePolicies
            .Where(policy => policy.Role == "heading1")
            .OrderByDescending(policy => policy.Priority)
            .First();
        AssertEqual(0, heading1.Match.StyleIds.Count);
        AssertEqual(true, heading1.Match.TextPatterns.Any(pattern => Regex.IsMatch("第一章绪论", pattern)));
        AssertEqual("center", heading1.Format!.Alignment);
        AssertEqual(true, heading1.Format.RunFormat!.Bold);
        AssertEqual("32", heading1.Format.RunFormat.FontSizeHalfPoints);
        AssertEqual(true, heading1.Confidence >= 0.7);

        var heading2 = profile.RolePolicies.Single(policy => policy.Role == "heading2");
        AssertEqual(true, heading2.Match.TextPatterns.Any(pattern => Regex.IsMatch("1.1  研究背景", pattern)));
        AssertEqual(240, heading2.Format!.SpacingBeforeTwips);
        AssertEqual("24", heading2.Format.RunFormat!.FontSizeHalfPoints);

        var heading3 = profile.RolePolicies.Single(policy => policy.Role == "heading3");
        AssertEqual(true, heading3.Match.TextPatterns.Any(pattern => Regex.IsMatch("1.1.1 研究意义", pattern)));
        AssertEqual("21", heading3.Format!.RunFormat!.FontSizeHalfPoints);

        var body = profile.RolePolicies
            .Where(policy => policy.Role == "body")
            .OrderByDescending(policy => policy.Priority)
            .First();
        AssertEqual(0, body.Match.StyleIds.Count);
        AssertEqual(true, body.Match.TextPatterns.Any(pattern => Regex.IsMatch("本文围绕系统设计与实现展开研究。", pattern)));
        AssertEqual(false, body.Match.TextPatterns.Any(pattern => Regex.IsMatch("第一章绪论", pattern)));
        AssertEqual("21", body.Match.Format!.FontSizeHalfPoints);
        AssertEqual(false, body.Match.Format.Bold);
        AssertEqual("360", body.Match.Format.LineSpacing);
        AssertEqual("atleast", body.Match.Format.LineSpacingRule);
        AssertEqual(360, body.Match.Format.FirstLineIndentTwips!.Min);
        AssertEqual(560, body.Match.Format.FirstLineIndentTwips.Max);
        AssertEqual(420, body.Format!.FirstLineIndentTwips);
        AssertEqual(false, body.Format.RunFormat!.Bold);
        AssertEqual("21", body.Format.RunFormat.FontSizeHalfPoints);

        AssertEqual(true, profile.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "profile_role_inferred"
            && diagnostic.Evidence.Any(evidence => evidence == "role:heading1")));
        AssertEqual(true, profile.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "profile_style_ambiguous"
            && diagnostic.Evidence.Any(evidence => evidence == "style:2")));
    }

    static void TemplateProfileBuilderInfersExpandedSemanticRoles()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("semantic-template.docx"),
            Styles =
            [
                new DocumentStyle { StyleId = "Heading1", Name = "heading 1", Type = "paragraph", UsageCount = 4 },
                new DocumentStyle { StyleId = "Caption", Name = "caption", Type = "paragraph", UsageCount = 2 },
                new DocumentStyle { StyleId = "Normal", Name = "Normal", Type = "paragraph", UsageCount = 4 }
            ],
            Paragraphs =
            [
                new DocumentParagraph
                {
                    Index = 0,
                    Text = "关键词：毕业论文；排版工具；Open XML",
                    StyleId = "Normal",
                    Format = new ParagraphFormatSample { StyleId = "Normal", RunFormat = new RunFormatSample { FontSizeHalfPoints = "21" } }
                },
                new DocumentParagraph
                {
                    Index = 1,
                    Text = "Key words: thesis; formatting; automation",
                    StyleId = "Normal",
                    Format = new ParagraphFormatSample { StyleId = "Normal", RunFormat = new RunFormatSample { FontSizeHalfPoints = "21" } }
                },
                new DocumentParagraph
                {
                    Index = 2,
                    Text = "致谢",
                    StyleId = "Heading1",
                    Format = new ParagraphFormatSample { StyleId = "Heading1", Alignment = "center", RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "28" } }
                },
                new DocumentParagraph
                {
                    Index = 3,
                    Text = "附录A 访谈提纲",
                    StyleId = "Heading1",
                    Format = new ParagraphFormatSample { StyleId = "Heading1", Alignment = "center", RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "28" } }
                },
                new DocumentParagraph
                {
                    Index = 4,
                    Text = "图 1-1 系统总体架构",
                    StyleId = "Caption",
                    Format = new ParagraphFormatSample { StyleId = "Caption", Alignment = "center", RunFormat = new RunFormatSample { FontSizeHalfPoints = "18" } }
                },
                new DocumentParagraph
                {
                    Index = 5,
                    Text = "表 2-1 功能模块说明",
                    StyleId = "Caption",
                    Format = new ParagraphFormatSample { StyleId = "Caption", Alignment = "center", RunFormat = new RunFormatSample { FontSizeHalfPoints = "18" } }
                }
            ]
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");

        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "keywords.zh" && role.StyleId == "Normal"));
        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "keywords.en" && role.StyleId == "Normal"));
        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "acknowledgements" && role.StyleId == "Heading1"));
        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "appendix" && role.StyleId == "Heading1"));
        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "figureCaption" && role.StyleId == "Caption"));
        AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "tableCaption" && role.StyleId == "Caption"));

        var zhKeywords = profile.RolePolicies.Single(policy => policy.Role == "keywords.zh");
        AssertEqual("^关键词：毕业论文；排版工具；Open\\ XML$", zhKeywords.Match.TextPatterns[0]);
        AssertEqual("21", zhKeywords.Format!.RunFormat!.FontSizeHalfPoints);

        var figureCaption = profile.RolePolicies.Single(policy => policy.Role == "figureCaption");
        AssertEqual(true, figureCaption.Match.TextPatterns.Any(pattern => Regex.IsMatch("图 1-2 数据流程", pattern)));
        AssertEqual("center", figureCaption.Format!.Alignment);
        AssertEqual("18", figureCaption.Format.RunFormat!.FontSizeHalfPoints);
    }

    static void TemplateProfileBuilderClustersParagraphFormats()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("clustered-template.docx"),
            Styles =
            [
                new DocumentStyle { StyleId = "2", Name = "Plain Text", Type = "paragraph", UsageCount = 6 }
            ],
            Paragraphs =
            [
                new DocumentParagraph
                {
                    Index = 0,
                    Text = "1.1  研究背景",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        SpacingBeforeTwips = 240,
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "24" }
                    }
                },
                new DocumentParagraph
                {
                    Index = 1,
                    Text = "1.2  国内外研究现状",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        SpacingBeforeTwips = 240,
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "24" }
                    }
                },
                new DocumentParagraph
                {
                    Index = 2,
                    Text = "本文围绕系统设计与实现展开研究。",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        FirstLineIndentTwips = 420,
                        RunFormat = new RunFormatSample { Bold = false, FontSizeHalfPoints = "21" }
                    }
                },
                new DocumentParagraph
                {
                    Index = 3,
                    Text = "系统采用分层架构提高维护性。",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        FirstLineIndentTwips = 420,
                        RunFormat = new RunFormatSample { Bold = false, FontSizeHalfPoints = "21" }
                    }
                },
                new DocumentParagraph
                {
                    Index = 4,
                    Text = "参考文献",
                    StyleId = "2",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        Alignment = "center",
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "28" }
                    }
                }
            ]
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");

        var heading2 = profile.FormatClusters.Single(cluster => cluster.RoleHint == "heading2");
        AssertEqual("paragraph", heading2.AppliesTo);
        AssertEqual(2, heading2.Count);
        AssertEqual("2", heading2.StyleIds[0]);
        AssertEqual("24", heading2.Match.Format!.FontSizeHalfPoints);
        AssertEqual(true, heading2.Match.Format.Bold);
        AssertEqual(240, heading2.Format!.SpacingBeforeTwips);
        AssertEqual(0, heading2.Evidence[0].ParagraphIndex);
        AssertEqual(1, heading2.Evidence[1].ParagraphIndex);

        var body = profile.FormatClusters.Single(cluster => cluster.RoleHint == "body");
        AssertEqual(2, body.Count);
        AssertEqual("21", body.Match.Format!.FontSizeHalfPoints);
        AssertEqual(false, body.Match.Format.Bold);
        AssertEqual(420, body.Match.Format.FirstLineIndentTwips!.Exact);
        AssertEqual(420, body.Format!.FirstLineIndentTwips);
        AssertEqual(true, body.Confidence >= 0.7);

        var json = ThesisJson.Serialize(profile);
        AssertContains(json, "\"formatClusters\"");
        AssertContains(json, "\"roleHint\":\"body\"");
    }

    static void TemplateProfileBuilderGroupsMultipleTableArchetypes()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("tables-template.docx"),
            Tables =
            [
                new DocumentTable
                {
                    Index = 0,
                    RowCount = 3,
                    CellCounts = [2, 2, 2],
                    TextPreview = "three line 1",
                    Format = new TableFormatSample
                    {
                        WidthTwips = 8000,
                        WidthType = "dxa",
                        Alignment = "center",
                        Borders = new TableBordersSample
                        {
                            Top = new TableBorderLineSample { Value = "single", Size = "12" },
                            Bottom = new TableBorderLineSample { Value = "single", Size = "12" },
                            Left = new TableBorderLineSample { Value = "nil" },
                            Right = new TableBorderLineSample { Value = "nil" },
                            InsideHorizontal = new TableBorderLineSample { Value = "single", Size = "4" },
                            InsideVertical = new TableBorderLineSample { Value = "nil" }
                        }
                    }
                },
                new DocumentTable
                {
                    Index = 1,
                    RowCount = 4,
                    CellCounts = [2, 2, 2, 2],
                    TextPreview = "three line 2",
                    Format = new TableFormatSample
                    {
                        WidthTwips = 8000,
                        WidthType = "dxa",
                        Alignment = "center",
                        Borders = new TableBordersSample
                        {
                            Top = new TableBorderLineSample { Value = "single", Size = "12" },
                            Bottom = new TableBorderLineSample { Value = "single", Size = "12" },
                            Left = new TableBorderLineSample { Value = "nil" },
                            Right = new TableBorderLineSample { Value = "nil" },
                            InsideHorizontal = new TableBorderLineSample { Value = "single", Size = "4" },
                            InsideVertical = new TableBorderLineSample { Value = "nil" }
                        }
                    }
                },
                new DocumentTable
                {
                    Index = 2,
                    RowCount = 5,
                    CellCounts = [3, 3, 3, 3, 3],
                    TextPreview = "grid",
                    Format = new TableFormatSample
                    {
                        WidthTwips = 9000,
                        WidthType = "dxa",
                        Alignment = "center",
                        Borders = new TableBordersSample
                        {
                            Top = new TableBorderLineSample { Value = "single", Size = "4" },
                            Bottom = new TableBorderLineSample { Value = "single", Size = "4" },
                            Left = new TableBorderLineSample { Value = "single", Size = "4" },
                            Right = new TableBorderLineSample { Value = "single", Size = "4" },
                            InsideHorizontal = new TableBorderLineSample { Value = "single", Size = "4" },
                            InsideVertical = new TableBorderLineSample { Value = "single", Size = "4" }
                        }
                    }
                }
            ]
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");

        AssertEqual(2, profile.TableArchetypes.Count);
        var threeLine = profile.TableArchetypes.Single(archetype => archetype.Name == "threeLine");
        AssertEqual(3, threeLine.Match.MinRows);
        AssertEqual(4, threeLine.Match.MaxRows);
        AssertEqual(2, threeLine.Match.ColumnCounts[0]);
        AssertEqual("single", threeLine.Format!.Borders!.Top!.Value);
        AssertEqual("nil", threeLine.Format.Borders.Left!.Value);
        AssertEqual(true, threeLine.Confidence >= 0.8);

        var grid = profile.TableArchetypes.Single(archetype => archetype.Name == "grid");
        AssertEqual(5, grid.Match.MinRows);
        AssertEqual(5, grid.Match.MaxRows);
        AssertEqual(3, grid.Match.ColumnCounts[0]);
        AssertEqual("single", grid.Format!.Borders!.Left!.Value);
        AssertEqual("single", grid.Format.Borders.InsideVertical!.Value);
    }

    static void TemplateProfileBuilderCopiesTableFormatSamples()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("sample.docx"),
            Tables =
            [
                new DocumentTable
                {
                    Index = 0,
                    RowCount = 2,
                    CellCounts = [2, 2],
                    TextPreview = "A1 B1",
                    Format = new TableFormatSample
                    {
                        WidthTwips = 8640,
                        WidthType = "dxa",
                        Alignment = "center",
                        GridColumnWidthsTwips = [4320, 4320],
                        Borders = new TableBordersSample
                        {
                            Top = new TableBorderLineSample { Value = "single", Size = "12", Color = "000000", Space = "0" },
                            Bottom = new TableBorderLineSample { Value = "single", Size = "12", Color = "000000" },
                            Left = new TableBorderLineSample { Value = "nil" },
                            Right = new TableBorderLineSample { Value = "nil" },
                            InsideHorizontal = new TableBorderLineSample { Value = "single", Size = "4", Color = "000000" },
                            InsideVertical = new TableBorderLineSample { Value = "nil" }
                        },
                        CellMargins = new TableCellMarginsSample
                        {
                            TopTwips = 60,
                            RightTwips = 120,
                            BottomTwips = 60,
                            LeftTwips = 120
                        },
                        HeaderRowCount = 1,
                        FirstCellParagraphFormat = new ParagraphFormatSample
                        {
                            StyleId = "TableHeader",
                            Alignment = "center",
                            SpacingAfterTwips = 0,
                            RunFormat = new RunFormatSample
                            {
                                Bold = true,
                                FontSizeHalfPoints = "21",
                                AsciiFont = "Times New Roman",
                                EastAsiaFont = "宋体"
                            }
                        }
                    }
                }
            ]
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");
        var format = profile.TablePolicy.Default!.Format!;

        AssertEqual(8640, format.WidthTwips);
        AssertEqual("dxa", format.WidthType);
        AssertEqual("center", format.Alignment);
        AssertEqual(2, format.GridColumnWidthsTwips.Count);
        AssertEqual(4320, format.GridColumnWidthsTwips[0]);
        AssertEqual(4320, format.GridColumnWidthsTwips[1]);
        AssertEqual("single", format.Borders!.Top!.Value);
        AssertEqual("12", format.Borders.Top.Size);
        AssertEqual("000000", format.Borders.Top.Color);
        AssertEqual("0", format.Borders.Top.Space);
        AssertEqual("single", format.Borders.Bottom!.Value);
        AssertEqual("nil", format.Borders.Left!.Value);
        AssertEqual("nil", format.Borders.Right!.Value);
        AssertEqual("single", format.Borders.InsideHorizontal!.Value);
        AssertEqual("4", format.Borders.InsideHorizontal.Size);
        AssertEqual("nil", format.Borders.InsideVertical!.Value);
        AssertEqual(60, format.CellMargins!.TopTwips);
        AssertEqual(120, format.CellMargins.RightTwips);
        AssertEqual(60, format.CellMargins.BottomTwips);
        AssertEqual(120, format.CellMargins.LeftTwips);
        AssertEqual(1, format.HeaderRowCount);
        AssertEqual("TableHeader", format.FirstCellParagraphFormat!.StyleId);
        AssertEqual("center", format.FirstCellParagraphFormat.Alignment);
        AssertEqual(0, format.FirstCellParagraphFormat.SpacingAfterTwips);
        AssertEqual(true, format.FirstCellParagraphFormat.RunFormat!.Bold);
        AssertEqual("21", format.FirstCellParagraphFormat.RunFormat.FontSizeHalfPoints);
        AssertEqual("Times New Roman", format.FirstCellParagraphFormat.RunFormat.AsciiFont);
        AssertEqual("宋体", format.FirstCellParagraphFormat.RunFormat.EastAsiaFont);

        var sourceFormat = map.Tables[0].Format;
        sourceFormat.WidthTwips = 1;
        sourceFormat.GridColumnWidthsTwips[0] = 1;
        sourceFormat.Borders!.Top!.Value = "nil";
        sourceFormat.Borders.InsideHorizontal!.Color = "FFFFFF";
        sourceFormat.CellMargins!.LeftTwips = 1;
        sourceFormat.HeaderRowCount = 9;
        sourceFormat.FirstCellParagraphFormat!.Alignment = "left";
        sourceFormat.FirstCellParagraphFormat.RunFormat!.EastAsiaFont = "黑体";

        AssertEqual(8640, format.WidthTwips);
        AssertEqual(4320, format.GridColumnWidthsTwips[0]);
        AssertEqual("single", format.Borders.Top.Value);
        AssertEqual("000000", format.Borders.InsideHorizontal.Color);
        AssertEqual(120, format.CellMargins.LeftTwips);
        AssertEqual(1, format.HeaderRowCount);
        AssertEqual("center", format.FirstCellParagraphFormat.Alignment);
        AssertEqual("宋体", format.FirstCellParagraphFormat.RunFormat.EastAsiaFont);
    }

    static void TemplateProfileBuilderInfersThreeLineTableArchetype()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("sample.docx"),
            Tables =
            [
                new DocumentTable
                {
                    Index = 0,
                    RowCount = 2,
                    CellCounts = [2, 2],
                    TextPreview = "A1 B1",
                    Format = new TableFormatSample
                    {
                        Borders = new TableBordersSample
                        {
                            Top = new TableBorderLineSample { Value = "single", Size = "12" },
                            Bottom = new TableBorderLineSample { Value = "single", Size = "12" },
                            Left = new TableBorderLineSample { Value = "nil" },
                            Right = new TableBorderLineSample { Value = "nil" },
                            InsideHorizontal = new TableBorderLineSample { Value = "single", Size = "4" },
                            InsideVertical = new TableBorderLineSample { Value = "nil" }
                        }
                    }
                }
            ]
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");

        var archetype = profile.TableArchetypes.Single();
        AssertEqual("threeLine", archetype.Name);
        AssertEqual(2, archetype.Match.MinRows);
        AssertEqual(2, archetype.Match.ColumnCounts[0]);
        AssertEqual("single", archetype.Format!.Borders!.Top!.Value);
    }

    static void TemplateProfileBuilderReportsWeakProfileDiagnostics()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("sample.docx"),
            Paragraphs =
            [
                new DocumentParagraph { Index = 0, Text = "正文", StyleId = "Normal", Format = new ParagraphFormatSample() }
            ],
            Styles =
            [
                new DocumentStyle { StyleId = "Normal", Name = "Normal", Type = "paragraph", UsageCount = 1 }
            ]
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");

        AssertEqual(true, profile.Diagnostics.Any(diagnostic => diagnostic.Code == "profile_role_missing"));
        AssertEqual(true, profile.Diagnostics.Any(diagnostic => diagnostic.Code == "profile_table_missing"));
    }

    static void TemplateProfileBuilderExtractsStructuralPackageAndZonePolicies()
    {
        var map = new DocumentMap
        {
            Path = Path.GetFullPath("accepted-thesis.docx"),
            Paragraphs =
            [
                new DocumentParagraph { Index = 0, BodyElementIndex = 0, Text = "封面" },
                new DocumentParagraph { Index = 1, BodyElementIndex = 1, Text = "独创性声明" },
                new DocumentParagraph { Index = 2, BodyElementIndex = 2, Text = "摘   要", StyleId = "21" },
                new DocumentParagraph { Index = 3, BodyElementIndex = 3, Text = "目    录", StyleId = "22" },
                new DocumentParagraph { Index = 4, BodyElementIndex = 4, Text = "第一章 绪论", StyleId = "23" },
                new DocumentParagraph { Index = 5, BodyElementIndex = 5, Text = "参考文献", StyleId = "24" },
                new DocumentParagraph { Index = 6, BodyElementIndex = 6, Text = "致谢", StyleId = "25" }
            ],
            Styles =
            [
                new DocumentStyle { StyleId = "21", Name = "摘要标题", Type = "paragraph", UsageCount = 1 },
                new DocumentStyle { StyleId = "22", Name = "目录标题", Type = "paragraph", UsageCount = 1 },
                new DocumentStyle { StyleId = "23", Name = "章标题", Type = "paragraph", UsageCount = 1 },
                new DocumentStyle { StyleId = "Heading1", Name = "heading 1", Type = "paragraph", UsageCount = 0 }
            ],
            Sections =
            [
                new DocumentSection
                {
                    Index = 0,
                    Headers = [new HeaderFooterReference { Type = "default", RelationshipId = "rIdCoverHeader" }],
                    Footers = []
                },
                new DocumentSection
                {
                    Index = 1,
                    Headers = [new HeaderFooterReference { Type = "default", RelationshipId = "rIdBodyHeader" }],
                    Footers = [new HeaderFooterReference { Type = "default", RelationshipId = "rIdBodyFooter" }]
                },
                new DocumentSection
                {
                    Index = 2,
                    Headers = [new HeaderFooterReference { Type = "default", RelationshipId = "rIdTailHeader" }],
                    Footers = [new HeaderFooterReference { Type = "default", RelationshipId = "rIdTailFooter" }]
                }
            ],
            Package = new DocumentPackageFacts
            {
                ImageCount = 2,
                DrawingCount = 2,
                UnresolvedImageReferenceCount = 0,
                Relationships =
                [
                    new DocumentRelationship { Id = "rIdImage1", Type = "image", Target = "media/image1.png", TargetMode = "" },
                    new DocumentRelationship { Id = "rIdImage2", Type = "image", Target = "media/image2.png", TargetMode = "" }
                ],
                FieldCodes =
                [
                    new DocumentFieldCode { Kind = "TOC", Instruction = "TOC \\o \"1-3\" \\h \\z \\u" }
                ]
            }
        };

        var profile = TemplateProfileBuilder.Build(map, "doc");

        AssertEqual(3, profile.StructurePolicy.SectionCount);
        AssertEqual(3, profile.StructurePolicy.Sections.Count);
        AssertEqual("default", profile.StructurePolicy.Sections[0].HeaderSignature);
        AssertEqual("default", profile.StructurePolicy.Sections[1].FooterSignature);
        AssertEqual(true, profile.StylePolicy.PreserveNumericStyleIds);
        AssertEqual(true, profile.StylePolicy.NumericStyleIds.Contains("21"));
        AssertEqual(false, profile.StylePolicy.DisallowedGeneratedStyleIds.Contains("21"));
        AssertEqual(false, profile.StylePolicy.DisallowedGeneratedStyleIds.Contains("Heading1"));
        AssertEqual("word/media", profile.PackagePolicy.ImagePartRoot);
        AssertEqual("relative", profile.PackagePolicy.ImageRelationshipTargetMode);
        AssertEqual(2, profile.PackagePolicy.ImageCount);
        AssertEqual(false, profile.PackagePolicy.AllowUnresolvedImageReferences);
        AssertEqual(true, profile.FieldPolicy.RequiresToc);
        AssertEqual(true, profile.FieldPolicy.AllowTcFields);
        AssertEqual("abstract.zh", profile.ZonePolicy.Landmarks[0].Role);
        AssertEqual("body", profile.ZonePolicy.Landmarks[2].Role);
        AssertEqual(0, profile.ZonePolicy.ForbiddenFrontMatterHeadings.Count);
    }

}
