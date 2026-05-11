using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Thesis.Cli;
using Thesis.OpenXml;
using Thesis.Profile;
using Thesis.Schema;
using Thesis.Session;

var tests = new (string Name, Action Test)[]
{
    ("JSON roundtrip uses camelCase and enum strings", JsonRoundtripUsesCamelCaseAndEnumStrings),
    ("Template profile rules serialize as camelCase JSON", TemplateProfileRulesSerializeAsCamelCaseJson),
    ("SessionPaths resolves expected filenames", SessionPathsResolvesExpectedFilenames),
    ("SessionInitializer creates workspace files and refuses existing workspace", SessionInitializerCreatesFilesAndRefusesExistingWorkspace),
    ("SessionInitializer refuses non-empty stale workspace", SessionInitializerRefusesNonEmptyStaleWorkspace),
    ("SessionInitializer refuses locked workspace", SessionInitializerRefusesLockedWorkspace),
    ("SessionInitializer releases lock after validation errors", SessionInitializerReleasesLockAfterValidationErrors),
    ("CLI run reads request JSON and returns success JSON", CliRunReadsRequestJsonAndReturnsSuccessJson),
    ("CLI run dry-run previews micro edits without changing DOCX", CliRunDryRunPreviewsMicroEditsWithoutChangingDocx),
    ("CLI run execute can replace multiple paragraph text matches", CliRunExecuteCanReplaceMultipleParagraphTextMatches),
    ("CLI run execute applies micro edits and creates snapshot", CliRunExecuteAppliesMicroEditsAndCreatesSnapshot),
    ("CLI run execute aborts transaction on operation error", CliRunExecuteAbortsTransactionOnOperationError),
    ("CLI run wrong typed target returns operation diagnostic", CliRunWrongTypedTargetReturnsOperationDiagnostic),
    ("CLI run rejects invalid run font size", CliRunRejectsInvalidRunFontSize),
    ("CLI run resolveTarget finds paragraphs by styleId", CliRunResolveTargetFindsParagraphsByStyleId),
    ("CLI run resolveTarget is read-only in execute mode", CliRunResolveTargetIsReadOnlyInExecuteMode),
    ("CLI run execute resolveTarget does not create default snapshot", CliRunExecuteResolveTargetDoesNotCreateDefaultSnapshot),
    ("CLI run paragraph operation rejects table target", CliRunParagraphOperationRejectsTableTarget),
    ("CLI run dry-run previews applyProfileRole without changing DOCX", CliRunDryRunPreviewsApplyProfileRoleWithoutChangingDocx),
    ("CLI run dry-run previews actual applyProfileRole after format", CliRunDryRunPreviewsActualApplyProfileRoleAfterFormat),
    ("CLI run applyProfileRole returns role_not_found", CliRunApplyProfileRoleReturnsRoleNotFound),
    ("CLI run applyProfileRole rejects table target", CliRunApplyProfileRoleRejectsTableTarget),
    ("CLI run applyProfileRole returns format missing", CliRunApplyProfileRoleReturnsFormatMissing),
    ("CLI run execute applies profile role formatting", CliRunExecuteAppliesProfileRoleFormatting),
    ("CLI run applyProfileRole uses role policy format", CliRunApplyProfileRoleUsesRolePolicyFormat),
    ("CLI run applyProfileRole format overrides profile values", CliRunApplyProfileRoleFormatOverridesProfileValues),
    ("CLI run applyProfileRole uses profile override role aliases", CliRunApplyProfileRoleUsesProfileOverrideRoleAliases),
    ("CLI run applyProfileRole rejects invalid override format", CliRunApplyProfileRoleRejectsInvalidOverrideFormat),
    ("CLI run applyProfileRole rejects invalid override style", CliRunApplyProfileRoleRejectsInvalidOverrideStyle),
    ("CLI run applyProfileRole rejects invalid override font size", CliRunApplyProfileRoleRejectsInvalidOverrideFontSize),
    ("CLI run applyProfileRole rejects invalid override values in dry-run", CliRunApplyProfileRoleRejectsInvalidOverrideValuesInDryRun),
    ("CLI run applyProfileRole accepts extracted lowercase enum values", CliRunApplyProfileRoleAcceptsExtractedLowercaseEnumValues),
    ("CLI run dry-run previews applyProfileTable without changing DOCX", CliRunDryRunPreviewsApplyProfileTableWithoutChangingDocx),
    ("CLI run execute applies profile table formatting", CliRunExecuteAppliesProfileTableFormatting),
    ("CLI run applyProfileTable returns format missing", CliRunApplyProfileTableReturnsFormatMissing),
    ("CLI run execute applies table micro operations", CliRunExecuteAppliesTableMicroOperations),
    ("CLI run table border update preserves existing sides", CliRunTableBorderUpdatePreservesExistingSides),
    ("CLI run table border update can set one side on bare table", CliRunTableBorderUpdateCanSetOneSideOnBareTable),
    ("CLI run dry-run previews table cell text without changing DOCX", CliRunDryRunPreviewsTableCellTextWithoutChangingDocx),
    ("CLI run applyThreeLineTable sets academic borders", CliRunApplyThreeLineTableSetsAcademicBorders),
    ("CLI run table cell operation rejects table target", CliRunTableCellOperationRejectsTableTarget),
    ("CLI run returns profile_invalid for malformed workspace profile", CliRunReturnsProfileInvalidForMalformedWorkspaceProfile),
    ("CLI run returns profile_invalid for structurally invalid workspace profile", CliRunReturnsProfileInvalidForStructurallyInvalidWorkspaceProfile),
    ("CLI run returns profile_invalid for null role evidence", CliRunReturnsProfileInvalidForNullRoleEvidence),
    ("CLI run returns profile_invalid for null profile rule containers", CliRunReturnsProfileInvalidForNullProfileRuleContainers),
    ("CLI run resolveTarget finds role evidence from profile", CliRunResolveTargetFindsRoleEvidenceFromProfile),
    ("CLI run role target uses role policies when evidence is missing", CliRunRoleTargetUsesRolePoliciesWhenEvidenceIsMissing),
    ("CLI run role policy target honors afterHeading position", CliRunRolePolicyTargetHonorsAfterHeadingPosition),
    ("CLI run role policy target matches style outline levels", CliRunRolePolicyTargetMatchesStyleOutlineLevels),
    ("CLI run profileOverrides roleAliases resolve profile role", CliRunProfileOverridesRoleAliasesResolveProfileRole),
    ("CLI run role target merges multiple matching profile entries", CliRunRoleTargetMergesMultipleMatchingProfileEntries),
    ("CLI run role afterHeading resolves shifted paragraph", CliRunRoleAfterHeadingResolvesShiftedParagraph),
    ("CLI run sectionRange resolves paragraphs between role anchors", CliRunSectionRangeResolvesParagraphsBetweenRoleAnchors),
    ("CLI run sectionRange rejects ambiguous role anchor", CliRunSectionRangeRejectsAmbiguousRoleAnchor),
    ("CLI run paragraphText regex resolves chapter headings", CliRunParagraphTextRegexResolvesChapterHeadings),
    ("CLI run resolveTarget finds table cells", CliRunResolveTargetFindsTableCells),
    ("CLI run requireSingleMatch blocks ambiguous style target", CliRunRequireSingleMatchBlocksAmbiguousStyleTarget),
    ("CLI run wrong typed section range anchor returns operation diagnostic", CliRunWrongTypedSectionRangeAnchorReturnsOperationDiagnostic),
    ("CLI run refuses replacing complex paragraph structure", CliRunRefusesReplacingComplexParagraphStructure),
    ("CLI run execute refuses locked workspace", CliRunExecuteRefusesLockedWorkspace),
    ("CLI unknown command returns JSON error", CliUnknownCommandReturnsJsonError),
    ("Snapshot creates next copy, increments counter, and returns info", SnapshotCreatesNextCopyIncrementsCounterAndReturnsInfo),
    ("Rollback restores working document bytes", RollbackRestoresWorkingDocumentBytes),
    ("Rollback missing snapshot returns JSON error", RollbackMissingSnapshotReturnsJsonError),
    ("Export copies working document and leaves original unchanged", ExportCopiesWorkingDocumentAndLeavesOriginalUnchanged),
    ("Export to original path is refused", ExportToOriginalPathIsRefused),
    ("Export to working path is refused", ExportToWorkingPathIsRefused),
    ("Export inside workspace is refused", ExportInsideWorkspaceIsRefused),
    ("Inspect returns session info and snapshot list", InspectReturnsSessionInfoAndSnapshotList),
    ("Mutating commands refuse existing lock and inspect still works", MutatingCommandsRefuseExistingLockAndInspectStillWorks),
    ("Corrupt session returns JSON error", CorruptSessionReturnsJsonError),
    ("Tampered session paths return JSON error", TamperedSessionPathsReturnJsonError),
    ("Missing workspace files return JSON errors", MissingWorkspaceFilesReturnJsonErrors),
    ("Snapshot and rollback reject traversal identifiers", SnapshotAndRollbackRejectTraversalIdentifiers),
    ("Snapshot refuses to overwrite existing target", SnapshotRefusesToOverwriteExistingTarget),
    ("Snapshot removes orphan when session save fails", SnapshotRemovesOrphanWhenSessionSaveFails),
    ("Rollback ambiguous suffix returns JSON error", RollbackAmbiguousSuffixReturnsJsonError),
    ("Export to missing parent directory returns JSON error", ExportToMissingParentDirectoryReturnsJsonError),
    ("Inspect is read-only when lock exists", InspectIsReadOnlyWhenLockExists),
    ("OpenXml inspector reads paragraphs, styles, numbering, sections, and tables", OpenXmlInspectorReadsDocumentMap),
    ("OpenXml inspector reads paragraph and run format samples", OpenXmlInspectorReadsParagraphAndRunFormatSamples),
    ("OpenXml inspector falls back to complex script font size", OpenXmlInspectorFallsBackToComplexScriptFontSize),
    ("OpenXml inspector reads style usage and outline facts", OpenXmlInspectorReadsStyleUsageAndOutlineFacts),
    ("OpenXml inspector reads outline facts from style definitions", OpenXmlInspectorReadsOutlineFactsFromStyleDefinitions),
    ("OpenXml inspector reads table format samples", OpenXmlInspectorReadsTableFormatSamples),
    ("CLI inspect includes document map for DOCX workspaces", CliInspectIncludesDocumentMapForDocxWorkspaces),
    ("CLI inspect reports JSON warning when document map is unavailable", CliInspectReportsJsonWarningWhenDocumentMapUnavailable),
    ("Template profile builder returns typed profile with semantic roles", TemplateProfileBuilderReturnsTypedProfileWithSemanticRoles),
    ("Template profile builder copies role format samples", TemplateProfileBuilderCopiesRoleFormatSamples),
    ("Template profile builder infers role policies", TemplateProfileBuilderInfersRolePolicies),
    ("Template profile builder infers direct format roles without semantic styles", TemplateProfileBuilderInfersDirectFormatRolesWithoutSemanticStyles),
    ("Template profile builder copies table format samples", TemplateProfileBuilderCopiesTableFormatSamples),
    ("Template profile builder infers three-line table archetype", TemplateProfileBuilderInfersThreeLineTableArchetype),
    ("Template profile builder reports weak profile diagnostics", TemplateProfileBuilderReportsWeakProfileDiagnostics),
    ("CLI profile extract writes template profile from DOCX", CliProfileExtractWritesTemplateProfileFromDocx),
    ("CLI profile extract supports workspace working document", CliProfileExtractSupportsWorkspaceWorkingDocument),
    ("CLI profile extract validates source and output options", CliProfileExtractValidatesSourceAndOutputOptions),
    ("CLI profile extract refuses unsafe output paths", CliProfileExtractRefusesUnsafeOutputPaths),
    ("CLI profile extract returns JSON error for non-DOCX input", CliProfileExtractReturnsJsonErrorForNonDocxInput)
};

