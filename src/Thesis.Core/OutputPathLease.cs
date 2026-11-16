namespace Thesis.Core;

/// <summary>Reserve every destination before beginning a multi-file delivery.</summary>
public sealed class OutputPathLease : IDisposable
{
    private readonly List<FileStream> locks = [];

    private OutputPathLease() { }

    public static OutputPathLease Acquire(IEnumerable<string> paths)
    {
        var lease = new OutputPathLease();
        try
        {
            foreach (var path in paths.Select(Path.GetFullPath).Distinct(FileOutputTransaction.PathComparer).Order(FileOutputTransaction.PathComparer))
            {
                var directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);
                var lockPath = Path.Combine(directory, "." + Path.GetFileName(path) + ".thesis-lock");
                lease.locks.Add(new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    public void Dispose()
    {
        foreach (var stream in locks.AsEnumerable().Reverse()) stream.Dispose();
        locks.Clear();
    }
}
