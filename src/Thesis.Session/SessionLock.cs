namespace Thesis.Session;

internal static class SessionLock
{
    public static FileStream? TryAcquire(SessionPaths paths)
    {
        try
        {
            var stream = new FileStream(paths.LockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(DateTimeOffset.UtcNow.ToString("O"));
            writer.Flush();
            stream.Position = stream.Length;
            return stream;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Release(SessionPaths paths)
    {
        try
        {
            if (File.Exists(paths.LockFile))
            {
                File.Delete(paths.LockFile);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