var failures = new List<string>();
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine(ex);
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"{failures.Count} test(s) failed.");
    Environment.Exit(1);
}

static void JsonRoundtripUsesCamelCaseAndEnumStrings()
{
    var request = new OperationRequest
    {
        SchemaVersion = "1.0",
        RequestId = "fix-abstract-001",
        Mode = RequestMode.ValidateOnly,
        Operations =
        [
            new ThesisOperation
            {
                Id = "op-001",
                Op = "replaceParagraph",
                Target = JsonNode.Parse("""{"type":"role","role":"abstract.zh.body"}"""),
                Text = "updated"
            }
        ]
    };

    var json = ThesisJson.Serialize(request);

    AssertContains(json, "\"schemaVersion\"");
    AssertContains(json, "\"requestId\"");
    AssertContains(json, "\"mode\":\"validateOnly\"");
    AssertDoesNotContain(json, "SchemaVersion");

    var roundtrip = ThesisJson.Deserialize<OperationRequest>(json);
    AssertEqual(RequestMode.ValidateOnly, roundtrip.Mode);
    AssertEqual("op-001", roundtrip.Operations[0].Id);
    AssertEqual(true, roundtrip.Options.CreateSnapshot);
    AssertEqual(true, roundtrip.Options.StopOnError);
    AssertEqual(false, roundtrip.Options.RequireSingleMatch);
    AssertEqual(false, roundtrip.Options.TrackChanges);
}

static void TemplateProfileRulesSerializeAsCamelCaseJson()
{
    var profile = new TemplateProfile
    {
        RolePolicies =
        [
            new ProfileRolePolicy
            {
                Role = "heading1",
                AppliesTo = "paragraph",
                Priority = 100,
                Match = new ProfileRoleMatch
                {
                    StyleIds = ["Heading1"],
                    TextPatterns = ["^第.+章"],
                    OutlineLevels = [0]
                },
                Format = new ParagraphFormatSample
                {
                    Alignment = "center",
                    RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "28" }
                }
            }
        ],
        TableArchetypes =
        [
            new ProfileTableArchetype
            {
                Name = "threeLine",
                Confidence = 0.91,
                Match = new ProfileTableMatch { MinRows = 2, ColumnCounts = [2, 3] },
                Format = new TableFormatSample
                {
                    Borders = new TableBordersSample
                    {
                        Top = new TableBorderLineSample { Value = "single", Size = "12" },
                        InsideVertical = new TableBorderLineSample { Value = "nil" }
                    }
                }
            }
        ],
        Diagnostics =
        [
            new ProfileDiagnostic
            {
                Severity = "info",
                Code = "profile_rule_inferred",
                Message = "heading1 policy inferred from style usage",
                Evidence = ["style:Heading1", "paragraph:p1"]
            }
        ]
    };

    var json = ThesisJson.Serialize(profile);

    AssertContains(json, "\"rolePolicies\"");
    AssertContains(json, "\"appliesTo\":\"paragraph\"");
    AssertContains(json, "\"outlineLevels\":[0]");
    AssertContains(json, "\"tableArchetypes\"");
    AssertContains(json, "\"diagnostics\"");
}

static void SessionPathsResolvesExpectedFilenames()
{
    var root = Path.Combine(Path.GetTempPath(), "thesis-tests", Guid.NewGuid().ToString("N"));
    var paths = SessionPaths.FromWorkspace(root);

    AssertEqual(Path.GetFullPath(root), paths.Workspace);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "session.json"), paths.SessionJson);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "profile.json"), paths.ProfileJson);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "working.docx"), paths.WorkingDocument);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "session.lock"), paths.LockFile);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "snapshots"), paths.SnapshotsDirectory);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "logs"), paths.LogsDirectory);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "cache"), paths.CacheDirectory);
}

static void SessionInitializerCreatesFilesAndRefusesExistingWorkspace()
{
    using var temp = new TempDirectory();
    var sourceDoc = Path.Combine(temp.Path, "source.docx");
    var profile = Path.Combine(temp.Path, "input-profile.json");
    var workspace = Path.Combine(temp.Path, ".thesis");

    File.WriteAllText(sourceDoc, "doc");
    File.WriteAllText(profile, "{}");

    var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);
    var paths = SessionPaths.FromWorkspace(workspace);

    AssertEqual("success", result.Status);
    AssertEqual(true, File.Exists(paths.WorkingDocument));
    AssertEqual(true, File.Exists(paths.ProfileJson));
    AssertEqual(true, File.Exists(paths.SessionJson));
    AssertEqual(true, File.Exists(Path.Combine(paths.SnapshotsDirectory, "0001-init.docx")));
    AssertEqual("doc", File.ReadAllText(sourceDoc));

    var sessionJson = ThesisJson.Deserialize<JsonObject>(File.ReadAllText(paths.SessionJson));
    AssertEqual("1.0", sessionJson["schemaVersion"]!.GetValue<string>());
    AssertEqual(Path.GetFullPath(sourceDoc), sessionJson["originalPath"]!.GetValue<string>());
    AssertEqual(paths.WorkingDocument, sessionJson["workingPath"]!.GetValue<string>());
    AssertEqual(paths.ProfileJson, sessionJson["profilePath"]!.GetValue<string>());
    AssertEqual(1, sessionJson["snapshotCounter"]!.GetValue<int>());

    var refused = SessionInitializer.Initialize(sourceDoc, profile, workspace);
    AssertEqual("error", refused.Status);
    AssertEqual(true, refused.Diagnostics.Count > 0);
}

static void SessionInitializerRefusesNonEmptyStaleWorkspace()
{
    using var temp = new TempDirectory();
    var sourceDoc = Path.Combine(temp.Path, "source.docx");
    var profile = Path.Combine(temp.Path, "input-profile.json");
    var workspace = Path.Combine(temp.Path, ".thesis");
    var paths = SessionPaths.FromWorkspace(workspace);

    Directory.CreateDirectory(workspace);
    File.WriteAllText(sourceDoc, "doc");
    File.WriteAllText(profile, "{}");
    File.WriteAllText(paths.SessionJson, "{}");

    var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);

    AssertEqual("error", result.Status);
    AssertEqual("workspace_exists", result.Diagnostics[0].Code);
    AssertEqual(false, File.Exists(paths.WorkingDocument));
}

static void SessionInitializerRefusesLockedWorkspace()
{
    using var temp = new TempDirectory();
    var sourceDoc = Path.Combine(temp.Path, "source.docx");
    var profile = Path.Combine(temp.Path, "input-profile.json");
    var workspace = Path.Combine(temp.Path, ".thesis");
    var paths = SessionPaths.FromWorkspace(workspace);

    Directory.CreateDirectory(workspace);
    File.WriteAllText(sourceDoc, "doc");
    File.WriteAllText(profile, "{}");
    File.WriteAllText(paths.LockFile, "locked");

    var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);

    AssertEqual("error", result.Status);
    AssertEqual("workspace_locked", result.Diagnostics[0].Code);
    AssertEqual(false, File.Exists(paths.WorkingDocument));
}

