using Thesis.Schema;

namespace Thesis.Core;

public static class ContentPaths
{
    public static IEnumerable<string> MarkdownBodies(ThesisContent content) => content.Chapters.SelectMany(c =>
        new[] { c.Markdown }.Concat(c.Sections.Select(s => s.Markdown))).OfType<string>();

    public static IEnumerable<string> Assets(ThesisContent content) => Blocks(content)
        .SelectMany(b => new[] { b.Path, b.SourcePath }).OfType<string>()
        .Concat(MarkdownBodies(content).SelectMany(m => MarkdownSupport.AssetPaths(m, content.AssetDirectory)))
        .Distinct(FileOutputTransaction.PathComparer);
    public static IEnumerable<ThesisContentBlock> Blocks(ThesisContent content) =>
        content.Chapters.SelectMany(c => (c.Blocks ?? []).Concat((c.Sections ?? []).SelectMany(s => s.Blocks ?? [])));

    public static void Resolve(ThesisContent content, string contentPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(contentPath))!;
        content.AssetDirectory = Path.GetFullPath(content.AssetDirectory, directory);
        foreach (var block in Blocks(content))
        {
            if (block.Path is not null) block.Path = Path.GetFullPath(block.Path, directory);
            if (block.SourcePath is not null) block.SourcePath = Path.GetFullPath(block.SourcePath, directory);
        }
    }

    public static void ProtectAssets(ThesisContent content, string outputPath)
    {
        var output = Path.GetFullPath(outputPath);
        if (Assets(content)
            .Any(input => FileOutputTransaction.PathComparer.Equals(Path.GetFullPath(input), output)))
            throw new InvalidDataException("Output must not overwrite content source snapshots or images.");
    }
}
