using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Application;

public sealed class WorkspacePreferences
{
    public string Theme { get; set; } = "Default";
    public List<RecentProject> RecentProjects { get; set; } = [];
    public bool ShowInspector { get; set; } = true;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thesis", "workspace.json");
    [System.Text.Json.Serialization.JsonIgnore]
    public string StoragePath { get; set; } = FilePath;
    public static WorkspacePreferences Load(string? path = null)
    {
        path ??= FilePath;
        try
        {
            var result = File.Exists(path) ? ThesisJson.Deserialize<WorkspacePreferences>(File.ReadAllText(path)) : new();
            result.StoragePath = path;
            result.RecentProjects ??= [];
            return result;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return new() { StoragePath = path }; }
    }
    public void Remember(string path, string title)
    {
        RecentProjects.RemoveAll(p => FileOutputTransaction.PathComparer.Equals(p.Path, path));
        RecentProjects.Insert(0, new(path, title, DateTimeOffset.Now)); RecentProjects = RecentProjects.Take(12).ToList(); Save();
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
        using var lease = OutputPathLease.Acquire([StoragePath]);
        using var transaction = new FileOutputTransaction();
        File.WriteAllText(transaction.Stage(StoragePath), ThesisJson.Serialize(this)); transaction.Commit();
    }
}
