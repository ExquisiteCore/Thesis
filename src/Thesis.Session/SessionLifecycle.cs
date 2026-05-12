using System.Text;
using System.Text.Json;
using Thesis.Schema;

namespace Thesis.Session;

public static class SessionLifecycle
{
    public static CliResult Snapshot(string workspace, string name)
    {
        var paths = SessionPaths.FromWorkspace(workspace);
        return WithLock(paths, () => SnapshotLocked(paths, name));
    }

    public static CliResult Rollback(string workspace, string snapshotIdOrName)
    {
        var paths = SessionPaths.FromWorkspace(workspace);
        return WithLock(paths, () => RollbackLocked(paths, snapshotIdOrName));
    }

    public static CliResult Export(string workspace, string outputPath)
    {
        var paths = SessionPaths.FromWorkspace(workspace);
        return WithLock(paths, () => ExportLocked(paths, outputPath));
    }

    public static CliResult Run(
        string workspace,
        OperationRequest request,
        Func<string, OperationRequest, TemplateProfile?, DocumentEditResult> editDocument)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(editDocument);

        var paths = SessionPaths.FromWorkspace(workspace);
        return WithLock(paths, () => RunLocked(paths, request, editDocument));
    }

    public static CliResult RunWithWorkingDocumentLock(
        string workspace,
        string snapshotName,
        Func<string, CliResult> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotName);
        ArgumentNullException.ThrowIfNull(action);

        var paths = SessionPaths.FromWorkspace(workspace);
        return WithLock(paths, () => RunWithWorkingDocumentLockLocked(paths, snapshotName, action));
    }

    public static CliResult Inspect(string workspace)
    {
        var paths = SessionPaths.FromWorkspace(workspace);
        if (!TryLoadReadySession(paths, out var session, out var error))
        {
            return error!;
        }

        if (!Directory.Exists(paths.SnapshotsDirectory))
        {
            return SessionStore.Error(paths, "snapshots_missing", $"Snapshots directory not found: {paths.SnapshotsDirectory}");
        }

        return Success(paths, session, snapshots: ListSnapshots(paths));
    }

    private static CliResult SnapshotLocked(SessionPaths paths, string name)
    {
        if (!TryLoadReadySession(paths, out var session, out var error))
        {
            return error!;
        }

        if (!Directory.Exists(paths.SnapshotsDirectory))
        {
            return SessionStore.Error(paths, "snapshots_missing", $"Snapshots directory not found: {paths.SnapshotsDirectory}");
        }

        if (!SnapshotIdentifiers.TrySanitizeName(name, out var safeName))
        {
            return SessionStore.Error(paths, "invalid_snapshot_identifier", "Snapshot name is invalid.");
        }

        if (!TryCreateSnapshot(paths, session, safeName, out var snapshot, out error))
        {
            return error!;
        }

        return Success(paths, session, snapshot: snapshot);
    }

    private static CliResult RollbackLocked(SessionPaths paths, string snapshotIdOrName)
    {
        if (!TryLoadReadySession(paths, out var session, out var error))
        {
            return error!;
        }

        if (!Directory.Exists(paths.SnapshotsDirectory))
        {
            return SessionStore.Error(paths, "snapshots_missing", $"Snapshots directory not found: {paths.SnapshotsDirectory}");
        }

        if (!SnapshotIdentifiers.IsSafeLookup(snapshotIdOrName))
        {
            return SessionStore.Error(paths, "invalid_snapshot_identifier", "Snapshot identifier is invalid.");
        }

        var matches = ListSnapshots(paths)
            .Where(snapshot => SnapshotMatches(snapshot.Id, snapshotIdOrName))
            .ToList();

        if (matches.Count == 0)
        {
            return SessionStore.Error(paths, "snapshot_missing", $"Snapshot not found: {snapshotIdOrName}");
        }

        if (matches.Count > 1)
        {
            return SessionStore.Error(paths, "snapshot_ambiguous", $"Snapshot identifier is ambiguous: {snapshotIdOrName}");
        }

        var match = matches[0];
        if (string.IsNullOrWhiteSpace(match.Path)
            || !IsPathInsideDirectory(paths.SnapshotsDirectory, match.Path))
        {
            return SessionStore.Error(paths, "invalid_snapshot_identifier", "Snapshot path is outside the snapshots directory.");
        }

        var tempPath = session.WorkingPath + ".rollback-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(match.Path, tempPath);
            File.Move(tempPath, session.WorkingPath, overwrite: true);
        }
        catch (FileNotFoundException)
        {
            return SessionStore.Error(paths, "snapshot_missing", $"Snapshot not found: {snapshotIdOrName}");
        }
        catch (IOException ex)
        {
            return SessionStore.Error(paths, "rollback_failed", $"Rollback failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return SessionStore.Error(paths, "rollback_failed", $"Rollback failed: {ex.Message}");
        }
        finally
        {
            DeleteIfExists(tempPath);
        }

        return Success(paths, session, snapshot: new SnapshotInfo
        {
            Created = false,
            Id = match.Id,
            Path = match.Path
        });
    }

    private static CliResult ExportLocked(SessionPaths paths, string outputPath)
    {
        if (!TryLoadReadySession(paths, out var session, out var error))
        {
            return error!;
        }

        string fullOutputPath;
        try
        {
            fullOutputPath = Path.GetFullPath(outputPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return SessionStore.Error(paths, "export_path_invalid", $"Export path is invalid: {ex.Message}");
        }

        if (SamePath(fullOutputPath, session.OriginalPath)
            || SamePath(fullOutputPath, session.WorkingPath)
            || IsPathInsideDirectory(paths.Workspace, fullOutputPath))
        {
            return SessionStore.Error(paths, "export_path_refused", "Export output path must not overwrite the original, working document, or workspace state.");
        }

        var parent = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return SessionStore.Error(paths, "export_directory_missing", $"Export directory not found: {parent}");
        }

        try
        {
            File.Copy(session.WorkingPath, fullOutputPath, overwrite: true);
        }
        catch (FileNotFoundException)
        {
            return SessionStore.Error(paths, "working_doc_missing", $"Working document not found: {session.WorkingPath}");
        }
        catch (IOException ex)
        {
            return SessionStore.Error(paths, "export_failed", $"Export failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return SessionStore.Error(paths, "export_failed", $"Export failed: {ex.Message}");
        }

        return Success(paths, session, outputPath: fullOutputPath);
    }

    private static CliResult RunLocked(
        SessionPaths paths,
        OperationRequest request,
        Func<string, OperationRequest, TemplateProfile?, DocumentEditResult> editDocument)
    {
        if (!TryLoadReadySession(paths, out var session, out var error))
        {
            return error!;
        }

        if (!Directory.Exists(paths.SnapshotsDirectory))
        {
            return SessionStore.Error(paths, "snapshots_missing", $"Snapshots directory not found: {paths.SnapshotsDirectory}");
        }

        if (!TryLoadProfile(session.ProfilePath, out var profile, out error))
        {
            return error!;
        }

        SnapshotInfo? snapshot = null;
        if (request.Mode == RequestMode.Execute
            && request.Options.CreateSnapshot
            && RequestHasEditOperations(request))
        {
            var snapshotName = string.IsNullOrWhiteSpace(request.RequestId)
                ? "before-run"
                : $"before-run-{request.RequestId}";
            if (!SnapshotIdentifiers.TrySanitizeName(snapshotName, out var safeName))
            {
                return SessionStore.Error(paths, "invalid_snapshot_identifier", "Run snapshot name is invalid.");
            }

            if (!TryCreateSnapshot(paths, session, safeName, out snapshot, out error))
            {
                return error!;
            }
        }

        DocumentEditResult edit;
        try
        {
            edit = editDocument(session.WorkingPath, request, profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            edit = new DocumentEditResult
            {
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "run_failed",
                        Message = $"Run failed: {ex.Message}",
                        Path = session.WorkingPath
                    }
                ]
            };
        }

        if (edit.Diagnostics.Any(diagnostic => string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase)))
        {
            return new CliResult
            {
                Status = "error",
                RequestId = request.RequestId,
                Mode = request.Mode,
                Workspace = paths.Workspace,
                Document = session.WorkingPath,
                Session = session,
                Snapshot = snapshot,
                Operations = edit.Operations,
                Diagnostics = edit.Diagnostics
            };
        }

        return new CliResult
        {
            Status = "success",
            RequestId = request.RequestId,
            Mode = request.Mode,
            Workspace = paths.Workspace,
            Document = session.WorkingPath,
            Session = session,
            Snapshot = snapshot,
            Operations = edit.Operations,
            Diagnostics = edit.Diagnostics
        };
    }

    private static CliResult RunWithWorkingDocumentLockLocked(
        SessionPaths paths,
        string snapshotName,
        Func<string, CliResult> action)
    {
        if (!TryLoadReadySession(paths, out var session, out var error))
        {
            return error!;
        }

        if (!Directory.Exists(paths.SnapshotsDirectory))
        {
            return SessionStore.Error(paths, "snapshots_missing", $"Snapshots directory not found: {paths.SnapshotsDirectory}");
        }

        if (!SnapshotIdentifiers.TrySanitizeName(snapshotName, out var safeName))
        {
            return SessionStore.Error(paths, "invalid_snapshot_identifier", "Run snapshot name is invalid.");
        }

        if (!TryCreateSnapshot(paths, session, safeName, out var snapshot, out error))
        {
            return error!;
        }

        var result = action(session.WorkingPath);
        result.Workspace = paths.Workspace;
        result.Document ??= session.WorkingPath;
        result.Session = session;
        result.Snapshot = snapshot;
        result.Snapshots = ListSnapshots(paths);
        return result;
    }

    private static bool TryCreateSnapshot(
        SessionPaths paths,
        SessionState session,
        string safeName,
        out SnapshotInfo? snapshot,
        out CliResult? error)
    {
        snapshot = null;
        error = null;

        var nextCounter = checked(session.SnapshotCounter + 1);
        var id = $"{nextCounter:0000}-{safeName}";
        var snapshotPath = Path.Combine(paths.SnapshotsDirectory, id + ".docx");

        if (!IsPathInsideDirectory(paths.SnapshotsDirectory, snapshotPath))
        {
            error = SessionStore.Error(paths, "invalid_snapshot_identifier", "Snapshot path is outside the snapshots directory.");
            return false;
        }

        if (File.Exists(snapshotPath))
        {
            error = SessionStore.Error(paths, "snapshot_exists", $"Snapshot already exists: {id}");
            return false;
        }

        var tempPath = Path.Combine(paths.SnapshotsDirectory, id + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(session.WorkingPath, tempPath);
            File.Move(tempPath, snapshotPath);
        }
        catch (FileNotFoundException)
        {
            error = SessionStore.Error(paths, "working_doc_missing", $"Working document not found: {session.WorkingPath}");
            return false;
        }
        catch (DirectoryNotFoundException ex)
        {
            error = SessionStore.Error(paths, "snapshot_failed", $"Snapshot could not be created: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            error = SessionStore.Error(paths, "snapshot_failed", $"Snapshot could not be created: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = SessionStore.Error(paths, "snapshot_failed", $"Snapshot could not be created: {ex.Message}");
            return false;
        }
        finally
        {
            DeleteIfExists(tempPath);
        }

        session.SnapshotCounter = nextCounter;
        if (!SessionStore.TrySave(paths, session, out error))
        {
            DeleteIfExists(snapshotPath);
            return false;
        }

        snapshot = new SnapshotInfo
        {
            Created = true,
            Id = id,
            Path = snapshotPath
        };
        return true;
    }

    private static bool TryLoadProfile(string path, out TemplateProfile? profile, out CliResult? error)
    {
        profile = null;
        error = null;

        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == "{}")
            {
                return true;
            }

            profile = ThesisJson.Deserialize<TemplateProfile>(text);
            if (!IsValidProfile(profile))
            {
                error = ProfileInvalid(path, "Workspace profile has an invalid structure.");
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            error = ProfileInvalid(path, $"Workspace profile is invalid: {ex.Message}");
            return false;
        }
    }

    private static bool RequestHasEditOperations(OperationRequest request)
    {
        return request.Operations.Any(operation => !string.Equals(operation.Op, "resolveTarget", StringComparison.Ordinal));
    }

    private static bool IsValidProfile(TemplateProfile? profile)
    {
        return profile is not null
            && profile.StyleRoles is not null
            && profile.RolePolicies is not null
            && profile.FormatClusters is not null
            && profile.FinalizationReasons is not null
            && profile.PageSetup is not null
            && profile.NumberingPolicy is not null
            && profile.TablePolicy is not null
            && profile.TableArchetypes is not null
            && profile.Diagnostics is not null
            && profile.SourceEvidence is not null
            && profile.StyleRoles.All(role => role.Evidence is not null)
            && profile.RolePolicies.All(policy =>
                policy.Match is not null
                && policy.Match.StyleIds is not null
                && policy.Match.TextPatterns is not null
                && policy.Match.OutlineLevels is not null
                && IsValidRoleFormatMatch(policy.Match.Format))
            && profile.FormatClusters.All(IsValidFormatCluster)
            && profile.NumberingPolicy.Instances is not null
            && profile.NumberingPolicy.ParagraphUses is not null
            && profile.TablePolicy.ObservedColumnCounts is not null
            && profile.TableArchetypes.All(archetype =>
                archetype.Match is not null
                && archetype.Match.ColumnCounts is not null)
            && profile.Diagnostics.All(diagnostic => diagnostic.Evidence is not null)
            && profile.SourceEvidence.ParagraphSamples is not null;
    }

    private static bool IsValidFormatCluster(ProfileFormatCluster cluster)
    {
        return !string.IsNullOrWhiteSpace(cluster.Id)
            && string.Equals(cluster.AppliesTo, "paragraph", StringComparison.OrdinalIgnoreCase)
            && IsKnownClusterRoleHint(cluster.RoleHint)
            && cluster.Count >= 0
            && cluster.Confidence is >= 0 and <= 1
            && cluster.StyleIds is not null
            && cluster.Match is not null
            && cluster.Match.StyleIds is not null
            && cluster.Match.TextPatterns is not null
            && cluster.Match.OutlineLevels is not null
            && IsValidRoleFormatMatch(cluster.Match.Format)
            && cluster.Format is not null
            && cluster.Evidence is not null
            && cluster.Evidence.All(evidence => evidence is not null);
    }

    private static bool IsKnownClusterRoleHint(string? roleHint)
    {
        return roleHint is "unknown" or "title" or "heading1" or "heading2" or "heading3" or "body" or "abstract.zh" or "abstract.en" or "toc" or "references";
    }

    private static bool IsValidRoleFormatMatch(ProfileRoleFormatMatch? format)
    {
        return format is null
            || (IsValidRange(format.FirstLineIndentTwips)
                && IsValidRange(format.LeftIndentTwips)
                && IsValidRange(format.RightIndentTwips));
    }

    private static bool IsValidRange(IntRangeMatch? range)
    {
        if (range is null)
        {
            return true;
        }

        if (range.Exact is null && range.Min is null && range.Max is null)
        {
            return false;
        }

        if (range.Exact is not null && (range.Min is not null || range.Max is not null))
        {
            return false;
        }

        return range.Min is null || range.Max is null || range.Min <= range.Max;
    }

    private static CliResult ProfileInvalid(string path, string message)
    {
        return new CliResult
        {
            Status = "error",
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "error",
                    Code = "profile_invalid",
                    Message = message,
                    Path = path
                }
            ]
        };
    }

    private static CliResult WithLock(SessionPaths paths, Func<CliResult> action)
    {
        if (!Directory.Exists(paths.Workspace))
        {
            return SessionStore.Error(paths, "workspace_missing", $"Workspace not found: {paths.Workspace}");
        }

        var lockFile = SessionLock.TryAcquire(paths);
        if (lockFile is null)
        {
            return SessionStore.Error(paths, "workspace_locked", "The workspace is locked by session.lock.");
        }

        try
        {
            return action();
        }
        finally
        {
            lockFile.Dispose();
            SessionLock.Release(paths);
        }
    }

    private static bool TryLoadReadySession(SessionPaths paths, out SessionState session, out CliResult? error)
    {
        if (!SessionStore.TryLoad(paths, out session, out error))
        {
            return false;
        }

        if (!File.Exists(session.WorkingPath))
        {
            error = SessionStore.Error(paths, "working_doc_missing", $"Working document not found: {session.WorkingPath}");
            return false;
        }

        return true;
    }

    private static CliResult Success(
        SessionPaths paths,
        SessionState session,
        SnapshotInfo? snapshot = null,
        List<SnapshotInfo>? snapshots = null,
        string? outputPath = null)
    {
        return new CliResult
        {
            Status = "success",
            Workspace = paths.Workspace,
            Document = session.WorkingPath,
            OutputPath = outputPath,
            Session = session,
            Snapshot = snapshot,
            Snapshots = snapshots ?? []
        };
    }

    private static List<SnapshotInfo> ListSnapshots(SessionPaths paths)
    {
        if (!Directory.Exists(paths.SnapshotsDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(paths.SnapshotsDirectory, "*.docx")
            .Select(path => new SnapshotInfo
            {
                Created = false,
                Id = Path.GetFileNameWithoutExtension(path),
                Path = Path.GetFullPath(path)
            })
            .Where(snapshot => SnapshotIdentifiers.IsSafeSnapshotId(snapshot.Id))
            .OrderBy(snapshot => snapshot.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static bool SnapshotMatches(string? id, string lookup)
    {
        if (id is null)
        {
            return false;
        }

        if (string.Equals(id, lookup, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var suffixStart = id.IndexOf('-', StringComparison.Ordinal);
        return suffixStart >= 0
            && string.Equals(id[(suffixStart + 1)..], lookup, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathInsideDirectory(string directory, string path)
    {
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static class SnapshotIdentifiers
    {
        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9"
        };

        public static bool TrySanitizeName(string value, out string sanitized)
        {
            sanitized = "";
            if (!IsSafeInput(value))
            {
                return false;
            }

            var builder = new StringBuilder();
            var lastWasDash = false;
            foreach (var ch in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                    lastWasDash = false;
                    continue;
                }

                if (ch is '-' or '_' or ' ')
                {
                    if (!lastWasDash && builder.Length > 0)
                    {
                        builder.Append('-');
                        lastWasDash = true;
                    }
                }
            }

            sanitized = builder.ToString().Trim('-');
            return IsSafeSnapshotComponent(sanitized);
        }

        public static bool IsSafeLookup(string value)
        {
            return IsSafeInput(value) && IsSafeSnapshotComponent(value);
        }

        public static bool IsSafeSnapshotId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            return IsSafeInput(id) && IsSafeSnapshotComponent(id);
        }

        private static bool IsSafeInput(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.Contains("..", StringComparison.Ordinal)
                || value.Contains(Path.DirectorySeparatorChar)
                || value.Contains(Path.AltDirectorySeparatorChar)
                || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            return true;
        }

        private static bool IsSafeSnapshotComponent(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value is "." or "..")
            {
                return false;
            }

            if (ReservedNames.Contains(value)
                || value.Split('-', StringSplitOptions.RemoveEmptyEntries).Any(ReservedNames.Contains))
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (!char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
