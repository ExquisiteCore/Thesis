internal static partial class Program
{
    static void CliRunResolveTargetFindsRoleEvidenceFromProfile()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteResolverProfile(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-role",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "abstract.zh" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(2, result.Operations[0].Matches.Count);
        AssertEqual("p3", result.Operations[0].Matches[0].Id);
        AssertEqual("摘要", result.Operations[0].Matches[0].Preview);
        AssertEqual("p6", result.Operations[0].Matches[1].Id);
        AssertEqual("参考文献", result.Operations[0].Matches[1].Preview);
    }

    static void CliRunRoleTargetUsesRolePoliciesWhenEvidenceIsMissing()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "heading1",
                    Evidence = []
                }
            ],
            RolePolicies =
            [
                new ProfileRolePolicy
                {
                    Role = "heading1",
                    AppliesTo = "paragraph",
                    Priority = 100,
                    Match = new ProfileRoleMatch { StyleIds = ["Heading1"] }
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-heading",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "heading1" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.Operations[0].Matches.Count > 0);
    }

    static void CliRunRoleTargetIgnoresCrossDocumentEvidenceWhenPolicyMatches()
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
                    StyleId = "Heading1",
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
                    Match = new ProfileRoleMatch { TextPatterns = ["^目录$"] }
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-toc-title",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "toc.title" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(1, result.Operations[0].Matches.Count);
        AssertEqual("p5", result.Operations[0].Matches[0].Id);
        AssertEqual("目录", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunRoleTargetUsesSemanticFallbackForTemplatePlaceholders()
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
                    StyleId = "2",
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
                    StyleId = "2",
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
                    }
                }
            ]
        };
        File.WriteAllText(profilePath, ThesisJson.Serialize(profile));
        AssertEqual("success", SessionInitializer.Initialize(docx, profilePath, workspace).Status);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-keywords",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "keywords.zh" }
                },
                {
                  "id": "find-toc-title",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "toc.title" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("p1", result.Operations[0].Matches[0].Id);
        AssertEqual("关键词：工业控制系统；入侵检测", result.Operations[0].Matches[0].Preview);
        AssertEqual("p2", result.Operations[1].Matches[0].Id);
        AssertEqual("目    录", result.Operations[1].Matches[0].Preview);
    }

    static void CliRunRolePolicyTargetHonorsAfterHeadingPosition()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            RolePolicies =
            [
                new ProfileRolePolicy
                {
                    Role = "heading1",
                    AppliesTo = "paragraph",
                    Priority = 100,
                    Match = new ProfileRoleMatch { TextPatterns = ["^摘要$"] }
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "after-policy",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "heading1", "position": "afterHeading", "offset": 1 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("p4", result.Operations[0].Matches[0].Id);
        AssertEqual("Abstract", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunRolePolicyTargetMatchesStyleOutlineLevels()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            RolePolicies =
            [
                new ProfileRolePolicy
                {
                    Role = "heading1",
                    AppliesTo = "paragraph",
                    Priority = 100,
                    Match = new ProfileRoleMatch { OutlineLevels = [0], TextPatterns = ["^第一章 绪论$"] }
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "style-outline-policy",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "heading1" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("p1", result.Operations[0].Matches[0].Id);
        AssertEqual("第一章 绪论", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunRolePolicyTargetMatchesParagraphFormat()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedFormatMatchDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            RolePolicies =
            [
                new ProfileRolePolicy
                {
                    Role = "body",
                    AppliesTo = "paragraph",
                    Priority = 15,
                    Match = new ProfileRoleMatch
                    {
                        TextPatterns = ["^本文.+$"],
                        Format = new ProfileRoleFormatMatch
                        {
                            StyleId = "2",
                            Alignment = "both",
                            FontSizeHalfPoints = "21",
                            Bold = false,
                            Italic = false,
                            LineSpacing = "360",
                            LineSpacingRule = "atleast",
                            FirstLineIndentTwips = new IntRangeMatch { Min = 360, Max = 560 },
                            LeftIndentTwips = new IntRangeMatch { Exact = 0 }
                        }
                    }
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "format-body",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "body" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(1, result.Operations[0].Matches.Count);
        AssertEqual("p0", result.Operations[0].Matches[0].Id);
        AssertEqual("本文围绕系统设计与实现展开研究。", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunRoleTargetUsesFormatClustersWhenPoliciesAreMissing()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedFormatMatchDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            FormatClusters =
            [
                new ProfileFormatCluster
                {
                    Id = "body-format",
                    RoleHint = "body",
                    AppliesTo = "paragraph",
                    Count = 2,
                    Confidence = 0.8,
                    Match = new ProfileRoleMatch
                    {
                        Format = new ProfileRoleFormatMatch
                        {
                            StyleId = "2",
                            Alignment = "both",
                            FontSizeHalfPoints = "21",
                            Bold = false,
                            Italic = false,
                            LineSpacing = "360",
                            LineSpacingRule = "atleast",
                            FirstLineIndentTwips = new IntRangeMatch { Exact = 420 },
                            LeftIndentTwips = new IntRangeMatch { Exact = 0 }
                        }
                    },
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        Alignment = "both",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        FirstLineIndentTwips = 420,
                        LeftIndentTwips = 0,
                        RunFormat = new RunFormatSample { Bold = false, Italic = false, FontSizeHalfPoints = "21" }
                    }
                },
                new ProfileFormatCluster
                {
                    Id = "unknown-format",
                    RoleHint = "unknown",
                    AppliesTo = "paragraph",
                    Count = 2,
                    Confidence = 0.9,
                    Match = new ProfileRoleMatch
                    {
                        Format = new ProfileRoleFormatMatch
                        {
                            StyleId = "2",
                            Alignment = "both",
                            FontSizeHalfPoints = "24",
                            Bold = false,
                            LineSpacing = "360",
                            LineSpacingRule = "atleast",
                            FirstLineIndentTwips = new IntRangeMatch { Exact = 420 }
                        }
                    },
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        Alignment = "both",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        FirstLineIndentTwips = 420,
                        RunFormat = new RunFormatSample { Bold = false, FontSizeHalfPoints = "24" }
                    }
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "cluster-body",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "body" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(1, result.Operations[0].Matches.Count);
        AssertEqual("p0", result.Operations[0].Matches[0].Id);
        AssertEqual("本文围绕系统设计与实现展开研究。", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunRoleTargetUsesHeadingFormatCluster()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedFormatMatchDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            FormatClusters =
            [
                new ProfileFormatCluster
                {
                    Id = "heading2-format",
                    RoleHint = "heading2",
                    AppliesTo = "paragraph",
                    Count = 2,
                    Confidence = 0.78,
                    Match = new ProfileRoleMatch
                    {
                        Format = new ProfileRoleFormatMatch
                        {
                            StyleId = "2",
                            Alignment = "center",
                            FontSizeHalfPoints = "21",
                            Bold = true,
                            Italic = false,
                            LineSpacing = "360",
                            LineSpacingRule = "atleast",
                            FirstLineIndentTwips = new IntRangeMatch { Exact = 0 }
                        }
                    },
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        Alignment = "center",
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        FirstLineIndentTwips = 0,
                        RunFormat = new RunFormatSample { Bold = true, Italic = false, FontSizeHalfPoints = "21" }
                    }
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "cluster-heading2",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "heading2" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(1, result.Operations[0].Matches.Count);
        AssertEqual("p1", result.Operations[0].Matches[0].Id);
        AssertEqual("本文围绕标题样式展开说明。", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunProfileOverridesRoleAliasesResolveProfileRole()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteResolverProfile(context, includeAmbiguousZhEvidence: false);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "profileOverrides": {
                "roleAliases": {
                  "zhAbstract": "abstract.zh"
                }
              },
              "operations": [
                {
                  "id": "find-role-alias",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "zhAbstract" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("p3", result.Operations[0].Matches[0].Id);
        AssertEqual("摘要", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunRoleTargetMergesMultipleMatchingProfileEntries()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
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
                        new ProfileParagraphEvidence { ParagraphIndex = 3, StyleId = "Heading1", TextPreview = "摘要" }
                    ]
                },
                new ProfileStyleRole
                {
                    Role = "abstract.zh",
                    StyleId = "Heading1",
                    Evidence =
                    [
                        new ProfileParagraphEvidence { ParagraphIndex = 4, StyleId = "Heading1", TextPreview = "Abstract" }
                    ]
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-role",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "abstract.zh" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual(2, result.Operations[0].Matches.Count);
        AssertEqual("p3", result.Operations[0].Matches[0].Id);
        AssertEqual("p4", result.Operations[0].Matches[1].Id);
    }

    static void CliRunRoleAfterHeadingResolvesShiftedParagraph()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteResolverProfile(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "options": {
                "requireSingleMatch": false
              },
              "operations": [
                {
                  "id": "after-role",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "abstract.zh", "position": "afterHeading", "offset": 1 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("p4", result.Operations[0].Matches[0].Id);
        AssertEqual("Abstract", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunSectionRangeResolvesParagraphsBetweenRoleAnchors()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteResolverProfile(context, includeAmbiguousZhEvidence: false);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "range",
                  "op": "resolveTarget",
                  "target": {
                    "type": "sectionRange",
                    "start": { "type": "role", "role": "abstract.zh" },
                    "end": { "type": "role", "role": "toc" },
                    "includeStart": false,
                    "includeEnd": false
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual(1, result.Operations[0].Matches.Count);
        AssertEqual("p4", result.Operations[0].Matches[0].Id);
        AssertEqual("Abstract", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunSectionRangeRejectsAmbiguousRoleAnchor()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteResolverProfile(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "range",
                  "op": "resolveTarget",
                  "target": {
                    "type": "sectionRange",
                    "start": { "type": "role", "role": "abstract.zh" },
                    "end": { "type": "role", "role": "toc" }
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("range_anchor_ambiguous", result.Operations[0].Reason);
    }

    static void CliRunParagraphTextRegexResolvesChapterHeadings()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "chapter",
                  "op": "resolveTarget",
                  "target": { "type": "paragraphText", "text": "^第[一二三四五六七八九十]+章", "match": "regex" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("p1", result.Operations[0].Matches[0].Id);
        AssertEqual("第一章 绪论", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunResolvesAdvancedTargets()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedFormatMatchDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "paragraph-id",
                  "op": "resolveTarget",
                  "target": { "type": "paragraphId", "id": "p1" }
                },
                {
                  "id": "heading-path",
                  "op": "resolveTarget",
                  "target": { "type": "headingPath", "path": ["本文围绕标题样式展开说明。"] }
                },
                {
                  "id": "within",
                  "op": "resolveTarget",
                  "target": {
                    "type": "within",
                    "scope": {
                      "type": "sectionRange",
                      "start": { "type": "paragraphIndex", "index": 0 },
                      "end": { "type": "paragraphIndex", "index": 2 },
                      "includeStart": true,
                      "includeEnd": true
                    },
                    "target": { "type": "paragraphText", "text": "字号差异", "match": "contains" }
                  }
                },
                {
                  "id": "format-target",
                  "op": "resolveTarget",
                  "target": {
                    "type": "format",
                    "format": {
                      "alignment": "center",
                      "bold": true,
                      "fontSizeHalfPoints": "21"
                    }
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("p1", result.Operations[0].Matches[0].Id);
        AssertEqual("本文围绕标题样式展开说明。", result.Operations[1].Matches[0].Preview);
        AssertEqual("p2", result.Operations[2].Matches[0].Id);
        AssertEqual("p1", result.Operations[3].Matches[0].Id);
    }

    static void CliRunResolveTargetFindsTableCells()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "cell",
                  "op": "resolveTarget",
                  "target": { "type": "tableCell", "tableIndex": 0, "rowIndex": 1, "cellIndex": 1 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("t0:r1:c1", result.Operations[0].Matches[0].Id);
        AssertEqual("tableCell", result.Operations[0].Matches[0].Type);
        AssertEqual("B2", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunRequireSingleMatchBlocksAmbiguousStyleTarget()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "options": { "requireSingleMatch": true },
              "operations": [
                {
                  "id": "ambiguous",
                  "op": "resolveTarget",
                  "target": { "type": "styleId", "styleId": "Heading1" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("target_ambiguous", result.Operations[0].Reason);
    }

    static void CliRunWrongTypedSectionRangeAnchorReturnsOperationDiagnostic()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "bad-range",
                  "op": "resolveTarget",
                  "target": {
                    "type": "sectionRange",
                    "start": { "type": "paragraphIndex", "index": "bad" },
                    "end": { "type": "paragraphIndex", "index": 2 }
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("target_value_invalid", result.Operations[0].Reason);
    }

    static void CliRunRefusesReplacingComplexParagraphStructure()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        InjectHyperlinkIntoFirstParagraph(context.Paths.WorkingDocument);
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-complex",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "replace-complex",
                  "op": "replaceParagraphText",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "text": "should not replace"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("paragraph_structure_unsupported", result.Operations[0].Reason);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

}