static void SessionInitializerReleasesLockAfterValidationErrors()
{
    using var temp = new TempDirectory();
    var missingSourceDoc = Path.Combine(temp.Path, "missing.docx");
    var profile = Path.Combine(temp.Path, "input-profile.json");
    var workspace = Path.Combine(temp.Path, ".thesis");
    var paths = SessionPaths.FromWorkspace(workspace);

    File.WriteAllText(profile, "{}");

    var result = SessionInitializer.Initialize(missingSourceDoc, profile, workspace);

    AssertEqual("error", result.Status);
    AssertEqual("source_doc_missing", result.Diagnostics[0].Code);
    AssertEqual(false, File.Exists(paths.LockFile));
}

static void CliRunReadsRequestJsonAndReturnsSuccessJson()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-123",
          "mode": "dryRun",
          "operations": []
        }
        """);

    var output = new StringWriter();
    var error = new StringWriter();
    var exitCode = ThesisCli.Run(
        ["run", "--workspace", context.Workspace, "--request", requestPath],
        output,
        error);

    AssertEqual(0, exitCode);
    var result = ThesisJson.Deserialize<CliResult>(output.ToString());
    AssertEqual("success", result.Status);
    AssertEqual("req-123", result.RequestId);
    AssertEqual(context.Workspace, result.Workspace);
    AssertEqual(context.Paths.WorkingDocument, result.Document);
}

static void CliRunDryRunPreviewsMicroEditsWithoutChangingDocx()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var before = File.ReadAllBytes(context.Paths.WorkingDocument);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-dry-run",
          "mode": "dryRun",
          "options": {
            "requireSingleMatch": true
          },
          "operations": [
            {
              "id": "replace-title",
              "op": "replaceParagraphText",
              "target": { "type": "paragraphText", "text": "中文摘要", "match": "exact" },
              "text": "中文摘要（修改后）"
            },
            {
              "id": "style-title",
              "op": "setParagraphStyle",
              "target": { "type": "paragraphIndex", "index": 0 },
              "format": { "styleId": "Heading1" }
            },
            {
              "id": "format-title-run",
              "op": "setRunFormat",
              "target": { "type": "runIndex", "paragraphIndex": 0, "runIndex": 0 },
              "format": { "bold": true, "fontSizeHalfPoints": "32" }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual("req-dry-run", result.RequestId);
    AssertEqual(RequestMode.DryRun, result.Mode);
    AssertEqual(3, result.Operations.Count);
    AssertEqual("preview", result.Operations[0].Status);
    AssertEqual("中文摘要", result.Operations[0].Matches[0].PreviewBefore);
    AssertEqual("中文摘要（修改后）", result.Operations[0].Matches[0].PreviewAfter);
    AssertEqual("preview", result.Operations[1].Status);
    AssertEqual("paragraph", result.Operations[1].Matches[0].Type);
    AssertEqual("preview", result.Operations[2].Status);
    AssertEqual("run", result.Operations[2].Matches[0].Type);
    AssertEqual(null, result.Snapshot);
    AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    AssertBytesEqual(context.OriginalBytes, File.ReadAllBytes(context.SourceDoc));

    var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
    AssertEqual("中文摘要", map.Paragraphs[0].Text);
    AssertEqual("Title", map.Paragraphs[0].StyleId);
    AssertEqual(false, map.Paragraphs[0].Runs[0].Bold);
    AssertEqual(null, map.Paragraphs[0].Runs[0].FontSizeHalfPoints);
}

static void CliRunExecuteCanReplaceMultipleParagraphTextMatches()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-multi",
          "mode": "execute",
          "options": {
            "createSnapshot": false,
            "requireSingleMatch": false
          },
          "operations": [
            {
              "id": "replace-headings",
              "op": "replaceParagraphText",
              "target": { "type": "paragraphText", "text": "摘", "match": "contains" },
              "text": "摘要标题"
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(null, result.Snapshot);
    AssertEqual(1, result.Operations.Count);
    AssertEqual("applied", result.Operations[0].Status);
    AssertEqual(2, result.Operations[0].Matches.Count);

    var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
    AssertEqual("摘要标题", map.Paragraphs[0].Text);
    AssertEqual("摘要标题", map.Paragraphs[3].Text);
    AssertBytesEqual(context.OriginalBytes, File.ReadAllBytes(context.SourceDoc));
}

static void CliRunExecuteAppliesMicroEditsAndCreatesSnapshot()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var before = File.ReadAllBytes(context.Paths.WorkingDocument);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-execute",
          "mode": "execute",
          "options": {
            "createSnapshot": true,
            "requireSingleMatch": true
          },
          "operations": [
            {
              "id": "replace-title",
              "op": "replaceParagraphText",
              "target": { "type": "paragraphIndex", "index": 0 },
              "text": "中文摘要（修改后）"
            },
            {
              "id": "style-title",
              "op": "setParagraphStyle",
              "target": { "type": "paragraphIndex", "index": 0 },
              "format": { "styleId": "Heading1" }
            },
            {
              "id": "format-title-run",
              "op": "setRunFormat",
              "target": { "type": "runIndex", "paragraphIndex": 0, "runIndex": 0 },
              "format": { "bold": true, "fontSizeHalfPoints": "32" }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(RequestMode.Execute, result.Mode);
    AssertEqual(3, result.Operations.Count);
    AssertEqual(true, result.Operations.All(operation => operation.Status == "applied"));
    AssertEqual("0002-before-run-req-execute", result.Snapshot!.Id);
    AssertEqual(true, result.Snapshot.Created);
    AssertBytesEqual(before, File.ReadAllBytes(result.Snapshot.Path!));
    AssertBytesEqual(context.OriginalBytes, File.ReadAllBytes(context.SourceDoc));

    var session = ThesisJson.Deserialize<SessionState>(File.ReadAllText(context.Paths.SessionJson));
    AssertEqual(2, session.SnapshotCounter);

    var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
    AssertEqual("中文摘要（修改后）", map.Paragraphs[0].Text);
    AssertEqual("Heading1", map.Paragraphs[0].StyleId);
    AssertEqual(true, map.Paragraphs[0].Runs[0].Bold);
    AssertEqual("32", map.Paragraphs[0].Runs[0].FontSizeHalfPoints);
}

static void CliRunExecuteAbortsTransactionOnOperationError()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var before = File.ReadAllBytes(context.Paths.WorkingDocument);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-abort",
          "mode": "execute",
          "options": {
            "createSnapshot": false
          },
          "operations": [
            {
              "id": "replace-title",
              "op": "replaceParagraphText",
              "target": { "type": "paragraphIndex", "index": 0 },
              "text": "changed but not committed"
            },
            {
              "id": "bad-style",
              "op": "setParagraphStyle",
              "target": { "type": "paragraphIndex", "index": 0 },
              "format": { "styleId": "MissingStyle" }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual(2, result.Operations.Count);
    AssertEqual("preview", result.Operations[0].Status);
    AssertEqual("error", result.Operations[1].Status);
    AssertEqual("paragraph_style_missing", result.Diagnostics[0].Code);
    AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
}

static void CliRunWrongTypedTargetReturnsOperationDiagnostic()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-bad-target",
          "mode": "dryRun",
          "operations": [
            {
              "id": "bad-index",
              "op": "replaceParagraphText",
              "target": { "type": "paragraphIndex", "index": "zero" },
              "text": "unused"
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual(1, result.Operations.Count);
    AssertEqual("bad-index", result.Operations[0].Id);
    AssertEqual("error", result.Operations[0].Status);
    AssertEqual("target_value_invalid", result.Operations[0].Reason);
    AssertEqual("target_value_invalid", result.Diagnostics[0].Code);
}

static void CliRunRejectsInvalidRunFontSize()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var before = File.ReadAllBytes(context.Paths.WorkingDocument);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-bad-size",
          "mode": "execute",
          "options": {
            "createSnapshot": false
          },
          "operations": [
            {
              "id": "bad-size",
              "op": "setRunFormat",
              "target": { "type": "runIndex", "paragraphIndex": 0, "runIndex": 0 },
              "format": { "fontSizeHalfPoints": "large" }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("font_size_invalid", result.Operations[0].Reason);
    AssertEqual("font_size_invalid", result.Diagnostics[0].Code);
    AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
}

static void CliRunResolveTargetFindsParagraphsByStyleId()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-resolve-style",
          "mode": "dryRun",
          "options": {
            "requireSingleMatch": false
          },
          "operations": [
            {
              "id": "find-heading1",
              "op": "resolveTarget",
              "target": { "type": "styleId", "styleId": "Heading1" }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual("preview", result.Operations[0].Status);
    AssertEqual(5, result.Operations[0].Matches.Count);
    AssertEqual("p1", result.Operations[0].Matches[0].Id);
    AssertEqual("paragraph", result.Operations[0].Matches[0].Type);
    AssertEqual("第一章 绪论", result.Operations[0].Matches[0].Preview);
}

static void CliRunResolveTargetIsReadOnlyInExecuteMode()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var before = File.ReadAllBytes(context.Paths.WorkingDocument);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-resolve-execute",
          "mode": "execute",
          "options": {
            "createSnapshot": false
          },
          "operations": [
            {
              "id": "find-table",
              "op": "resolveTarget",
              "target": { "type": "tableIndex", "index": 0 }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual("preview", result.Operations[0].Status);
    AssertEqual("t0", result.Operations[0].Matches[0].Id);
    AssertEqual("table", result.Operations[0].Matches[0].Type);
    AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
}

static void CliRunExecuteResolveTargetDoesNotCreateDefaultSnapshot()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var sessionBefore = ThesisJson.Deserialize<SessionState>(File.ReadAllText(context.Paths.SessionJson));
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-resolve-default-snapshot",
          "mode": "execute",
          "operations": [
            {
              "id": "find-table",
              "op": "resolveTarget",
              "target": { "type": "tableIndex", "index": 0 }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(null, result.Snapshot);
    var sessionAfter = ThesisJson.Deserialize<SessionState>(File.ReadAllText(context.Paths.SessionJson));
    AssertEqual(sessionBefore.SnapshotCounter, sessionAfter.SnapshotCounter);
    AssertEqual(1, Directory.EnumerateFiles(context.Paths.SnapshotsDirectory, "*.docx").Count());
}

static void CliRunParagraphOperationRejectsTableTarget()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var before = File.ReadAllBytes(context.Paths.WorkingDocument);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-table-reject",
          "mode": "execute",
          "options": {
            "createSnapshot": false
          },
          "operations": [
            {
              "id": "bad-replace-table",
              "op": "replaceParagraphText",
              "target": { "type": "tableIndex", "index": 0 },
              "text": "not allowed"
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("error", result.Operations[0].Status);
    AssertEqual("target_type_unsupported", result.Operations[0].Reason);
    AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
}

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

static void CliRunDryRunPreviewsApplyProfileTableWithoutChangingDocx()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    WriteProfileWithTableFormat(context);
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
              "id": "apply-table",
              "op": "applyProfileTable",
              "target": { "type": "tableIndex", "index": 0 },
              "format": {
                "widthTwips": 7200
              }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual("preview", result.Operations[0].Status);
    AssertEqual("t0", result.Operations[0].Matches[0].Id);
    AssertEqual("table", result.Operations[0].Matches[0].Type);
    AssertContains(result.Operations[0].Matches[0].PreviewAfter!, "\"widthTwips\":7200");
    AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
}

static void CliRunExecuteAppliesProfileTableFormatting()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    WriteProfileWithTableFormat(context);
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
              "id": "apply-table",
              "op": "applyProfileTable",
              "target": { "type": "tableIndex", "index": 0 }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual("applied", result.Operations[0].Status);

    var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
    var format = map.Tables[0].Format;
    AssertEqual(8640, format.WidthTwips);
    AssertEqual("dxa", format.WidthType);
    AssertEqual("center", format.Alignment);
    AssertEqual(2, format.GridColumnWidthsTwips.Count);
    AssertEqual(4320, format.GridColumnWidthsTwips[0]);
    AssertEqual(4320, format.GridColumnWidthsTwips[1]);
    AssertEqual("single", format.Borders!.Top!.Value);
    AssertEqual("12", format.Borders.Top.Size);
    AssertEqual("single", format.Borders.Bottom!.Value);
    AssertEqual("single", format.Borders.InsideHorizontal!.Value);
    AssertEqual("4", format.Borders.InsideHorizontal.Size);
    AssertEqual("nil", format.Borders.InsideVertical!.Value);
    AssertEqual(60, format.CellMargins!.TopTwips);
    AssertEqual(120, format.CellMargins.LeftTwips);
    AssertEqual(60, format.CellMargins.BottomTwips);
    AssertEqual(120, format.CellMargins.RightTwips);
    AssertEqual(1, format.HeaderRowCount);
    AssertEqual("center", format.FirstCellParagraphFormat!.Alignment);
    AssertEqual(true, format.FirstCellParagraphFormat.RunFormat!.Bold);
    AssertEqual("21", format.FirstCellParagraphFormat.RunFormat.FontSizeHalfPoints);
    AssertEqual("宋体", format.FirstCellParagraphFormat.RunFormat.EastAsiaFont);
}

static void CliRunApplyProfileTableReturnsFormatMissing()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var profile = new TemplateProfile
    {
        SourceType = "test",
        SourceDocument = context.SourceDoc,
        TablePolicy = new ProfileTablePolicy
        {
            Detected = true,
            TableCount = 1,
            ObservedColumnCounts = [2],
            Default = new ProfileTableSample { RowCount = 2, CellCounts = [2, 2], TextPreview = "A1 B1" }
        }
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
              "id": "missing-table-format",
              "op": "applyProfileTable",
              "target": { "type": "tableIndex", "index": 0 }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("profile_table_format_missing", result.Operations[0].Reason);
    AssertEqual("profile_table_format_missing", result.Diagnostics[0].Code);
}

static void CliRunExecuteAppliesTableMicroOperations()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
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
              "id": "cell-text",
              "op": "setTableCellText",
              "target": { "type": "tableCell", "tableIndex": 0, "rowIndex": 1, "cellIndex": 1 },
              "text": "结果"
            },
            {
              "id": "cell-format",
              "op": "setTableCellFormat",
              "target": { "type": "tableCell", "tableIndex": 0, "rowIndex": 0, "cellIndex": 0 },
              "format": {
                "alignment": "center",
                "bold": true,
                "fontSizeHalfPoints": "24",
                "eastAsiaFont": "黑体"
              }
            },
            {
              "id": "column-width",
              "op": "setTableColumnWidth",
              "target": { "type": "tableIndex", "index": 0 },
              "format": { "columnIndex": 0, "widthTwips": 4800 }
            },
            {
              "id": "header-row",
              "op": "setTableRowHeader",
              "target": { "type": "tableIndex", "index": 0 },
              "format": { "rowIndex": 0, "header": true }
            },
            {
              "id": "table-borders",
              "op": "setTableBorders",
              "target": { "type": "tableIndex", "index": 0 },
              "format": {
                "borders": {
                  "top": { "value": "single", "size": "8", "color": "000000" },
                  "left": { "value": "nil" },
                  "bottom": { "value": "single", "size": "8", "color": "000000" },
                  "right": { "value": "nil" },
                  "insideHorizontal": { "value": "single", "size": "4", "color": "000000" },
                  "insideVertical": { "value": "nil" }
                }
              }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(5, result.Operations.Count);
    foreach (var operation in result.Operations)
    {
        AssertEqual("applied", operation.Status);
    }

    var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
    var table = map.Tables[0];
    AssertContains(table.TextPreview, "结果");
    AssertEqual(4800, table.Format.GridColumnWidthsTwips[0]);
    AssertEqual(1, table.Format.HeaderRowCount);
    AssertEqual("single", table.Format.Borders!.Top!.Value);
    AssertEqual("8", table.Format.Borders.Top.Size);
    AssertEqual("nil", table.Format.Borders.Left!.Value);
    AssertEqual("single", table.Format.Borders.InsideHorizontal!.Value);
    AssertEqual("nil", table.Format.Borders.InsideVertical!.Value);
    AssertEqual("center", table.Format.FirstCellParagraphFormat!.Alignment);
    AssertEqual(true, table.Format.FirstCellParagraphFormat.RunFormat!.Bold);
    AssertEqual("24", table.Format.FirstCellParagraphFormat.RunFormat.FontSizeHalfPoints);
    AssertEqual("黑体", table.Format.FirstCellParagraphFormat.RunFormat.EastAsiaFont);
}

static void CliRunDryRunPreviewsTableCellTextWithoutChangingDocx()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
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
              "id": "cell-text",
              "op": "setTableCellText",
              "target": { "type": "tableCell", "tableIndex": 0, "rowIndex": 1, "cellIndex": 1 },
              "text": "结果"
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual("preview", result.Operations[0].Status);
    AssertEqual("B2", result.Operations[0].Matches[0].PreviewBefore);
    AssertEqual("结果", result.Operations[0].Matches[0].PreviewAfter);
    AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
}

static void CliRunTableBorderUpdateCanSetOneSideOnBareTable()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
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
              "id": "bottom-only",
              "op": "setTableBorders",
              "target": { "type": "tableIndex", "index": 0 },
              "format": {
                "borders": {
                  "bottom": { "value": "single", "size": "8", "color": "000000" }
                }
              }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    var borders = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument).Tables[0].Format.Borders!;
    AssertEqual(null, borders.Top);
    AssertEqual(null, borders.Left);
    AssertEqual("single", borders.Bottom!.Value);
    AssertEqual("8", borders.Bottom.Size);
    AssertEqual(null, borders.Right);
    AssertEqual(null, borders.InsideHorizontal);
    AssertEqual(null, borders.InsideVertical);
}

static void CliRunTableBorderUpdatePreservesExistingSides()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    WriteProfileWithTableFormat(context);
    var applyProfileRequest = Path.Combine(temp.Path, "apply-profile.json");
    File.WriteAllText(
        applyProfileRequest,
        """
        {
          "schemaVersion": "1.0",
          "mode": "execute",
          "options": {
            "createSnapshot": false
          },
          "operations": [
            {
              "id": "apply-table",
              "op": "applyProfileTable",
              "target": { "type": "tableIndex", "index": 0 }
            }
          ]
        }
        """);
    AssertEqual(0, RunCli(["run", "--workspace", context.Workspace, "--request", applyProfileRequest]).ExitCode);

    var updateRequest = Path.Combine(temp.Path, "update-border.json");
    File.WriteAllText(
        updateRequest,
        """
        {
          "schemaVersion": "1.0",
          "mode": "execute",
          "options": {
            "createSnapshot": false
          },
          "operations": [
            {
              "id": "bottom-only",
              "op": "setTableBorders",
              "target": { "type": "tableIndex", "index": 0 },
              "format": {
                "borders": {
                  "bottom": { "value": "double", "size": "16", "color": "FF0000" }
                }
              }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", updateRequest]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    var borders = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument).Tables[0].Format.Borders!;
    AssertEqual("single", borders.Top!.Value);
    AssertEqual("nil", borders.Left!.Value);
    AssertEqual("double", borders.Bottom!.Value);
    AssertEqual("16", borders.Bottom.Size);
    AssertEqual("FF0000", borders.Bottom.Color);
    AssertEqual("nil", borders.Right!.Value);
    AssertEqual("single", borders.InsideHorizontal!.Value);
    AssertEqual("nil", borders.InsideVertical!.Value);
}

static void CliRunApplyThreeLineTableSetsAcademicBorders()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
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
              "id": "three-line",
              "op": "applyThreeLineTable",
              "target": { "type": "tableIndex", "index": 0 }
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual("applied", result.Operations[0].Status);
    var format = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument).Tables[0].Format;
    AssertEqual("single", format.Borders!.Top!.Value);
    AssertEqual("12", format.Borders.Top.Size);
    AssertEqual("nil", format.Borders.Left!.Value);
    AssertEqual("single", format.Borders.Bottom!.Value);
    AssertEqual("12", format.Borders.Bottom.Size);
    AssertEqual("nil", format.Borders.Right!.Value);
    AssertEqual("single", format.Borders.InsideHorizontal!.Value);
    AssertEqual("4", format.Borders.InsideHorizontal.Size);
    AssertEqual("nil", format.Borders.InsideVertical!.Value);
}

