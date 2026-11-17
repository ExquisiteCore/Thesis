using System.Security.Cryptography;
using System.Text;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Application;

public sealed record OpenThesisProject(string Path, string Fingerprint, ThesisProject Document);

public static class ThesisProjectStore
{
    public static OpenThesisProject Open(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(path);
        var document = ThesisJson.DeserializeStrict<ThesisProject>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        Validate(document);
        ContentPaths.Resolve(document.Content, path);
        return new(path, Hash(bytes), document);
    }

    public static OpenThesisProject Create(string path, ThesisProject document)
    {
        // The native save picker may have reserved an empty destination already.
        var fingerprint = File.Exists(path) && new FileInfo(path).Length == 0 ? Hash([]) : "";
        return Save(new(path, fingerprint, document));
    }

    /// <summary>Compare-and-swap under a shared output lease; preserve external/Agent edits.</summary>
    public static OpenThesisProject Save(OpenThesisProject opened)
    {
        var path = Path.GetFullPath(opened.Path);
        Validate(opened.Document);
        ContentPaths.Resolve(opened.Document.Content, path);
        using var lease = OutputPathLease.Acquire([path]);
        var previous = File.Exists(path) ? File.ReadAllBytes(path) : [];
        var current = File.Exists(path) ? Hash(previous) : "";
        if (current != opened.Fingerprint) throw new IOException("项目已被其他程序修改。请先保留当前内容，再重新打开项目合并修改。");
        var copy = ThesisJson.Deserialize<ThesisProject>(ThesisJson.Serialize(opened.Document));
        copy.Revision = checked(copy.Revision + 1);
        var directory = Path.GetDirectoryName(path)!;
        ContentPaths.Resolve(copy.Content, path);
        copy.Content.AssetDirectory = Path.GetRelativePath(directory, copy.Content.AssetDirectory);
        foreach (var block in ContentPaths.Blocks(copy.Content))
        {
            if (block.Path is not null) block.Path = Path.GetRelativePath(directory, block.Path);
            if (block.SourcePath is not null) block.SourcePath = Path.GetRelativePath(directory, block.SourcePath);
        }
        var bytes = Encoding.UTF8.GetBytes(ThesisJson.Serialize(copy));
        using var transaction = new FileOutputTransaction();
        ProjectHistoryStore.StagePrevious(opened, previous, transaction);
        File.WriteAllBytes(transaction.Stage(path), bytes);
        transaction.Commit();
        opened.Document.Revision = copy.Revision;
        return new(path, Hash(bytes), opened.Document);
    }

    public static string? Resolve(OpenThesisProject project, string? path) => string.IsNullOrWhiteSpace(path)
        ? null : Path.GetFullPath(path, Path.GetDirectoryName(project.Path)!);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static void Validate(ThesisProject project)
    {
        if (project.SchemaVersion != "1.0" || project.DocumentKind != "thesisProject" || !Guid.TryParseExact(project.Id, "N", out _) || project.Revision < 0)
            throw new InvalidDataException("论文项目格式或版本不受支持。");
        var content = project.Content ?? throw new InvalidDataException("项目缺少正文。");
        if (string.IsNullOrWhiteSpace(content.AssetDirectory) || project.CoverFields is null) throw new InvalidDataException("项目资源目录或封面字段无效。");
        if (content.DocumentKind != "thesisContent" || content.SchemaVersion != "1.0")
            throw new InvalidDataException("正文格式或版本不受支持。");
        if (content.Chapters is null || content.References is null || content.KeywordsZh is null || content.KeywordsEn is null)
            throw new InvalidDataException("论文项目包含空的内容集合。");
        var ids = new HashSet<string>();
        foreach (var chapter in content.Chapters)
        {
            if (chapter is null || string.IsNullOrWhiteSpace(chapter.Id) || !ids.Add(chapter.Id)
                || chapter.Sections is null || chapter.Paragraphs is null || chapter.Blocks is null || chapter.Tables is null)
                throw new InvalidDataException("章节标识重复或章节数据不完整。");
            foreach (var section in chapter.Sections)
                if (section is null || string.IsNullOrWhiteSpace(section.Id) || !ids.Add(section.Id)
                    || section.Paragraphs is null || section.Blocks is null || section.Tables is null)
                    throw new InvalidDataException("小节标识重复或小节数据不完整。");
            if (chapter.Blocks.Any(b => b is null) || chapter.Sections.Any(s => s.Blocks.Any(b => b is null)))
                throw new InvalidDataException("正文包含空内容块。");
        }
    }
}
