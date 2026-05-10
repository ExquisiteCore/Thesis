using Thesis.Schema;

namespace Thesis.Session;

public static class SessionInitializer
{
    public static CliResult Initialize(string sourceDoc, string profilePath, string workspace)
    {
        var paths = SessionPaths.FromWorkspace(workspace);

        if (File.Exists(paths.LockFile))
        {
            return Error(paths, "workspace_locked", "The workspace is locked by session.lock.");
        }

        if (Directory.Exists(paths.Workspace)
            && Directory.EnumerateFileSystemEntries(paths.Workspace).Any())
        {
            return Error(
                paths,
                "workspace_exists",
                "The workspace already contains files. Existing workspaces are not overwritten.");
        }

        if (!File.Exists(sourceDoc))
        {
            return Error(paths, "source_doc_missing", $"Source document not found: {sourceDoc}");
        }

        if (!File.Exists(profilePath))
        {
            return Error(paths, "profile_missing", $"Profile not found: {profilePath}");
        }

        Directory.CreateDirectory(paths.Workspace);

        File.WriteAllText(paths.LockFile, DateTimeOffset.UtcNow.ToString("O"));
        try
        {
            Directory.CreateDirectory(paths.SnapshotsDirectory);
            Directory.CreateDirectory(paths.LogsDirectory);
            Directory.CreateDirectory(paths.CacheDirectory);

            File.Copy(sourceDoc, paths.WorkingDocument);
            File.Copy(profilePath, paths.ProfileJson);

            var snapshotPath = Path.Combine(paths.SnapshotsDirectory, "0001-init.docx");
            File.Copy(paths.WorkingDocument, snapshotPath);

            var session = new
            {
                schemaVersion = "1.0",
                originalPath = Path.GetFullPath(sourceDoc),
                workingPath = paths.WorkingDocument,
                profilePath = paths.ProfileJson,
                createdAt = DateTimeOffset.UtcNow,
                snapshotCounter = 1
            };
            File.WriteAllText(paths.SessionJson, ThesisJson.Serialize(session));

            return new CliResult
            {
                Status = "success",
                Workspace = paths.Workspace,
                Document = paths.WorkingDocument,
                Snapshot = new SnapshotInfo
                {
                    Created = true,
                    Id = "0001-init",
                    Path = snapshotPath
                }
            };
        }
        finally
        {
            if (File.Exists(paths.LockFile))
            {
                File.Delete(paths.LockFile);
            }
        }
    }

    private static CliResult Error(SessionPaths paths, string code, string message)
    {
        return new CliResult
        {
            Status = "error",
            Workspace = paths.Workspace,
            Document = paths.WorkingDocument,
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "error",
                    Code = code,
                    Message = message
                }
            ]
        };
    }
}
