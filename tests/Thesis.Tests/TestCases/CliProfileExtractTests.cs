internal static partial class Program
{
    static void CliProfileExtractWritesTemplateProfileFromDocx()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var outputPath = Path.Combine(temp.Path, "profile.json");
        WriteFixtureDocx(docx);

        var (exitCode, output) = RunCliRaw(["profile", "extract", "--doc", docx, "--out", outputPath]);
        var result = ThesisJson.Deserialize<CliResult>(output);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(docx), result.Document);
        AssertEqual(Path.GetFullPath(outputPath), result.OutputPath);
        AssertEqual(true, File.Exists(outputPath));

        var profileJson = File.ReadAllText(outputPath);
        AssertContains(profileJson, "\"profileKind\":\"templateProfile\"");
        AssertContains(profileJson, "\"sourceDocument\"");
        AssertContains(profileJson, "\"pageSetup\"");
        AssertContains(profileJson, "\"styleRoles\"");
        AssertContains(profileJson, "\"role\":\"heading1\"");
        AssertContains(profileJson, "\"role\":\"body\"");
        AssertContains(profileJson, "\"role\":\"abstract.zh\"");
        AssertContains(profileJson, "\"role\":\"toc\"");
        AssertContains(profileJson, "\"role\":\"references\"");
        AssertContains(profileJson, "\"styleId\":\"Heading1\"");
        AssertContains(profileJson, "\"rolePolicies\"");
        AssertContains(profileJson, "\"appliesTo\":\"paragraph\"");
        AssertContains(profileJson, "\"tableArchetypes\"");
        AssertContains(profileJson, "\"diagnostics\"");
        AssertContains(profileJson, "\"numberingPolicy\"");
        AssertContains(profileJson, "\"abstractNumberingId\":\"0\"");
        AssertContains(profileJson, "\"format\":\"decimal\"");
        AssertContains(profileJson, "\"tablePolicy\"");
        AssertContains(profileJson, "\"requiresFinalization\":true");
        AssertContains(profileJson, "\"sourceEvidence\"");
    }

    static void CliProfileExtractSupportsWorkspaceWorkingDocument()
    {
        using var temp = new TempDirectory();
        var sourceDoc = Path.Combine(temp.Path, "source.docx");
        var profile = Path.Combine(temp.Path, "input-profile.json");
        var workspace = Path.Combine(temp.Path, ".thesis");
        var outputPath = Path.Combine(temp.Path, "workspace-profile.json");

        WriteFixtureDocx(sourceDoc);
        File.WriteAllText(profile, "{}");
        AssertEqual("success", SessionInitializer.Initialize(sourceDoc, profile, workspace).Status);

        var (exitCode, result) = RunCli(["profile", "extract", "--workspace", workspace, "--out", outputPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.Combine(Path.GetFullPath(workspace), "working.docx"), result.Document);
        AssertEqual(Path.GetFullPath(outputPath), result.OutputPath);
        AssertContains(File.ReadAllText(outputPath), "\"sourceType\":\"workspace\"");
    }

    static void CliProfileExtractValidatesSourceAndOutputOptions()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var profile = Path.Combine(temp.Path, "input-profile.json");
        var workspace = Path.Combine(temp.Path, ".thesis");
        WriteFixtureDocx(docx);
        File.WriteAllText(profile, "{}");
        AssertEqual("success", SessionInitializer.Initialize(docx, profile, workspace).Status);

        var bothSources = RunCli(["profile", "extract", "--doc", docx, "--workspace", workspace, "--out", Path.Combine(temp.Path, "both.json")]);
        AssertEqual(1, bothSources.ExitCode);
        AssertEqual("error", bothSources.Result.Status);
        AssertEqual("profile_source_ambiguous", bothSources.Result.Diagnostics[0].Code);

        var missingOutputParent = Path.Combine(temp.Path, "missing", "profile.json");
        var missingParent = RunCli(["profile", "extract", "--doc", docx, "--out", missingOutputParent]);
        AssertEqual(1, missingParent.ExitCode);
        AssertEqual("error", missingParent.Result.Status);
        AssertEqual("profile_output_directory_missing", missingParent.Result.Diagnostics[0].Code);
        AssertEqual(false, File.Exists(missingOutputParent));
    }

    static void CliProfileExtractRefusesUnsafeOutputPaths()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var profile = Path.Combine(temp.Path, "input-profile.json");
        var workspace = Path.Combine(temp.Path, ".thesis");
        WriteFixtureDocx(docx);
        File.WriteAllText(profile, "{}");
        AssertEqual("success", SessionInitializer.Initialize(docx, profile, workspace).Status);
        var paths = SessionPaths.FromWorkspace(workspace);

        var sourceBefore = File.ReadAllBytes(docx);
        var sourceOverwrite = RunCli(["profile", "extract", "--doc", docx, "--out", docx]);
        AssertEqual(1, sourceOverwrite.ExitCode);
        AssertEqual("profile_output_refused", sourceOverwrite.Result.Diagnostics[0].Code);
        AssertBytesEqual(sourceBefore, File.ReadAllBytes(docx));

        foreach (var output in new[] { paths.WorkingDocument, paths.ProfileJson, paths.SessionJson, Path.Combine(paths.CacheDirectory, "profile.json") })
        {
            var result = RunCli(["profile", "extract", "--workspace", workspace, "--out", output]);
            AssertEqual(1, result.ExitCode);
            AssertEqual("profile_output_refused", result.Result.Diagnostics[0].Code);
        }
    }

    static void CliProfileExtractReturnsJsonErrorForNonDocxInput()
    {
        using var temp = new TempDirectory();
        var notDocx = Path.Combine(temp.Path, "not-docx.docx");
        var outputPath = Path.Combine(temp.Path, "profile.json");
        File.WriteAllText(notDocx, "not a docx");

        var (exitCode, result) = RunCli(["profile", "extract", "--doc", notDocx, "--out", outputPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("document_map_unavailable", result.Diagnostics[0].Code);
        AssertEqual(Path.GetFullPath(notDocx), result.Diagnostics[0].Path);
        AssertEqual(false, File.Exists(outputPath));
    }

    static void CliProfileExplainSummarizesRulesAndRisks()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var profilePath = Path.Combine(temp.Path, "profile.json");
        WriteFixtureDocx(docx);
        AssertEqual(0, RunCli(["profile", "extract", "--doc", docx, "--out", profilePath]).ExitCode);

        var (exitCode, result) = RunCli(["profile", "explain", "--profile", profilePath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(profilePath), result.ProfileExplanation!.ProfilePath);
        AssertEqual("doc", result.ProfileExplanation.SourceType);
        AssertEqual(true, result.ProfileExplanation.SourceEvidence.ParagraphCount > 0);
        AssertEqual(true, result.ProfileExplanation.RoleSummaries.Any(role =>
            role.Role == "abstract.zh"
            && role.EvidenceCount > 0
            && role.HasFormat));
        AssertEqual(true, result.ProfileExplanation.TableSummary.Detected);
        AssertEqual(1, result.ProfileExplanation.TableSummary.TableCount);
        AssertEqual(true, result.ProfileExplanation.Risks.Any(risk =>
            risk.Code == "finalization_required"
            && risk.Severity == "warning"));
    }

    static void CliProfileExplainSupportsWorkspaceProfile()
    {
        using var temp = new TempDirectory();
        var sourceDoc = Path.Combine(temp.Path, "source.docx");
        var profile = Path.Combine(temp.Path, "input-profile.json");
        var workspace = Path.Combine(temp.Path, ".thesis");
        WriteFixtureDocx(sourceDoc);
        File.WriteAllText(profile, "{}");
        AssertEqual("success", SessionInitializer.Initialize(sourceDoc, profile, workspace).Status);
        var profileModel = new TemplateProfile
        {
            SourceType = "test",
            SourceDocument = sourceDoc,
            SourceEvidence = new ProfileSourceEvidence { ParagraphCount = 2, StyleCount = 1 },
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "body",
                    StyleId = "Normal",
                    Confidence = 0.9,
                    Format = new ParagraphFormatSample { StyleId = "Normal" },
                    Evidence =
                    [
                        new ProfileParagraphEvidence { ParagraphIndex = 0, TextPreview = "正文" }
                    ]
                }
            ]
        };
        File.WriteAllText(SessionPaths.FromWorkspace(workspace).ProfileJson, ThesisJson.Serialize(profileModel));

        var (exitCode, result) = RunCli(["profile", "explain", "--workspace", workspace]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(SessionPaths.FromWorkspace(workspace).ProfileJson, result.ProfileExplanation!.ProfilePath);
        AssertEqual("test", result.ProfileExplanation.SourceType);
        AssertEqual("body", result.ProfileExplanation.RoleSummaries[0].Role);
    }

    static void CliProfileExplainValidatesOptionsAndJson()
    {
        using var temp = new TempDirectory();
        var malformedProfile = Path.Combine(temp.Path, "bad-profile.json");
        File.WriteAllText(malformedProfile, "{");

        var missingSource = RunCli(["profile", "explain"]);
        AssertEqual(1, missingSource.ExitCode);
        AssertEqual("profile_source_missing", missingSource.Result.Diagnostics[0].Code);

        var malformed = RunCli(["profile", "explain", "--profile", malformedProfile]);
        AssertEqual(1, malformed.ExitCode);
        AssertEqual("profile_invalid", malformed.Result.Diagnostics[0].Code);
        AssertEqual(Path.GetFullPath(malformedProfile), malformed.Result.Diagnostics[0].Path);
    }

    static void CliProfileDiffReportsStructuredChanges()
    {
        using var temp = new TempDirectory();
        var leftPath = Path.Combine(temp.Path, "left-profile.json");
        var rightPath = Path.Combine(temp.Path, "right-profile.json");
        var left = new TemplateProfile
        {
            SourceType = "left",
            PageSetup = new ProfilePageSetup
            {
                Margins = new PageMarginInfo { TopTwips = 1440, LeftTwips = 1800 }
            },
            StyleRoles =
            [
                new ProfileStyleRole { Role = "body", StyleId = "Normal", Confidence = 0.9 },
                new ProfileStyleRole { Role = "heading1", StyleId = "Heading1", Confidence = 0.8 }
            ],
            TablePolicy = new ProfileTablePolicy { Detected = true, TableCount = 1, ObservedColumnCounts = [2] },
            Diagnostics =
            [
                new ProfileDiagnostic { Severity = "warning", Code = "old_warning", Message = "old" }
            ]
        };
        var right = new TemplateProfile
        {
            SourceType = "right",
            PageSetup = new ProfilePageSetup
            {
                Margins = new PageMarginInfo { TopTwips = 1200, LeftTwips = 1800 }
            },
            StyleRoles =
            [
                new ProfileStyleRole { Role = "body", StyleId = "BodyText", Confidence = 0.7 },
                new ProfileStyleRole { Role = "references", StyleId = "Heading1", Confidence = 0.82 }
            ],
            TablePolicy = new ProfileTablePolicy { Detected = true, TableCount = 2, ObservedColumnCounts = [2, 3] },
            Diagnostics =
            [
                new ProfileDiagnostic { Severity = "warning", Code = "new_warning", Message = "new" }
            ]
        };
        File.WriteAllText(leftPath, ThesisJson.Serialize(left));
        File.WriteAllText(rightPath, ThesisJson.Serialize(right));

        var (exitCode, result) = RunCli(["profile", "diff", "--left", leftPath, "--right", rightPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.GetFullPath(leftPath), result.ProfileDiff!.LeftProfilePath);
        AssertEqual(Path.GetFullPath(rightPath), result.ProfileDiff.RightProfilePath);
        AssertEqual(true, result.ProfileDiff.HasChanges);
        AssertEqual(true, result.ProfileDiff.Changes.Any(change =>
            change.Kind == "modified"
            && change.Path == "pageSetup.margins.topTwips"
            && change.Left == "1440"
            && change.Right == "1200"));
        AssertEqual(true, result.ProfileDiff.Changes.Any(change =>
            change.Kind == "modified"
            && change.Path == "styleRoles.body.styleId"
            && change.Left == "Normal"
            && change.Right == "BodyText"));
        AssertEqual(true, result.ProfileDiff.Changes.Any(change =>
            change.Kind == "removed"
            && change.Path == "styleRoles.heading1"));
        AssertEqual(true, result.ProfileDiff.Changes.Any(change =>
            change.Kind == "added"
            && change.Path == "styleRoles.references"));
        AssertEqual(true, result.ProfileDiff.Changes.Any(change =>
            change.Kind == "modified"
            && change.Path == "tablePolicy.tableCount"
            && change.Left == "1"
            && change.Right == "2"));
        AssertEqual(true, result.ProfileDiff.Changes.Any(change =>
            change.Kind == "added"
            && change.Path == "diagnostics.new_warning"));
    }

    static void CliProfileDiffReturnsNoChangesForEquivalentProfiles()
    {
        using var temp = new TempDirectory();
        var leftPath = Path.Combine(temp.Path, "left-profile.json");
        var rightPath = Path.Combine(temp.Path, "right-profile.json");
        var profile = new TemplateProfile
        {
            SourceType = "test",
            StyleRoles = [new ProfileStyleRole { Role = "body", StyleId = "Normal", Confidence = 0.9 }]
        };
        File.WriteAllText(leftPath, ThesisJson.Serialize(profile));
        File.WriteAllText(rightPath, ThesisJson.Serialize(profile));

        var (exitCode, result) = RunCli(["profile", "diff", "--left", leftPath, "--right", rightPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(false, result.ProfileDiff!.HasChanges);
        AssertEqual(0, result.ProfileDiff.Changes.Count);
    }

    static void CliProfileDiffValidatesInputs()
    {
        using var temp = new TempDirectory();
        var malformedProfile = Path.Combine(temp.Path, "bad-profile.json");
        var profile = Path.Combine(temp.Path, "profile.json");
        File.WriteAllText(malformedProfile, "{");
        File.WriteAllText(profile, ThesisJson.Serialize(new TemplateProfile()));

        var missing = RunCli(["profile", "diff", "--left", profile]);
        AssertEqual(1, missing.ExitCode);
        AssertEqual("missing_option", missing.Result.Diagnostics[0].Code);

        var malformed = RunCli(["profile", "diff", "--left", malformedProfile, "--right", profile]);
        AssertEqual(1, malformed.ExitCode);
        AssertEqual("profile_invalid", malformed.Result.Diagnostics[0].Code);
        AssertEqual(Path.GetFullPath(malformedProfile), malformed.Result.Diagnostics[0].Path);
    }

}
