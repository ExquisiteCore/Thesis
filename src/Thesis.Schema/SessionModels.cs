namespace Thesis.Schema;

public sealed class SessionState
{
    public string SchemaVersion { get; set; } = "1.0";

    public string OriginalPath { get; set; } = "";

    public string WorkingPath { get; set; } = "";

    public string ProfilePath { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public int SnapshotCounter { get; set; }
}

public sealed class SnapshotInfo
{
    public bool Created { get; set; }

    public string? Id { get; set; }

    public string? Path { get; set; }
}