static void CliRunTableCellOperationRejectsTableTarget()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
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
              "id": "bad-cell-text",
              "op": "setTableCellText",
              "target": { "type": "tableIndex", "index": 0 },
              "text": "not allowed"
            }
          ]
        }
        """);

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("target_type_unsupported", result.Operations[0].Reason);
    AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
}

static void CliRunReturnsProfileInvalidForMalformedWorkspaceProfile()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    File.WriteAllText(context.Paths.ProfileJson, "{not-json");
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-bad-profile",
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

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("profile_invalid", result.Diagnostics[0].Code);
}

static void CliRunReturnsProfileInvalidForStructurallyInvalidWorkspaceProfile()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    File.WriteAllText(
        context.Paths.ProfileJson,
        """
        {
          "schemaVersion": "1.0",
          "profileKind": "templateProfile",
          "styleRoles": null
        }
        """);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-bad-profile-shape",
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

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("profile_invalid", result.Diagnostics[0].Code);
}

static void CliRunReturnsProfileInvalidForNullRoleEvidence()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    File.WriteAllText(
        context.Paths.ProfileJson,
        """
        {
          "schemaVersion": "1.0",
          "profileKind": "templateProfile",
          "styleRoles": [
            {
              "role": "abstract.zh",
              "styleId": "Heading1",
              "evidence": null
            }
          ]
        }
        """);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-null-evidence",
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

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("profile_invalid", result.Diagnostics[0].Code);
}

static void CliRunReturnsProfileInvalidForNullProfileRuleContainers()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    File.WriteAllText(
        context.Paths.ProfileJson,
        """
        {
          "schemaVersion": "1.0",
          "profileKind": "templateProfile",
          "rolePolicies": [
            {
              "role": "heading1",
              "appliesTo": "paragraph",
              "match": null
            }
          ],
          "tableArchetypes": [
            {
              "name": "threeLine",
              "match": {
                "columnCounts": null
              }
            }
          ],
          "diagnostics": [
            {
              "severity": "info",
              "code": "profile_rule_inferred",
              "message": "bad",
              "evidence": null
            }
          ]
        }
        """);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-null-profile-rules",
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

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("profile_invalid", result.Diagnostics[0].Code);
}

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

static void CliRunExecuteRefusesLockedWorkspace()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedDocxWorkspace(temp.Path);
    var before = File.ReadAllBytes(context.Paths.WorkingDocument);
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(
        requestPath,
        """
        {
          "schemaVersion": "1.0",
          "requestId": "req-locked",
          "mode": "execute",
          "operations": [
            {
              "id": "replace-title",
              "op": "replaceParagraphText",
              "target": { "type": "paragraphIndex", "index": 0 },
              "text": "locked"
            }
          ]
        }
        """);
    File.WriteAllText(context.Paths.LockFile, "locked");

    var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("workspace_locked", result.Diagnostics[0].Code);
    AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
}

static void CliUnknownCommandReturnsJsonError()
{
    var output = new StringWriter();
    var exitCode = ThesisCli.Run(["bogus"], output, TextWriter.Null);

    AssertEqual(1, exitCode);
    var result = ThesisJson.Deserialize<CliResult>(output.ToString());
    AssertEqual("error", result.Status);
    AssertEqual("unknown_command", result.Diagnostics[0].Code);
}

static void SnapshotCreatesNextCopyIncrementsCounterAndReturnsInfo()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    File.WriteAllText(context.Paths.WorkingDocument, "snapshot body");

    var (exitCode, result) = RunCli(["snapshot", "--workspace", context.Workspace, "--name", "Before References!"]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual("0002-before-references", result.Snapshot!.Id);
    AssertEqual(true, result.Snapshot.Created);
    AssertEqual(Path.Combine(context.Paths.SnapshotsDirectory, "0002-before-references.docx"), result.Snapshot.Path);
    AssertEqual("snapshot body", File.ReadAllText(result.Snapshot.Path!));
    AssertBytesEqual(context.OriginalBytes, File.ReadAllBytes(context.SourceDoc));

    var session = ThesisJson.Deserialize<SessionState>(File.ReadAllText(context.Paths.SessionJson));
    AssertEqual(2, session.SnapshotCounter);
}

static void RollbackRestoresWorkingDocumentBytes()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    var expectedBytes = new byte[] { 0x00, 0x10, 0x20, 0xFF };
    File.WriteAllBytes(context.Paths.WorkingDocument, expectedBytes);
    var snapshot = RunCli(["snapshot", "--workspace", context.Workspace, "--name", "before-references"]).Result.Snapshot!;
    File.WriteAllText(context.Paths.WorkingDocument, "mutated");

    var (exitCode, result) = RunCli(["rollback", "--workspace", context.Workspace, "--snapshot", "before-references"]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(snapshot.Id, result.Snapshot!.Id);
    AssertBytesEqual(expectedBytes, File.ReadAllBytes(context.Paths.WorkingDocument));
    AssertBytesEqual(context.OriginalBytes, File.ReadAllBytes(context.SourceDoc));
}

static void RollbackMissingSnapshotReturnsJsonError()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);

    var (exitCode, result) = RunCli(["rollback", "--workspace", context.Workspace, "--snapshot", "does-not-exist"]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("snapshot_missing", result.Diagnostics[0].Code);
}

static void ExportCopiesWorkingDocumentAndLeavesOriginalUnchanged()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    File.WriteAllText(context.Paths.WorkingDocument, "edited working");
    var outputPath = Path.Combine(temp.Path, "exported.docx");

    var (exitCode, result) = RunCli(["export", "--workspace", context.Workspace, "--out", outputPath]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(Path.GetFullPath(outputPath), result.OutputPath);
    AssertEqual("edited working", File.ReadAllText(outputPath));
    AssertEqual("original body", File.ReadAllText(context.SourceDoc));
}

static void ExportToOriginalPathIsRefused()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);

    var (exitCode, result) = RunCli(["export", "--workspace", context.Workspace, "--out", context.SourceDoc]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("export_path_refused", result.Diagnostics[0].Code);
    AssertEqual("original body", File.ReadAllText(context.SourceDoc));
}

static void ExportToWorkingPathIsRefused()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);

    var (exitCode, result) = RunCli(["export", "--workspace", context.Workspace, "--out", context.Paths.WorkingDocument]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("export_path_refused", result.Diagnostics[0].Code);
}

static void ExportInsideWorkspaceIsRefused()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    var sessionBefore = File.ReadAllText(context.Paths.SessionJson);

    var (exitCode, result) = RunCli(["export", "--workspace", context.Workspace, "--out", context.Paths.SessionJson]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("export_path_refused", result.Diagnostics[0].Code);
    AssertEqual(sessionBefore, File.ReadAllText(context.Paths.SessionJson));
}

static void InspectReturnsSessionInfoAndSnapshotList()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    File.WriteAllText(context.Paths.WorkingDocument, "before references");
    RunCli(["snapshot", "--workspace", context.Workspace, "--name", "before-references"]);

    var (exitCode, result) = RunCli(["inspect", "--workspace", context.Workspace]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(context.Paths.Workspace, result.Workspace);
    AssertEqual(context.Paths.WorkingDocument, result.Document);
    AssertEqual(context.SourceDoc, result.Session!.OriginalPath);
    AssertEqual(2, result.Session.SnapshotCounter);
    AssertEqual(2, result.Snapshots.Count);
    AssertEqual("0001-init", result.Snapshots[0].Id);
    AssertEqual("0002-before-references", result.Snapshots[1].Id);
}

static void MutatingCommandsRefuseExistingLockAndInspectStillWorks()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    File.WriteAllText(context.Paths.LockFile, "locked");

    foreach (var args in new[]
    {
        new[] { "snapshot", "--workspace", context.Workspace, "--name", "locked" },
        ["rollback", "--workspace", context.Workspace, "--snapshot", "0001-init"],
        ["export", "--workspace", context.Workspace, "--out", Path.Combine(temp.Path, "locked-export.docx")]
    })
    {
        var (exitCode, result) = RunCli(args);
        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("workspace_locked", result.Diagnostics[0].Code);
    }

    var (inspectExitCode, inspectResult) = RunCli(["inspect", "--workspace", context.Workspace]);
    AssertEqual(0, inspectExitCode);
    AssertEqual("success", inspectResult.Status);
    AssertEqual(1, inspectResult.Snapshots.Count);
}

static void CorruptSessionReturnsJsonError()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    File.WriteAllText(context.Paths.SessionJson, "{not json");

    foreach (var args in new[]
    {
        new[] { "inspect", "--workspace", context.Workspace },
        ["snapshot", "--workspace", context.Workspace, "--name", "after-corrupt"],
        ["rollback", "--workspace", context.Workspace, "--snapshot", "0001-init"],
        ["export", "--workspace", context.Workspace, "--out", Path.Combine(temp.Path, "corrupt-export.docx")]
    })
    {
        var (exitCode, result) = RunCli(args);
        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("session_invalid", result.Diagnostics[0].Code);
    }
}

static void TamperedSessionPathsReturnJsonError()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    var outsideWorking = Path.Combine(temp.Path, "outside-working.docx");
    var state = ThesisJson.Deserialize<SessionState>(File.ReadAllText(context.Paths.SessionJson));
    state.WorkingPath = outsideWorking;
    File.WriteAllText(context.Paths.SessionJson, ThesisJson.Serialize(state));
    File.WriteAllText(outsideWorking, "outside");

    foreach (var args in new[]
    {
        new[] { "inspect", "--workspace", context.Workspace },
        ["snapshot", "--workspace", context.Workspace, "--name", "tampered"],
        ["rollback", "--workspace", context.Workspace, "--snapshot", "0001-init"],
        ["export", "--workspace", context.Workspace, "--out", Path.Combine(temp.Path, "tampered-export.docx")]
    })
    {
        var (exitCode, result) = RunCli(args);
        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("session_invalid", result.Diagnostics[0].Code);
    }

    AssertEqual("outside", File.ReadAllText(outsideWorking));
}

static void MissingWorkspaceFilesReturnJsonErrors()
{
    using var temp = new TempDirectory();
    var missingWorking = CreateInitializedWorkspace(Path.Combine(temp.Path, "missing-working"));
    File.Delete(missingWorking.Paths.WorkingDocument);

    foreach (var args in new[]
    {
        new[] { "inspect", "--workspace", missingWorking.Workspace },
        ["snapshot", "--workspace", missingWorking.Workspace, "--name", "after-delete"],
        ["rollback", "--workspace", missingWorking.Workspace, "--snapshot", "0001-init"],
        ["export", "--workspace", missingWorking.Workspace, "--out", Path.Combine(temp.Path, "missing-working-export.docx")]
    })
    {
        var (exitCode, result) = RunCli(args);
        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("working_doc_missing", result.Diagnostics[0].Code);
    }

    var missingSnapshots = CreateInitializedWorkspace(Path.Combine(temp.Path, "missing-snapshots"));
    Directory.Delete(missingSnapshots.Paths.SnapshotsDirectory, recursive: true);

    foreach (var args in new[]
    {
        new[] { "inspect", "--workspace", missingSnapshots.Workspace },
        ["snapshot", "--workspace", missingSnapshots.Workspace, "--name", "after-delete"],
        ["rollback", "--workspace", missingSnapshots.Workspace, "--snapshot", "0001-init"]
    })
    {
        var (exitCode, result) = RunCli(args);
        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("snapshots_missing", result.Diagnostics[0].Code);
    }
}

static void SnapshotAndRollbackRejectTraversalIdentifiers()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);

    foreach (var args in new[]
    {
        new[] { "snapshot", "--workspace", context.Workspace, "--name", ".." },
        ["snapshot", "--workspace", context.Workspace, "--name", "../outside"],
        ["snapshot", "--workspace", context.Workspace, "--name", "bad\\name"],
        ["rollback", "--workspace", context.Workspace, "--snapshot", "../outside"],
        ["rollback", "--workspace", context.Workspace, "--snapshot", "bad/name"],
        ["rollback", "--workspace", context.Workspace, "--snapshot", ".."],
        ["snapshot", "--workspace", context.Workspace, "--name", "CON"]
    })
    {
        var (exitCode, result) = RunCli(args);
        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("invalid_snapshot_identifier", result.Diagnostics[0].Code);
    }

    AssertBytesEqual(context.OriginalBytes, File.ReadAllBytes(context.SourceDoc));
}

static void SnapshotRefusesToOverwriteExistingTarget()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    var existing = Path.Combine(context.Paths.SnapshotsDirectory, "0002-before-references.docx");
    File.WriteAllText(existing, "existing");

    var (exitCode, result) = RunCli(["snapshot", "--workspace", context.Workspace, "--name", "before-references"]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("snapshot_exists", result.Diagnostics[0].Code);
    AssertEqual("existing", File.ReadAllText(existing));

    var session = ThesisJson.Deserialize<SessionState>(File.ReadAllText(context.Paths.SessionJson));
    AssertEqual(1, session.SnapshotCounter);
}

static void SnapshotRemovesOrphanWhenSessionSaveFails()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    File.SetAttributes(context.Paths.SessionJson, FileAttributes.ReadOnly);

    try
    {
        var (exitCode, result) = RunCli(["snapshot", "--workspace", context.Workspace, "--name", "orphan"]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("session_write_failed", result.Diagnostics[0].Code);
        AssertEqual(false, File.Exists(Path.Combine(context.Paths.SnapshotsDirectory, "0002-orphan.docx")));
    }
    finally
    {
        File.SetAttributes(context.Paths.SessionJson, FileAttributes.Normal);
    }
}

static void RollbackAmbiguousSuffixReturnsJsonError()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    File.WriteAllText(Path.Combine(context.Paths.SnapshotsDirectory, "0002-before.docx"), "first");
    File.WriteAllText(Path.Combine(context.Paths.SnapshotsDirectory, "0003-before.docx"), "second");

    var (exitCode, result) = RunCli(["rollback", "--workspace", context.Workspace, "--snapshot", "before"]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("snapshot_ambiguous", result.Diagnostics[0].Code);
    AssertBytesEqual(context.OriginalBytes, File.ReadAllBytes(context.SourceDoc));
}

static void ExportToMissingParentDirectoryReturnsJsonError()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    var outputPath = Path.Combine(temp.Path, "missing-parent", "exported.docx");

    var (exitCode, result) = RunCli(["export", "--workspace", context.Workspace, "--out", outputPath]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("export_directory_missing", result.Diagnostics[0].Code);
    AssertEqual(false, File.Exists(outputPath));
}

static void InspectIsReadOnlyWhenLockExists()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);
    var sessionBefore = File.ReadAllText(context.Paths.SessionJson);
    File.WriteAllText(context.Paths.LockFile, "locked");

    var (exitCode, result) = RunCli(["inspect", "--workspace", context.Workspace]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual("locked", File.ReadAllText(context.Paths.LockFile));
    AssertEqual(sessionBefore, File.ReadAllText(context.Paths.SessionJson));
}

static void OpenXmlInspectorReadsDocumentMap()
{
    using var temp = new TempDirectory();
    var docx = Path.Combine(temp.Path, "fixture.docx");
    WriteFixtureDocx(docx);

    var map = OpenXmlDocumentInspector.Inspect(docx);

    AssertEqual("1.0", map.SchemaVersion);
    AssertEqual(Path.GetFullPath(docx), map.Path);
    AssertEqual(true, map.RequiresFinalization);
    AssertEqual(true, map.FinalizationReasons.Contains("fields", StringComparer.Ordinal));

    AssertEqual(7, map.Paragraphs.Count);
    AssertEqual("中文摘要", map.Paragraphs[0].Text);
    AssertEqual("Title", map.Paragraphs[0].StyleId);
    AssertEqual("第一章 绪论", map.Paragraphs[1].Text);
    AssertEqual("Heading1", map.Paragraphs[1].StyleId);
    AssertEqual("列表项", map.Paragraphs[2].Text);
    AssertEqual("1", map.Paragraphs[2].Numbering!.NumberingId);
    AssertEqual("0", map.Paragraphs[2].Numbering!.Level);

    AssertEqual(true, map.Styles.Any(style => style.StyleId == "Heading1" && style.Name == "heading 1" && style.Type == "paragraph"));
    AssertEqual(true, map.Numbering.Any(numbering =>
        numbering.NumberingId == "1"
        && numbering.AbstractNumberingId == "0"
        && numbering.Levels.Any(level => level.Level == "0" && level.Format == "decimal" && level.Text == "%1.")));
    AssertEqual(1, map.Sections.Count);
    AssertEqual(11906, map.Sections[0].PageSize!.WidthTwips);
    AssertEqual(16838, map.Sections[0].PageSize!.HeightTwips);
    AssertEqual(1440, map.Sections[0].PageMargin!.TopTwips);
    AssertEqual(true, map.Sections[0].Headers.Any(header => header.Type == "default" && header.RelationshipId == "rIdHeader1"));

    AssertEqual(1, map.Tables.Count);
    AssertEqual(2, map.Tables[0].RowCount);
    AssertEqual(2, map.Tables[0].CellCounts[0]);
    AssertContains(map.Tables[0].TextPreview, "A1");
    AssertContains(map.Tables[0].TextPreview, "B2");
}

static void OpenXmlInspectorReadsParagraphAndRunFormatSamples()
{
    using var temp = new TempDirectory();
    var docx = Path.Combine(temp.Path, "formatted-fixture.docx");
    WriteFormattedFixtureDocx(docx);

    var map = OpenXmlDocumentInspector.Inspect(docx);
    var paragraph = map.Paragraphs[0];

    AssertEqual("Heading1", paragraph.StyleId);
    AssertEqual("Heading1", paragraph.Format.StyleId);
    AssertEqual("center", paragraph.Format.Alignment);
    AssertEqual(240, paragraph.Format.SpacingBeforeTwips);
    AssertEqual(120, paragraph.Format.SpacingAfterTwips);
    AssertEqual("360", paragraph.Format.LineSpacing);
    AssertEqual("auto", paragraph.Format.LineSpacingRule);
    AssertEqual(480, paragraph.Format.FirstLineIndentTwips);
    AssertEqual(240, paragraph.Format.LeftIndentTwips);
    AssertEqual(120, paragraph.Format.RightIndentTwips);
    AssertEqual(true, paragraph.Format.RunFormat!.Bold);
    AssertEqual("28", paragraph.Format.RunFormat.FontSizeHalfPoints);
    AssertEqual("Times New Roman", paragraph.Format.RunFormat.AsciiFont);
    AssertEqual("宋体", paragraph.Format.RunFormat.EastAsiaFont);
    AssertEqual("Times New Roman", paragraph.Runs[0].AsciiFont);
    AssertEqual("宋体", paragraph.Runs[0].EastAsiaFont);

    AssertEqual(false, map.Paragraphs[1].Format.RunFormat!.Bold);
    var emptyRunFormat = new RunFormatSample();
    AssertEqual((bool?)null, emptyRunFormat.Bold);
    AssertEqual((bool?)null, emptyRunFormat.Italic);
}

static void OpenXmlInspectorFallsBackToComplexScriptFontSize()
{
    using var temp = new TempDirectory();
    var docx = Path.Combine(temp.Path, "complex-size.docx");
    WriteComplexScriptSizeFixtureDocx(docx);

    var map = OpenXmlDocumentInspector.Inspect(docx);

    AssertEqual("21", map.Paragraphs[0].Format.RunFormat!.FontSizeHalfPoints);
    AssertEqual("21", map.Paragraphs[0].Runs[0].FontSizeHalfPoints);
}

static void OpenXmlInspectorReadsStyleUsageAndOutlineFacts()
{
    using var temp = new TempDirectory();
    var docx = Path.Combine(temp.Path, "formatted.docx");
    WriteFormattedFixtureDocx(docx);

    var map = OpenXmlDocumentInspector.Inspect(docx);

    var heading = map.Paragraphs.Single(paragraph => paragraph.Text == "第一章 绪论");
    AssertEqual("Heading1", heading.StyleId);
    AssertEqual(0, heading.OutlineLevel);

    var headingStyle = map.Styles.Single(style => style.StyleId == "Heading1");
    AssertEqual(true, headingStyle.UsageCount > 0);
}

static void OpenXmlInspectorReadsOutlineFactsFromStyleDefinitions()
{
    using var temp = new TempDirectory();
    var docx = Path.Combine(temp.Path, "style-outline.docx");
    WriteFixtureDocx(docx);

    var map = OpenXmlDocumentInspector.Inspect(docx);

    var heading = map.Paragraphs.Single(paragraph => paragraph.Text == "第一章 绪论");
    AssertEqual("Heading1", heading.StyleId);
    AssertEqual(0, heading.OutlineLevel);
}

static void OpenXmlInspectorReadsTableFormatSamples()
{
    using var temp = new TempDirectory();
    var docx = Path.Combine(temp.Path, "formatted-table-fixture.docx");
    WriteFormattedFixtureDocx(docx);

    var map = OpenXmlDocumentInspector.Inspect(docx);
    var table = map.Tables[0];

    AssertEqual(8640, table.Format.WidthTwips);
    AssertEqual("dxa", table.Format.WidthType);
    AssertEqual("center", table.Format.Alignment);
    AssertEqual(2, table.Format.GridColumnWidthsTwips.Count);
    AssertEqual(4320, table.Format.GridColumnWidthsTwips[0]);
    AssertEqual(4320, table.Format.GridColumnWidthsTwips[1]);
    AssertEqual("single", table.Format.Borders!.Top!.Value);
    AssertEqual("12", table.Format.Borders.Top.Size);
    AssertEqual("000000", table.Format.Borders.Top.Color);
    AssertEqual("single", table.Format.Borders.Bottom!.Value);
    AssertEqual("12", table.Format.Borders.Bottom.Size);
    AssertEqual("single", table.Format.Borders.InsideHorizontal!.Value);
    AssertEqual("4", table.Format.Borders.InsideHorizontal.Size);
    AssertEqual("nil", table.Format.Borders.InsideVertical!.Value);
    AssertEqual(60, table.Format.CellMargins!.TopTwips);
    AssertEqual(120, table.Format.CellMargins.LeftTwips);
    AssertEqual(60, table.Format.CellMargins.BottomTwips);
    AssertEqual(120, table.Format.CellMargins.RightTwips);
    AssertEqual(1, table.Format.HeaderRowCount);
    AssertEqual("center", table.Format.FirstCellParagraphFormat!.Alignment);
    AssertEqual(true, table.Format.FirstCellParagraphFormat.RunFormat!.Bold);
    AssertEqual("21", table.Format.FirstCellParagraphFormat.RunFormat.FontSizeHalfPoints);
    AssertEqual("宋体", table.Format.FirstCellParagraphFormat.RunFormat.EastAsiaFont);
}

static void CliInspectIncludesDocumentMapForDocxWorkspaces()
{
    using var temp = new TempDirectory();
    var sourceDoc = Path.Combine(temp.Path, "source.docx");
    var profile = Path.Combine(temp.Path, "input-profile.json");
    var workspace = Path.Combine(temp.Path, ".thesis");

    WriteFixtureDocx(sourceDoc);
    File.WriteAllText(profile, "{}");

    var init = SessionInitializer.Initialize(sourceDoc, profile, workspace);
    AssertEqual("success", init.Status);

    var (exitCode, result) = RunCli(["inspect", "--workspace", workspace]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(Path.Combine(Path.GetFullPath(workspace), "working.docx"), result.DocumentMap!.Path);
    AssertEqual(7, result.DocumentMap.Paragraphs.Count);
    AssertEqual(1, result.DocumentMap.Tables.Count);

    var rawJson = RunCliRaw(["inspect", "--workspace", workspace]).Output;
    AssertContains(rawJson, "\"documentMap\"");
    AssertContains(rawJson, "\"requiresFinalization\":true");
    AssertContains(rawJson, "\"finalizationReasons\":[\"fields\"]");
    AssertContains(rawJson, "\"numberingId\":\"1\"");
    AssertContains(rawJson, "\"levels\":[");
    AssertDoesNotContain(rawJson, "\"DocumentMap\"");
}

static void CliInspectReportsJsonWarningWhenDocumentMapUnavailable()
{
    using var temp = new TempDirectory();
    var context = CreateInitializedWorkspace(temp.Path);

    var (exitCode, output) = RunCliRaw(["inspect", "--workspace", context.Workspace]);
    var result = ThesisJson.Deserialize<CliResult>(output);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(null, result.DocumentMap);
    AssertEqual(1, result.Diagnostics.Count);
    AssertEqual("warning", result.Diagnostics[0].Severity);
    AssertEqual("document_map_unavailable", result.Diagnostics[0].Code);
    AssertEqual(context.Paths.WorkingDocument, result.Diagnostics[0].Path);
    AssertContains(output, "\"diagnostics\":[");
    AssertContains(output, "\"code\":\"document_map_unavailable\"");
    AssertContains(output, "\"path\":\"");
}

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
    AssertEqual(true, profile.StyleRoles.Any(role => role.Role == "toc" && role.StyleId == "Heading1"));
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

static (int ExitCode, CliResult Result) RunCli(string[] args)
{
    var output = new StringWriter();
    var exitCode = ThesisCli.Run(args, output, TextWriter.Null);
    return (exitCode, ThesisJson.Deserialize<CliResult>(output.ToString()));
}

static (int ExitCode, string Output) RunCliRaw(string[] args)
{
    var output = new StringWriter();
    var exitCode = ThesisCli.Run(args, output, TextWriter.Null);
    return (exitCode, output.ToString());
}

static WorkspaceContext CreateInitializedWorkspace(string root)
{
    Directory.CreateDirectory(root);

    var sourceDoc = Path.GetFullPath(Path.Combine(root, "source.docx"));
    var profile = Path.Combine(root, "input-profile.json");
    var workspace = Path.Combine(root, ".thesis");

    File.WriteAllText(sourceDoc, "original body");
    File.WriteAllText(profile, "{}");

    var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);
    AssertEqual("success", result.Status);

    return new WorkspaceContext(
        sourceDoc,
        profile,
        Path.GetFullPath(workspace),
        SessionPaths.FromWorkspace(workspace),
        File.ReadAllBytes(sourceDoc));
}

static WorkspaceContext CreateInitializedDocxWorkspace(string root)
{
    Directory.CreateDirectory(root);

    var sourceDoc = Path.GetFullPath(Path.Combine(root, "source.docx"));
    var profile = Path.Combine(root, "input-profile.json");
    var workspace = Path.Combine(root, ".thesis");

    WriteFixtureDocx(sourceDoc);
    File.WriteAllText(profile, "{}");

    var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);
    AssertEqual("success", result.Status);

    return new WorkspaceContext(
        sourceDoc,
        profile,
        Path.GetFullPath(workspace),
        SessionPaths.FromWorkspace(workspace),
        File.ReadAllBytes(sourceDoc));
}

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

static void WriteFixtureDocx(string path)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    AddZipEntry(
        archive,
        "[Content_Types].xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
          <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
          <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
        </Types>
        """);
    AddZipEntry(
        archive,
        "_rels/.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """);
    AddZipEntry(
        archive,
        "word/_rels/document.xml.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdHeader1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
          <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
          <Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
        </Relationships>
        """);
    AddZipEntry(
        archive,
        "word/styles.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
          <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/></w:style>
          <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style>
        </w:styles>
        """);
    AddZipEntry(
        archive,
        "word/numbering.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:abstractNum w:abstractNumId="0">
            <w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl>
          </w:abstractNum>
          <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
        </w:numbering>
        """);
    AddZipEntry(
        archive,
        "word/header1.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>页眉</w:t></w:r></w:p></w:hdr>
        """);
    AddZipEntry(
        archive,
        "word/document.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <w:body>
            <w:p><w:pPr><w:pStyle w:val="Title"/></w:pPr><w:r><w:t>中文摘要</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>列表项</w:t></w:r></w:p>
            <w:tbl>
              <w:tr><w:tc><w:p><w:r><w:t>A1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B1</w:t></w:r></w:p></w:tc></w:tr>
              <w:tr><w:tc><w:p><w:r><w:t>A2</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B2</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            <w:p><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText>TOC \o "1-3" \h \z \u</w:instrText></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>摘要</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Abstract</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>目录</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>参考文献</w:t></w:r></w:p>
            <w:sectPr>
              <w:headerReference w:type="default" r:id="rIdHeader1"/>
              <w:pgSz w:w="11906" w:h="16838"/>
              <w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800" w:header="720" w:footer="720" w:gutter="0"/>
            </w:sectPr>
          </w:body>
        </w:document>
        """);
}

static void WriteFormattedFixtureDocx(string path)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    AddZipEntry(
        archive,
        "[Content_Types].xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
          <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
          <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
        </Types>
        """);
    AddZipEntry(
        archive,
        "_rels/.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """);
    AddZipEntry(
        archive,
        "word/_rels/document.xml.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdHeader1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
          <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
          <Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
        </Relationships>
        """);
    AddZipEntry(
        archive,
        "word/styles.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
          <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/></w:style>
          <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/></w:style>
        </w:styles>
        """);
    AddZipEntry(
        archive,
        "word/numbering.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:abstractNum w:abstractNumId="0">
            <w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl>
          </w:abstractNum>
          <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
        </w:numbering>
        """);
    AddZipEntry(
        archive,
        "word/header1.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>页眉</w:t></w:r></w:p></w:hdr>
        """);
    AddZipEntry(
        archive,
        "word/document.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <w:body>
            <w:p>
              <w:pPr>
                <w:pStyle w:val="Heading1"/>
                <w:jc w:val="center"/>
                <w:spacing w:before="240" w:after="120" w:line="360" w:lineRule="auto"/>
                <w:ind w:firstLine="480" w:left="240" w:right="120"/>
              </w:pPr>
              <w:r>
                <w:rPr>
                  <w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" w:eastAsia="宋体" w:cs="Times New Roman"/>
                  <w:b/>
                  <w:sz w:val="28"/>
                </w:rPr>
                <w:t>摘要</w:t>
              </w:r>
            </w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/><w:outlineLvl w:val="0"/></w:pPr><w:r><w:rPr><w:b w:val="false"/></w:rPr><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>列表项</w:t></w:r></w:p>
            <w:tbl>
              <w:tblPr>
                <w:tblW w:w="8640" w:type="dxa"/>
                <w:jc w:val="center"/>
                <w:tblBorders>
                  <w:top w:val="single" w:sz="12" w:color="000000"/>
                  <w:bottom w:val="single" w:sz="12" w:color="000000"/>
                  <w:left w:val="nil"/>
                  <w:right w:val="nil"/>
                  <w:insideH w:val="single" w:sz="4" w:color="000000"/>
                  <w:insideV w:val="nil"/>
                </w:tblBorders>
                <w:tblCellMar>
                  <w:top w:w="60" w:type="dxa"/>
                  <w:left w:w="120" w:type="dxa"/>
                  <w:bottom w:w="60" w:type="dxa"/>
                  <w:right w:w="120" w:type="dxa"/>
                </w:tblCellMar>
              </w:tblPr>
              <w:tblGrid>
                <w:gridCol w:w="4320"/>
                <w:gridCol w:w="4320"/>
              </w:tblGrid>
              <w:tr>
                <w:trPr><w:tblHeader/></w:trPr>
                <w:tc>
                  <w:p>
                    <w:pPr><w:jc w:val="center"/></w:pPr>
                    <w:r>
                      <w:rPr>
                        <w:rFonts w:eastAsia="宋体"/>
                        <w:b/>
                        <w:sz w:val="21"/>
                      </w:rPr>
                      <w:t>A1</w:t>
                    </w:r>
                  </w:p>
                </w:tc>
                <w:tc><w:p><w:r><w:t>B1</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr><w:tc><w:p><w:r><w:t>A2</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B2</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            <w:p><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText>TOC \o "1-3" \h \z \u</w:instrText></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>摘要</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Abstract</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>目录</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>参考文献</w:t></w:r></w:p>
            <w:sectPr>
              <w:headerReference w:type="default" r:id="rIdHeader1"/>
              <w:pgSz w:w="11906" w:h="16838"/>
              <w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800" w:header="720" w:footer="720" w:gutter="0"/>
            </w:sectPr>
          </w:body>
        </w:document>
        """);
}

static void WriteComplexScriptSizeFixtureDocx(string path)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    AddZipEntry(
        archive,
        "[Content_Types].xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
        </Types>
        """);
    AddZipEntry(
        archive,
        "_rels/.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """);
    AddZipEntry(
        archive,
        "word/_rels/document.xml.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """);
    AddZipEntry(
        archive,
        "word/styles.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
        </w:styles>
        """);
    AddZipEntry(
        archive,
        "word/document.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p>
              <w:r>
                <w:rPr><w:rFonts w:eastAsia="宋体"/><w:szCs w:val="21"/></w:rPr>
                <w:t>正文字号只在 szCs 中声明</w:t>
              </w:r>
            </w:p>
            <w:sectPr/>
          </w:body>
        </w:document>
        """);
}

