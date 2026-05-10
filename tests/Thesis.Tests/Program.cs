using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Thesis.Cli;
using Thesis.OpenXml;
using Thesis.Schema;
using Thesis.Session;

var tests = new (string Name, Action Test)[]
{
    ("JSON roundtrip uses camelCase and enum strings", JsonRoundtripUsesCamelCaseAndEnumStrings),
    ("SessionPaths resolves expected filenames", SessionPathsResolvesExpectedFilenames),
    ("SessionInitializer creates workspace files and refuses existing workspace", SessionInitializerCreatesFilesAndRefusesExistingWorkspace),
    ("SessionInitializer refuses non-empty stale workspace", SessionInitializerRefusesNonEmptyStaleWorkspace),
    ("SessionInitializer refuses locked workspace", SessionInitializerRefusesLockedWorkspace),
    ("SessionInitializer releases lock after validation errors", SessionInitializerReleasesLockAfterValidationErrors),
    ("CLI run reads request JSON and returns success JSON", CliRunReadsRequestJsonAndReturnsSuccessJson),
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
    ("Rollback ambiguous suffix returns JSON error", RollbackAmbiguousSuffixReturnsJsonError),
    ("Export to missing parent directory returns JSON error", ExportToMissingParentDirectoryReturnsJsonError),
    ("Inspect is read-only when lock exists", InspectIsReadOnlyWhenLockExists),
    ("OpenXml inspector reads paragraphs, styles, numbering, sections, and tables", OpenXmlInspectorReadsDocumentMap),
    ("CLI inspect includes document map for DOCX workspaces", CliInspectIncludesDocumentMapForDocxWorkspaces),
    ("CLI inspect reports JSON warning when document map is unavailable", CliInspectReportsJsonWarningWhenDocumentMapUnavailable)
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
    var workspace = Path.Combine(temp.Path, ".thesis");
    var requestPath = Path.Combine(temp.Path, "request.json");
    Directory.CreateDirectory(workspace);
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
        ["run", "--workspace", workspace, "--request", requestPath],
        output,
        error);

    AssertEqual(0, exitCode);
    var result = ThesisJson.Deserialize<CliResult>(output.ToString());
    AssertEqual("success", result.Status);
    AssertEqual("req-123", result.RequestId);
    AssertEqual(Path.GetFullPath(workspace), result.Workspace);
    AssertEqual(Path.Combine(Path.GetFullPath(workspace), "working.docx"), result.Document);
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

    AssertEqual(3, map.Paragraphs.Count);
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
    AssertEqual(3, result.DocumentMap.Paragraphs.Count);
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
            <w:p><w:pPr><w:pStyle w:val="Title"/></w:pPr><w:r><w:t>中文摘要</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>列表项</w:t></w:r></w:p>
            <w:tbl>
              <w:tr><w:tc><w:p><w:r><w:t>A1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B1</w:t></w:r></w:p></w:tc></w:tr>
              <w:tr><w:tc><w:p><w:r><w:t>A2</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B2</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            <w:p><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText>TOC \o "1-3" \h \z \u</w:instrText></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
            <w:sectPr>
              <w:headerReference w:type="default" r:id="rIdHeader1"/>
              <w:pgSz w:w="11906" w:h="16838"/>
              <w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800" w:header="720" w:footer="720" w:gutter="0"/>
            </w:sectPr>
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
