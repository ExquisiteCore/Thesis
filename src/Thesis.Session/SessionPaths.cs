namespace Thesis.Session;

public sealed class SessionPaths
{
    private SessionPaths(string workspace)
    {
        Workspace = Path.GetFullPath(workspace);
        SessionJson = Path.Combine(Workspace, "session.json");
        ProfileJson = Path.Combine(Workspace, "profile.json");
        WorkingDocument = Path.Combine(Workspace, "working.docx");
        LockFile = Path.Combine(Workspace, "session.lock");
        SnapshotsDirectory = Path.Combine(Workspace, "snapshots");
        LogsDirectory = Path.Combine(Workspace, "logs");
        CacheDirectory = Path.Combine(Workspace, "cache");
    }

    public string Workspace { get; }

    public string SessionJson { get; }

    public string ProfileJson { get; }

    public string WorkingDocument { get; }

    public string LockFile { get; }

    public string SnapshotsDirectory { get; }

    public string LogsDirectory { get; }

    public string CacheDirectory { get; }

    public static SessionPaths FromWorkspace(string workspace)
    {
        return new SessionPaths(workspace);
    }
}