static void AddZipEntry(ZipArchive archive, string entryName, string text)
{
    var entry = archive.CreateEntry(entryName);
    using var writer = new StreamWriter(entry.Open());
    writer.Write(text);
}

static void InjectHyperlinkIntoFirstParagraph(string docxPath)
{
    using var archive = ZipFile.Open(docxPath, ZipArchiveMode.Update);
    var entry = archive.GetEntry("word/document.xml") ?? throw new UnreachableException("Missing document.xml.");
    string xml;
    using (var reader = new StreamReader(entry.Open()))
    {
        xml = reader.ReadToEnd();
    }

    xml = xml.Replace(
        "<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr><w:r><w:t>中文摘要</w:t></w:r></w:p>",
        "<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr><w:hyperlink><w:r><w:t>中文摘要</w:t></w:r></w:hyperlink></w:p>",
        StringComparison.Ordinal);
    entry.Delete();
    AddZipEntry(archive, "word/document.xml", xml);
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new UnreachableException($"Expected '{expected}', got '{actual}'.");
    }
}

static void AssertContains(string text, string expected)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        throw new UnreachableException($"Expected text to contain '{expected}'.");
    }
}

static void AssertDoesNotContain(string text, string unexpected)
{
    if (text.Contains(unexpected, StringComparison.Ordinal))
    {
        throw new UnreachableException($"Expected text not to contain '{unexpected}'.");
    }
}

static void AssertBytesEqual(byte[] expected, byte[] actual)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new UnreachableException($"Expected bytes '{Convert.ToHexString(expected)}', got '{Convert.ToHexString(actual)}'.");
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "thesis-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed record WorkspaceContext(
    string SourceDoc,
    string Profile,
    string Workspace,
    SessionPaths Paths,
    byte[] OriginalBytes);
