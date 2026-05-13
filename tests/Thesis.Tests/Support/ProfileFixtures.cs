internal static partial class Program
{
    static void WriteResolverProfile(WorkspaceContext context, bool includeAmbiguousZhEvidence = true)
    {
        var zhEvidence = new List<ProfileParagraphEvidence>
        {
            new() { ParagraphIndex = 3, StyleId = "Heading1", TextPreview = "摘要" }
        };
        if (includeAmbiguousZhEvidence)
        {
            zhEvidence.Add(new ProfileParagraphEvidence { ParagraphIndex = 6, StyleId = "Heading1", TextPreview = "参考文献" });
        }

        var profile = new TemplateProfile
        {
            SourceType = "test",
            SourceDocument = context.SourceDoc,
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "abstract.zh",
                    StyleId = "Heading1",
                    Evidence = zhEvidence
                },
                new ProfileStyleRole
                {
                    Role = "abstract.en",
                    StyleId = "Heading1",
                    Evidence =
                    [
                        new ProfileParagraphEvidence { ParagraphIndex = 4, StyleId = "Heading1", TextPreview = "Abstract" }
                    ]
                },
                new ProfileStyleRole
                {
                    Role = "toc",
                    StyleId = "Heading1",
                    Evidence =
                    [
                        new ProfileParagraphEvidence { ParagraphIndex = 5, StyleId = "Heading1", TextPreview = "目录" }
                    ]
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
    }

    static void WriteProfileWithAbstractFormat(WorkspaceContext context)
    {
        var profile = new TemplateProfile
        {
            SourceType = "test",
            SourceDocument = context.SourceDoc,
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "abstract.zh",
                    StyleId = "Heading1",
                    Evidence =
                    [
                        new ProfileParagraphEvidence { ParagraphIndex = 0, StyleId = "Heading1", TextPreview = "摘要" }
                    ],
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

        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
    }

    static void WriteProfileWithBodyFormat(WorkspaceContext context)
    {
        var profile = new TemplateProfile
        {
            SourceType = "test",
            SourceDocument = context.SourceDoc,
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "body",
                    StyleId = "Normal",
                    Evidence =
                    [
                        new ProfileParagraphEvidence { ParagraphIndex = 2, StyleId = "Normal", TextPreview = "列表项" }
                    ],
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Normal",
                        Alignment = "both",
                        LineSpacing = "360",
                        LineSpacingRule = "auto",
                        FirstLineIndentTwips = 480,
                        SpacingBeforeTwips = 0,
                        SpacingAfterTwips = 0,
                        RunFormat = new RunFormatSample
                        {
                            FontSizeHalfPoints = "24",
                            EastAsiaFont = "宋体"
                        }
                    }
                }
            ]
        };

        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
    }

    static void WriteProfileWithTableFormat(WorkspaceContext context)
    {
        var profile = new TemplateProfile
        {
            SourceType = "test",
            SourceDocument = context.SourceDoc,
            TablePolicy = new ProfileTablePolicy
            {
                Detected = true,
                TableCount = 1,
                ObservedColumnCounts = [2],
                Default = new ProfileTableSample
                {
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
                            Top = new TableBorderLineSample { Value = "single", Size = "12", Color = "000000" },
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
                            Alignment = "center",
                            RunFormat = new RunFormatSample
                            {
                                Bold = true,
                                FontSizeHalfPoints = "21",
                                EastAsiaFont = "宋体"
                            }
                        }
                    }
                }
            }
        };

        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
    }

}
