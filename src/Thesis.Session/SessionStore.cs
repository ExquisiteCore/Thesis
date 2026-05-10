using System.Text.Json;
using Thesis.Schema;

namespace Thesis.Session;

internal static class SessionStore
{
    public static bool TryLoad(SessionPaths paths, out SessionState session, out CliResult? error)
    {
        session = new SessionState();
        error = null;

        if (!File.Exists(paths.SessionJson))
        {
            error = Error(paths, "session_missing", $"Session file not found: {paths.SessionJson}");
            return false;
        }

        try
        {
            session = ThesisJson.Deserialize<SessionState>(File.ReadAllText(paths.SessionJson));
        }
        catch (JsonException ex)
        {
            error = Error(paths, "session_invalid", $"Session file is invalid JSON: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            error = Error(paths, "session_unreadable", $"Session file could not be read: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = Error(paths, "session_unreadable", $"Session file could not be read: {ex.Message}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(session.OriginalPath)
            || string.IsNullOrWhiteSpace(session.WorkingPath)
            || string.IsNullOrWhiteSpace(session.ProfilePath)
            || session.SnapshotCounter < 0)
        {
            error = Error(paths, "session_invalid", "Session file is missing required state.");
            return false;
        }

        session.OriginalPath = Path.GetFullPath(session.OriginalPath);
        session.WorkingPath = Path.GetFullPath(session.WorkingPath);
        session.ProfilePath = Path.GetFullPath(session.ProfilePath);

        if (!SamePath(session.WorkingPath, paths.WorkingDocument)
            || !SamePath(session.ProfilePath, paths.ProfileJson)
            || SamePath(session.OriginalPath, paths.WorkingDocument)
            || SamePath(session.OriginalPath, paths.ProfileJson))
        {
            error = Error(paths, "session_invalid", "Session file path state does not match the workspace.");
            return false;
        }

        return true;
    }

    public static bool TrySave(SessionPaths paths, SessionState session, out CliResult? error)
    {
        error = null;

        try
        {
            File.WriteAllText(paths.SessionJson, ThesisJson.Serialize(session));
            return true;
        }
        catch (IOException ex)
        {
            error = Error(paths, "session_write_failed", $"Session file could not be written: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = Error(paths, "session_write_failed", $"Session file could not be written: {ex.Message}");
            return false;
        }
    }

    public static CliResult Error(SessionPaths paths, string code, string message)
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

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
