internal static partial class Program
{
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

}
