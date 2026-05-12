internal static partial class Program
{
    static void CliInspectSupportsDirectDocxDocuments()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        WriteFixtureDocx(docx);

        var (exitCode, result) = RunCli(["inspect", "--doc", docx]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(docx), result.Document);
        AssertEqual(Path.GetFullPath(docx), result.DocumentMap!.Path);
        AssertEqual(true, result.DocumentMap.Paragraphs.Count > 0);
        AssertEqual(true, result.DocumentMap.Tables.Count > 0);
    }

    static void CliInspectExtractsCommentsAndRequirementHints()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "comments.docx");
        WriteCommentedRulesFixtureDocx(docx);

        var (exitCode, result) = RunCli(["inspect", "--doc", docx]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.DocumentMap!.Comments.Any(comment =>
            comment.Text.Contains("正文首行缩进", StringComparison.Ordinal)));
        AssertEqual(true, result.DocumentMap.RequirementHints.Any(hint =>
            hint.Source == "comment"
            && hint.Text.Contains("三线表", StringComparison.Ordinal)));
        AssertEqual(true, result.DocumentMap.RequirementHints.Any(hint =>
            hint.Source == "paragraph"
            && hint.Text.Contains("格式要求", StringComparison.Ordinal)));
    }

    static void CliRulesMergeAppliesProjectRulesOverProfile()
    {
        using var temp = new TempDirectory();
        var profilePath = Path.Combine(temp.Path, "profile.json");
        var projectPath = Path.Combine(temp.Path, "project-rules.json");
        var outputPath = Path.Combine(temp.Path, "final-rules.json");
        var profile = new TemplateProfile
        {
            SourceType = "template",
            SourceDocument = "template.docx",
            PageSetup = new ProfilePageSetup
            {
                Margins = new PageMarginInfo { LeftTwips = 1800, RightTwips = 1800 }
            },
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "body",
                    StyleId = "Normal",
                    Confidence = 0.7,
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Normal",
                        FirstLineIndentTwips = 420,
                        RunFormat = new RunFormatSample { FontSizeHalfPoints = "21" }
                    }
                }
            ],
            TablePolicy = new ProfileTablePolicy
            {
                Detected = true,
                TableCount = 1,
                Default = new ProfileTableSample
                {
                    RowCount = 2,
                    CellCounts = [2],
                    Format = new TableFormatSample { WidthTwips = 7200 }
                }
            }
        };
        File.WriteAllText(profilePath, ThesisJson.Serialize(profile));
        File.WriteAllText(
            projectPath,
            """
            {
              "schemaVersion": "1.0",
              "rulesKind": "projectRules",
              "roleAliases": { "mainBody": "body" },
              "pageSetup": {
                "margins": { "leftTwips": 1701 }
              },
              "roleFormats": {
                "body": {
                  "firstLineIndentTwips": 480,
                  "lineSpacing": "360",
                  "fontSizeHalfPoints": "24",
                  "eastAsiaFont": "宋体"
                }
              },
              "tableDefault": {
                "widthTwips": 8307,
                "borders": {
                  "top": { "value": "single", "size": "12", "color": "000000" },
                  "left": { "value": "none" }
                }
              },
              "diagnostics": [
                { "severity": "info", "code": "ai_rule", "message": "AI补充规则" }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["rules", "merge", "--profile", profilePath, "--project", projectPath, "--out", outputPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(outputPath), result.OutputPath);

        var merged = ThesisJson.Deserialize<TemplateProfile>(File.ReadAllText(outputPath));
        AssertEqual(1701, merged.PageSetup.Margins!.LeftTwips);
        AssertEqual("mainBody", merged.RoleAliases[0].Alias);
        AssertEqual("body", merged.RoleAliases[0].Role);
        var body = merged.StyleRoles.Single(role => role.Role == "body");
        AssertEqual(480, body.Format!.FirstLineIndentTwips);
        AssertEqual("360", body.Format.LineSpacing);
        AssertEqual("24", body.Format.RunFormat!.FontSizeHalfPoints);
        AssertEqual("宋体", body.Format.RunFormat.EastAsiaFont);
        var bodyPolicy = merged.RolePolicies.Single(policy => policy.Role == "body");
        AssertEqual(200, bodyPolicy.Priority);
        AssertEqual(480, bodyPolicy.Match.Format!.FirstLineIndentTwips!.Exact);
        AssertEqual("360", bodyPolicy.Match.Format.LineSpacing);
        AssertEqual("24", bodyPolicy.Match.Format.FontSizeHalfPoints);
        AssertEqual(8307, merged.TablePolicy.Default!.Format!.WidthTwips);
        AssertEqual("single", merged.TablePolicy.Default.Format.Borders!.Top!.Value);
        AssertEqual("none", merged.TablePolicy.Default.Format.Borders.Left!.Value);
        AssertEqual(true, merged.Diagnostics.Any(diagnostic => diagnostic.Code == "ai_rule"));
    }

    static void CliRulesMergeValidatesProjectRulesAndNormalizesNullContainers()
    {
        using var temp = new TempDirectory();
        var profilePath = Path.Combine(temp.Path, "profile.json");
        var invalidProjectPath = Path.Combine(temp.Path, "invalid-project-rules.json");
        var nullProjectPath = Path.Combine(temp.Path, "null-project-rules.json");
        var invalidOutputPath = Path.Combine(temp.Path, "invalid-final-rules.json");
        var nullOutputPath = Path.Combine(temp.Path, "null-final-rules.json");

        File.WriteAllText(profilePath, ThesisJson.Serialize(new TemplateProfile()));
        File.WriteAllText(invalidProjectPath, """{"rulesKind":"templateProfile"}""");
        File.WriteAllText(
            nullProjectPath,
            """
            {
              "schemaVersion": "1.0",
              "rulesKind": "projectRules",
              "roleAliases": null,
              "roleFormats": null,
              "rolePolicies": null,
              "tableArchetypes": null,
              "diagnostics": null
            }
            """);

        var invalid = RunCli(["rules", "merge", "--profile", profilePath, "--project", invalidProjectPath, "--out", invalidOutputPath]);
        var normalized = RunCli(["rules", "merge", "--profile", profilePath, "--project", nullProjectPath, "--out", nullOutputPath]);

        AssertEqual(1, invalid.ExitCode);
        AssertEqual("project_rules_invalid", invalid.Result.Diagnostics[0].Code);
        AssertEqual(0, normalized.ExitCode);
        AssertEqual("success", normalized.Result.Status);
        AssertEqual(true, File.Exists(nullOutputPath));
    }
}
