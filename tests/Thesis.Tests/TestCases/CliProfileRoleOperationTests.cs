internal static partial class Program
{
    static void CliRunDryRunPreviewsApplyProfileRoleWithoutChangingDocx()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "apply-role",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("preview", result.Operations[0].Status);
        AssertEqual("p1", result.Operations[0].Matches[0].Id);
        AssertContains(result.Operations[0].Matches[0].PreviewAfter!, "\"alignment\":\"center\"");
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunDryRunPreviewsActualApplyProfileRoleAfterFormat()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var setupPath = Path.Combine(temp.Path, "setup.json");
        File.WriteAllText(
            setupPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "setup-format",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "format": {
                    "alignment": "right",
                    "spacingBeforeTwips": 360,
                    "fontSizeHalfPoints": "30"
                  }
                }
              ]
            }
            """);
        AssertEqual(0, RunCli(["run", "--workspace", context.Workspace, "--request", setupPath]).ExitCode);

        WriteProfileWithAbstractFormat(context);
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "preview-after",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("preview", result.Operations[0].Status);
        var previewAfter = result.Operations[0].Matches[0].PreviewAfter!;
        AssertContains(previewAfter, "\"alignment\":\"center\"");
        AssertContains(previewAfter, "\"spacingBeforeTwips\":360");
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunApplyProfileRoleReturnsRoleNotFound()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "missing-role",
                  "op": "applyProfileRole",
                  "role": "body",
                  "target": { "type": "paragraphIndex", "index": 1 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("role_not_found", result.Operations[0].Reason);
    }

    static void CliRunApplyProfileRoleRejectsTableTarget()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "bad-table",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "tableIndex", "index": 0 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("target_type_unsupported", result.Operations[0].Reason);
    }

    static void CliRunApplyProfileRoleReturnsFormatMissing()
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
                  "id": "format-missing",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("profile_role_format_missing", result.Operations[0].Reason);
        AssertEqual("profile_role_format_missing", result.Diagnostics[0].Code);
    }

    static void CliRunExecuteAppliesProfileRoleFormatting()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-apply-role",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "apply-role",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("Heading1", map.Paragraphs[1].Format.StyleId);
        AssertEqual("center", map.Paragraphs[1].Format.Alignment);
        AssertEqual(120, map.Paragraphs[1].Format.SpacingAfterTwips);
        var runFormat = map.Paragraphs[1].Format.RunFormat ?? throw new UnreachableException("Expected run format.");
        AssertEqual(true, runFormat.Bold);
        AssertEqual("28", runFormat.FontSizeHalfPoints);
        AssertEqual("黑体", runFormat.EastAsiaFont);
    }

    static void CliRunApplyProfileRoleUsesRolePolicyFormat()
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
                    Role = "heading1",
                    StyleId = "Heading1",
                    Evidence =
                    [
                        new ProfileParagraphEvidence { ParagraphIndex = 1, StyleId = "Heading1", TextPreview = "第一章 绪论" }
                    ]
                }
            ],
            RolePolicies =
            [
                new ProfileRolePolicy
                {
                    Role = "heading1",
                    AppliesTo = "paragraph",
                    Priority = 95,
                    Match = new ProfileRoleMatch { TextPatterns = [@"^第.+章"] },
                    Format = new ParagraphFormatSample
                    {
                        Alignment = "center",
                        SpacingBeforeTwips = 240,
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        RunFormat = new RunFormatSample
                        {
                            Bold = true,
                            FontSizeHalfPoints = "32",
                            EastAsiaFont = "宋体"
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
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "apply-policy-role",
                  "op": "applyProfileRole",
                  "role": "heading1",
                  "target": { "type": "paragraphIndex", "index": 1 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("center", map.Paragraphs[1].Format.Alignment);
        AssertEqual(240, map.Paragraphs[1].Format.SpacingBeforeTwips);
        AssertEqual("360", map.Paragraphs[1].Format.LineSpacing);
        var runFormat = map.Paragraphs[1].Format.RunFormat ?? throw new UnreachableException("Expected run format.");
        AssertEqual(true, runFormat.Bold);
        AssertEqual("32", runFormat.FontSizeHalfPoints);
        AssertEqual("宋体", runFormat.EastAsiaFont);
    }

    static void CliRunApplyProfileRoleUsesFormatClusterFormat()
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
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "2",
                        Alignment = "center",
                        SpacingBeforeTwips = 240,
                        LineSpacing = "360",
                        LineSpacingRule = "atleast",
                        FirstLineIndentTwips = 0,
                        RunFormat = new RunFormatSample
                        {
                            Bold = true,
                            Italic = false,
                            FontSizeHalfPoints = "21",
                            EastAsiaFont = "宋体"
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
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "apply-cluster-role",
                  "op": "applyProfileRole",
                  "role": "heading2",
                  "target": { "type": "paragraphIndex", "index": 0 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("center", map.Paragraphs[0].Format.Alignment);
        AssertEqual(240, map.Paragraphs[0].Format.SpacingBeforeTwips);
        AssertEqual(0, map.Paragraphs[0].Format.FirstLineIndentTwips);
        var runFormat = map.Paragraphs[0].Format.RunFormat ?? throw new UnreachableException("Expected run format.");
        AssertEqual(true, runFormat.Bold);
        AssertEqual("21", runFormat.FontSizeHalfPoints);
        AssertEqual("宋体", runFormat.EastAsiaFont);
    }

    static void CliRunApplyProfileRoleFormatOverridesProfileValues()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "apply-role",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "format": {
                    "alignment": "left",
                    "fontSizeHalfPoints": "32",
                    "bold": false
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("left", map.Paragraphs[1].Format.Alignment);
        var runFormat = map.Paragraphs[1].Format.RunFormat ?? throw new UnreachableException("Expected run format.");
        AssertEqual(false, runFormat.Bold);
        AssertEqual("32", runFormat.FontSizeHalfPoints);
        AssertEqual("黑体", runFormat.EastAsiaFont);
    }

    static void CliRunApplyProfileRoleUsesProfileOverrideRoleAliases()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
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
                  "id": "apply-role-alias",
                  "op": "applyProfileRole",
                  "role": "zhAbstract",
                  "target": { "type": "paragraphIndex", "index": 1 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("preview", result.Operations[0].Status);
        AssertContains(result.Operations[0].Matches[0].PreviewAfter!, "\"alignment\":\"center\"");
    }

    static void CliRunApplyProfileRoleRejectsInvalidOverrideFormat()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "bad-format",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "format": []
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("target_value_invalid", result.Operations[0].Reason);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunApplyProfileRoleRejectsInvalidOverrideStyle()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "bad-style",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "format": {
                    "styleId": "MissingStyle"
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("paragraph_style_missing", result.Operations[0].Reason);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunApplyProfileRoleRejectsInvalidOverrideFontSize()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "bad-size",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "format": {
                    "fontSizeHalfPoints": "large"
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("font_size_invalid", result.Operations[0].Reason);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunApplyProfileRoleRejectsInvalidOverrideValuesInDryRun()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "bad-alignment",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "format": {
                    "alignment": "sideways"
                  }
                },
                {
                  "id": "bad-indent",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "format": {
                    "leftIndentTwips": -1
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("format_value_invalid", result.Operations[0].Reason);
        AssertEqual(1, result.Operations.Count);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunApplyProfileRoleAcceptsExtractedLowercaseEnumValues()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "lowercase-enums",
                  "op": "applyProfileRole",
                  "role": "abstract.zh",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "format": {
                    "alignment": "mediumkashida",
                    "lineSpacing": "360",
                    "lineSpacingRule": "atleast"
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("mediumkashida", map.Paragraphs[1].Format.Alignment);
        AssertEqual("atleast", map.Paragraphs[1].Format.LineSpacingRule);
    }

}
