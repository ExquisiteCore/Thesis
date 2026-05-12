internal static partial class Program
{
    static void CliValidateSuggestsProfileRoleFixes()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "abstract.zh",
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
                    },
                    Evidence =
                    [
                        new ProfileParagraphEvidence { ParagraphIndex = 0, StyleId = "Title", TextPreview = "中文摘要" }
                    ]
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));

        var (exitCode, result) = RunCli(["validate", "--workspace", context.Workspace]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.Validation is not null);
        AssertEqual(false, result.Validation!.Compliant);
        AssertEqual(true, result.Validation.Diagnostics.Any(diagnostic => diagnostic.Code == "profile_role_format_mismatch"));
        AssertEqual(true, result.Validation.SuggestedOperations.Any(operation =>
            operation.Op == "applyProfileRole"
            && operation.Role == "abstract.zh"
            && operation.Target?["type"]?.GetValue<string>() == "paragraphIndex"
            && operation.Target?["index"]?.GetValue<int>() == 0));
    }

    static void CliValidateSuggestsPageSetupFixes()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            PageSetup = new ProfilePageSetup
            {
                PageSize = new PageSizeInfo
                {
                    WidthTwips = 11906,
                    HeightTwips = 16838
                },
                Margins = new PageMarginInfo
                {
                    TopTwips = 1200,
                    RightTwips = 1300,
                    BottomTwips = 1400,
                    LeftTwips = 1500
                }
            }
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));

        var (exitCode, result) = RunCli(["validate", "--workspace", context.Workspace]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(false, result.Validation!.Compliant);
        AssertEqual(true, result.Validation.Diagnostics.Any(diagnostic => diagnostic.Code == "profile_page_setup_mismatch"));
        AssertEqual(true, result.Validation.SuggestedOperations.Any(operation => operation.Op == "applyProfilePageSetup"));
    }

    static void CliValidateWarnsWhenFinalizationIsStillRequired()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");
        WriteSimpleDocx(
            docx,
            """
            <w:p><w:r><w:fldChar w:fldCharType="begin" w:dirty="true"/></w:r><w:r><w:instrText> PAGE </w:instrText></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
            """);
        File.WriteAllText(profilePath, "{}");

        var (exitCode, result) = RunCli(["validate", "--doc", docx, "--profile", profilePath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.Validation!.Compliant);
        AssertEqual(true, result.Diagnostics.Any(diagnostic => diagnostic.Code == "finalization_required"));
    }

    static void CliValidateResolvesProfileRolesAgainstTargetDocument()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            SourceType = "doc",
            SourceDocument = Path.Combine(temp.Path, "template.docx"),
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "toc.title",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Heading1",
                        Alignment = "center",
                        RunFormat = new RunFormatSample
                        {
                            Bold = true,
                            FontSizeHalfPoints = "28"
                        }
                    },
                    Evidence =
                    [
                        new ProfileParagraphEvidence
                        {
                            ParagraphIndex = 1,
                            StyleId = "Heading1",
                            TextPreview = "目录"
                        }
                    ]
                }
            ],
            RolePolicies =
            [
                new ProfileRolePolicy
                {
                    Role = "toc.title",
                    AppliesTo = "paragraph",
                    Priority = 80,
                    Match = new ProfileRoleMatch { TextPatterns = ["^目录$"] },
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Heading1",
                        Alignment = "center",
                        RunFormat = new RunFormatSample
                        {
                            Bold = true,
                            FontSizeHalfPoints = "28"
                        }
                    }
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));

        var (exitCode, result) = RunCli(["validate", "--workspace", context.Workspace]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        var suggested = result.Validation!.SuggestedOperations
            .Single(operation => operation.Op == "applyProfileRole" && operation.Role == "toc.title");
        AssertEqual("paragraphIndex", suggested.Target?["type"]?.GetValue<string>());
        AssertEqual(5, suggested.Target?["index"]?.GetValue<int>());
        AssertEqual(false, result.Validation.SuggestedOperations.Any(operation =>
            operation.Op == "applyProfileRole"
            && operation.Role == "toc.title"
            && operation.Target?["index"]?.GetValue<int>() == 1));
    }

    static void CliValidateUsesSemanticFallbackForTemplatePlaceholders()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "paper.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");
        var workspace = Path.Combine(temp.Path, ".thesis");
        WriteSimpleDocx(
            docx,
            """
            <w:p><w:pPr><w:pStyle w:val="2"/></w:pPr><w:r><w:t>摘   要</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="2"/></w:pPr><w:r><w:t>关键词：工业控制系统；入侵检测</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="2"/></w:pPr><w:r><w:t>目    录</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="2"/></w:pPr><w:r><w:t>第一章 绪论1</w:t></w:r></w:p>
            """);
        var profile = new TemplateProfile
        {
            SourceDocument = Path.GetFullPath("template.docx"),
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "keywords.zh",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "21" }
                    },
                    Evidence =
                    [
                        new ProfileParagraphEvidence
                        {
                            ParagraphIndex = 1,
                            StyleId = "2",
                            TextPreview = "关键词：（3~8个词）□□□□□□；"
                        }
                    ]
                },
                new ProfileStyleRole
                {
                    Role = "toc.title",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        Alignment = "center"
                    },
                    Evidence =
                    [
                        new ProfileParagraphEvidence
                        {
                            ParagraphIndex = 3,
                            StyleId = "2",
                            TextPreview = "目    录"
                        }
                    ]
                }
            ],
            RolePolicies =
            [
                new ProfileRolePolicy
                {
                    Role = "keywords.zh",
                    AppliesTo = "paragraph",
                    Priority = 70,
                    Match = new ProfileRoleMatch
                    {
                        StyleIds = ["2"],
                        TextPatterns = ["^关键词：（3~8个词）□□□□□□；.*$"]
                    },
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "21" }
                    }
                },
                new ProfileRolePolicy
                {
                    Role = "toc.title",
                    AppliesTo = "paragraph",
                    Priority = 80,
                    Match = new ProfileRoleMatch
                    {
                        StyleIds = ["2"],
                        TextPatterns = ["^目\\ \\ \\ \\ 录$"]
                    },
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        Alignment = "center"
                    }
                }
            ]
        };
        File.WriteAllText(profilePath, ThesisJson.Serialize(profile));
        AssertEqual("success", SessionInitializer.Initialize(docx, profilePath, workspace).Status);

        var (exitCode, result) = RunCli(["validate", "--workspace", workspace]);
        var report = result.Validation!;

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(false, report.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "profile_role_target_unresolved"
            && diagnostic.Path == "roles[keywords.zh]"));
        AssertEqual(false, report.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "profile_role_target_unresolved"
            && diagnostic.Path == "roles[toc.title]"));
        AssertEqual(true, report.SuggestedOperations.Any(operation =>
            operation.Role == "keywords.zh"
            && operation.Target?["index"]?.GetValue<int>() == 1));
        AssertEqual(true, report.SuggestedOperations.Any(operation =>
            operation.Role == "toc.title"
            && operation.Target?["index"]?.GetValue<int>() == 2));
        AssertEqual(false, report.SuggestedOperations.Any(operation =>
            operation.Role == "toc.title"
            && operation.Target?["index"]?.GetValue<int>() == 3));
    }
}
