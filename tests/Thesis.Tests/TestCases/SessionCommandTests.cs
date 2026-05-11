internal static partial class Program
{
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

}
