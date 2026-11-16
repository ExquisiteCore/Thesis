namespace Thesis.Core;

/// <summary>Stages sibling files and promotes them only after all work succeeds.</summary>
public sealed class FileOutputTransaction : IDisposable
{
    private readonly List<Entry> entries = [];
    private bool committed;
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public string Stage(string destination)
    {
        if (committed) throw new InvalidOperationException("Transaction was already committed.");
        var full = Path.GetFullPath(destination);
        if (entries.Any(e => PathComparer.Equals(e.Destination, full))) throw new ArgumentException("Duplicate output path.");
        if (!Directory.Exists(Path.GetDirectoryName(full))) throw new DirectoryNotFoundException($"Output directory not found: {Path.GetDirectoryName(full)}");
        if (Directory.Exists(full)) throw new IOException($"Output is a directory: {full}");
        var staged = Path.Combine(Path.GetDirectoryName(full)!, "." + Path.GetFileNameWithoutExtension(full) + ".stage-" + Guid.NewGuid().ToString("N") + Path.GetExtension(full));
        entries.Add(new Entry(full, staged));
        return staged;
    }

    public void Commit()
    {
        if (committed) throw new InvalidOperationException("Transaction was already committed.");
        foreach (var entry in entries)
            if (!File.Exists(entry.Staged)) throw new FileNotFoundException("A staged output is missing.", entry.Staged);
        try
        {
            foreach (var entry in entries)
            {
                if (File.Exists(entry.Destination))
                {
                    entry.Backup = entry.Staged + ".previous";
                    File.Replace(entry.Staged, entry.Destination, entry.Backup);
                }
                else File.Move(entry.Staged, entry.Destination);
                entry.Promoted = true;
            }
            committed = true;
        }
        catch
        {
            foreach (var entry in entries.AsEnumerable().Reverse().Where(e => e.Promoted))
            {
                // Keep a backup if restoration itself fails, so recovery is possible.
                if (entry.Backup is not null) File.Move(entry.Backup, entry.Destination, true);
                else File.Delete(entry.Destination);
                entry.Promoted = false;
            }
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var entry in entries)
        {
            TryDelete(entry.Staged);
            if (committed && entry.Backup is not null) TryDelete(entry.Backup);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class Entry(string destination, string staged)
    {
        public string Destination { get; } = destination;
        public string Staged { get; } = staged;
        public string? Backup { get; set; }
        public bool Promoted { get; set; }
    }
}
