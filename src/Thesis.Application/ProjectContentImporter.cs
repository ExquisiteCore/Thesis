using System.Security.Cryptography;
using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Application;

public static class ProjectContentImporter
{
    public static (ThesisContent Content, ContentExtractReport? Report) Import(string input, string projectPath)
    {
        input = Path.GetFullPath(input);
        if (Path.GetExtension(input).Equals(".md", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(input).Equals(".markdown", StringComparison.OrdinalIgnoreCase))
            return (MarkdownProjectFormat.Import(input, projectPath), null);
        if (Path.GetExtension(input).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var content = ThesisJson.DeserializeStrict<ThesisContent>(File.ReadAllText(input));
            ThesisProjectStore.Validate(new() { Content = content });
            ContentPaths.Resolve(content, input);
            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            var assets = Path.Combine(projectDirectory, "assets");
            foreach (var block in ContentPaths.Blocks(content))
            {
                if (block.Path is not null) block.Path = MarkdownProjectFormat.CopyAsset(block.Path, assets);
                if (block.SourcePath is not null) block.SourcePath = MarkdownProjectFormat.CopyAsset(block.SourcePath, assets);
            }
            MarkdownProjectFormat.Transform(content, text => MarkdownSupport.RewriteAssets(text, content.AssetDirectory,
                asset => Path.GetRelativePath(projectDirectory, MarkdownProjectFormat.CopyAsset(asset, assets))));
            content.AssetDirectory = projectDirectory;
            return (content, null);
        }
        var bytes = File.ReadAllBytes(input);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "assets");
        Directory.CreateDirectory(directory);
        var asset = Path.Combine(directory, "source-" + hash + ".docx");
        using (var lease = OutputPathLease.Acquire([asset]))
        {
            if (File.Exists(asset))
            {
                if (!SHA256.HashData(File.ReadAllBytes(asset)).SequenceEqual(SHA256.HashData(bytes)))
                    throw new InvalidDataException("正文快照校验失败，请检查项目 assets 目录。");
            }
            else
            {
                using var transaction = new FileOutputTransaction();
                File.WriteAllBytes(transaction.Stage(asset), bytes);
                transaction.Commit();
            }
        }
        if (!OpenXmlDocumentInspector.TryInspect(asset, out var map, out var diagnostic) || map is null)
            throw new InvalidDataException(diagnostic?.Message ?? "正文导入失败。");
        var extracted = ContentExtractBuilder.Build(map, null, null, null, null);
        extracted.Content.AssetDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        return extracted;
    }
}
