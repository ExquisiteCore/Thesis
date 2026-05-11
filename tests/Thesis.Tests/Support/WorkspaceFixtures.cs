internal static partial class Program
{
    static WorkspaceContext CreateInitializedWorkspace(string root)
    {
        Directory.CreateDirectory(root);

        var sourceDoc = Path.GetFullPath(Path.Combine(root, "source.docx"));
        var profile = Path.Combine(root, "input-profile.json");
        var workspace = Path.Combine(root, ".thesis");

        File.WriteAllText(sourceDoc, "original body");
        File.WriteAllText(profile, "{}");

        var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);
        AssertEqual("success", result.Status);

        return new WorkspaceContext(
            sourceDoc,
            profile,
            Path.GetFullPath(workspace),
            SessionPaths.FromWorkspace(workspace),
            File.ReadAllBytes(sourceDoc));
    }

    static WorkspaceContext CreateInitializedDocxWorkspace(string root)
    {
        Directory.CreateDirectory(root);

        var sourceDoc = Path.GetFullPath(Path.Combine(root, "source.docx"));
        var profile = Path.Combine(root, "input-profile.json");
        var workspace = Path.Combine(root, ".thesis");

        WriteFixtureDocx(sourceDoc);
        File.WriteAllText(profile, "{}");

        var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);
        AssertEqual("success", result.Status);

        return new WorkspaceContext(
            sourceDoc,
            profile,
            Path.GetFullPath(workspace),
            SessionPaths.FromWorkspace(workspace),
            File.ReadAllBytes(sourceDoc));
    }

    static WorkspaceContext CreateInitializedFormatMatchDocxWorkspace(string root)
    {
        Directory.CreateDirectory(root);

        var sourceDoc = Path.GetFullPath(Path.Combine(root, "source.docx"));
        var profile = Path.Combine(root, "input-profile.json");
        var workspace = Path.Combine(root, ".thesis");

        WriteFormatMatchFixtureDocx(sourceDoc);
        File.WriteAllText(profile, "{}");

        var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);
        AssertEqual("success", result.Status);

        return new WorkspaceContext(
            sourceDoc,
            profile,
            Path.GetFullPath(workspace),
            SessionPaths.FromWorkspace(workspace),
            File.ReadAllBytes(sourceDoc));
    }

}
