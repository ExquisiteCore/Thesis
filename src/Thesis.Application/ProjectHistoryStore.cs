using System.Security.Cryptography;
using System.Text;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Application;

public static class ProjectHistoryStore
{
    private static string DirectoryFor(OpenThesisProject project) => Path.Combine(Path.GetDirectoryName(project.Path)!, ".thesis", project.Document.Id);
    public static string RecoveryPath(OpenThesisProject project) => Path.Combine(DirectoryFor(project), "recovery.json");
    public static void WriteRecovery(OpenThesisProject project)
    {
        var path = RecoveryPath(project); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var lease = OutputPathLease.Acquire([path]);
        using var transaction = new FileOutputTransaction();
        File.WriteAllText(transaction.Stage(path), ThesisJson.Serialize(project.Document)); transaction.Commit();
    }
    public static void ClearRecovery(OpenThesisProject project) => File.Delete(RecoveryPath(project));
    public static bool HasRecovery(OpenThesisProject project)
    {
        var path = RecoveryPath(project);
        if (!File.Exists(path)) return false;
        try
        {
            // A crash after commit but before cleanup can leave an already-saved draft.
            // Read normalizes paths and revision before comparing actual project data.
            var recovery = Read(project, path);
            return ThesisJson.Serialize(recovery) != ThesisJson.Serialize(project.Document);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
        {
            return true; // Keep damaged recovery files available for inspection.
        }
    }
    public static ThesisProject Read(OpenThesisProject project, string path)
    {
        var document = ThesisJson.DeserializeStrict<ThesisProject>(File.ReadAllText(path));
        ThesisProjectStore.Validate(document);
        if (document.Id != project.Document.Id) throw new InvalidDataException("此快照属于另一个论文项目。");
        ContentPaths.Resolve(document.Content, project.Path);
        document.Revision = project.Document.Revision;
        return document;
    }
    internal static void StagePrevious(OpenThesisProject project, byte[] bytes, FileOutputTransaction transaction)
    {
        if (bytes.Length == 0) return;
        var previous = ThesisJson.DeserializeStrict<ThesisProject>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        var directory = Path.Combine(DirectoryFor(project), "history"); Directory.CreateDirectory(directory);
        // Coalesce automatic saves into five-minute checkpoints; destructive commands call Checkpoint explicitly.
        var last = Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (last is not null && DateTime.UtcNow - File.GetLastWriteTimeUtc(last) < TimeSpan.FromMinutes(5)) return;
        var path = Path.Combine(directory, $"{previous.Revision:D10}-{Convert.ToHexString(SHA256.HashData(bytes))[..12]}.json");
        if (!File.Exists(path)) File.WriteAllBytes(transaction.Stage(path), bytes);
    }
    public static void Checkpoint(OpenThesisProject project)
    {
        var directory = Path.Combine(DirectoryFor(project), "history"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{project.Document.Revision:D10}-{Guid.NewGuid():N}.json");
        using var transaction = new FileOutputTransaction();
        File.WriteAllText(transaction.Stage(path), ThesisJson.Serialize(project.Document)); transaction.Commit();
    }
    public static IReadOnlyList<ProjectHistoryEntry> List(OpenThesisProject project)
    {
        var directory = Path.Combine(DirectoryFor(project), "history");
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(100).Select(path =>
        {
            try { var doc = ThesisJson.DeserializeStrict<ThesisProject>(File.ReadAllText(path)); return new ProjectHistoryEntry(path, File.GetLastWriteTimeUtc(path), doc.Revision, doc.Content.Title); }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return new ProjectHistoryEntry(path, File.GetLastWriteTimeUtc(path), -1, "快照读取失败"); }
        }).ToList();
    }
}
