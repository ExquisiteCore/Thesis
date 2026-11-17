using System.Security.Cryptography;
using System.Text;
using Markdig.Syntax;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Application;

/// <summary>A readable Markdown interchange format. Source spans keep code, math and inline syntax intact.</summary>
public static class MarkdownProjectFormat
{
    public static ThesisContent Import(string path, string projectPath)
    {
        var content = Parse(File.ReadAllText(path));
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        content.AssetDirectory = targetDirectory;
        Transform(content, text => MarkdownSupport.RewriteAssets(text, sourceDirectory,
            asset => Path.GetRelativePath(targetDirectory, CopyAsset(asset, Path.Combine(targetDirectory, "assets")))));
        return content;
    }

    public static ThesisContent Parse(string markdown)
    {
        markdown = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var result = new ThesisContent();
        var document = MarkdownSupport.Parse(markdown);
        var headings = document.OfType<HeadingBlock>().Where(h => h.Level <= 3).ToArray();
        // An initial H1 is the document title. H2/H3 represent chapters/sections.
        var title = headings.FirstOrDefault(h => h.Level == 1);
        if (title is not null) result.Title = MarkdownSupport.PlainText(title.Inline);
        var metadata = document.OfType<FencedCodeBlock>().FirstOrDefault(b => b.Info == "thesis-metadata");
        if (metadata is not null)
        {
            var values = ThesisJson.DeserializeStrict<MarkdownMetadata>(metadata.Lines.ToString());
            result.Author = values.Author; result.KeywordsZh = values.KeywordsZh; result.KeywordsEn = values.KeywordsEn;
        }
        ThesisChapterContent? chapter = null;
        for (var i = 0; i < headings.Length; i++)
        {
            var h = headings[i];
            if (h == title) continue;
            var start = h.Span.End + 1;
            var end = i + 1 < headings.Length ? headings[i + 1].Span.Start : markdown.Length;
            var body = markdown[start..end].Trim('\n');
            var name = MarkdownSupport.PlainText(h.Inline);
            if (h.Level <= 2)
            {
                chapter = null;
                switch (name.Trim().ToLowerInvariant())
                {
                    case "摘要": case "中文摘要": result.AbstractZh = body; continue;
                    case "abstract": case "英文摘要": result.AbstractEn = body; continue;
                    case "参考文献": case "references": result.References = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(); continue;
                    case "致谢": case "acknowledgements": result.Acknowledgements = body; continue;
                }
                chapter = new() { Title = name, Markdown = body };
                result.Chapters.Add(chapter);
            }
            else
            {
                chapter ??= AddChapter(result, "正文");
                chapter.Sections.Add(new() { Title = name, Markdown = body });
            }
        }
        // Preserve unheaded prose, including a document consisting only of text.
        var first = headings.FirstOrDefault(h => h != title)?.Span.Start ?? markdown.Length;
        var preamble = markdown[..first];
        var removals = new List<SourceSpan>();
        if (title is not null && title.Span.Start < first) removals.Add(title.Span);
        if (metadata is not null && metadata.Span.Start < first) removals.Add(metadata.Span);
        foreach (var span in removals.OrderByDescending(s => s.Start)) preamble = preamble.Remove(span.Start, span.Length);
        if (!string.IsNullOrWhiteSpace(preamble)) result.Chapters.Insert(0, new() { Title = "正文", Markdown = preamble.Trim('\n') });
        if (result.Title.Length == 0) result.Title = "未命名论文";
        return result;
    }

    public static string Serialize(ThesisContent content)
    {
        var b = new StringBuilder().Append("# ").AppendLine(content.Title).AppendLine();
        b.AppendLine("```thesis-metadata").AppendLine(ThesisJson.Serialize(new MarkdownMetadata { Author = content.Author, KeywordsZh = content.KeywordsZh, KeywordsEn = content.KeywordsEn })).AppendLine("```").AppendLine();
        void Section(string name, string? text, int level = 2)
        {
            if (text is null) return;
            b.Append(new string('#', level)).Append(' ').AppendLine(name).AppendLine().AppendLine(text).AppendLine();
        }
        Section("摘要", content.AbstractZh); Section("Abstract", content.AbstractEn);
        foreach (var chapter in content.Chapters)
        {
            Section(chapter.Title, chapter.Markdown ?? MarkdownSupport.Body(chapter.Paragraphs, chapter.Blocks, chapter.Tables));
            if (!chapter.OrderedContentIncludesSections)
                foreach (var section in chapter.Sections)
                    Section(section.Title, section.Markdown ?? MarkdownSupport.Body(section.Paragraphs, section.Blocks, section.Tables), 3);
        }
        if (content.References.Count > 0) Section("参考文献", string.Join("\n", content.References));
        Section("致谢", content.Acknowledgements);
        return b.ToString().Replace("\r\n", "\n");
    }

    public static void Export(ThesisContent content, string path)
    {
        path = Path.GetFullPath(path);
        ContentPaths.ProtectAssets(content, path);
        var directory = Path.GetDirectoryName(path)!;
        var assets = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + ".assets");
        var text = MarkdownSupport.RewriteAssets(Serialize(content), content.AssetDirectory,
            source => Path.GetRelativePath(directory, CopyAsset(source, assets)));
        using var lease = OutputPathLease.Acquire([path]);
        using var transaction = new FileOutputTransaction();
        File.WriteAllText(transaction.Stage(path), text, new UTF8Encoding(false));
        transaction.Commit();
    }

    internal static string CopyAsset(string path, string directory)
    {
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, hash + Path.GetExtension(path).ToLowerInvariant());
        using var lease = OutputPathLease.Acquire([destination]);
        if (File.Exists(destination))
        {
            using var existing = File.OpenRead(destination);
            if (Convert.ToHexString(SHA256.HashData(existing)).ToLowerInvariant() != hash) throw new IOException("项目资源校验失败：" + Path.GetFileName(destination));
            return destination;
        }
        using var transaction = new FileOutputTransaction();
        stream.Position = 0;
        using (var output = File.Create(transaction.Stage(destination))) stream.CopyTo(output);
        transaction.Commit();
        return destination;
    }

    internal static void Transform(ThesisContent content, Func<string, string> transform)
    {
        foreach (var chapter in content.Chapters)
        {
            if (chapter.Markdown is not null) chapter.Markdown = transform(chapter.Markdown);
            foreach (var section in chapter.Sections)
                if (section.Markdown is not null) section.Markdown = transform(section.Markdown);
        }
    }
    private static ThesisChapterContent AddChapter(ThesisContent content, string title)
    {
        var chapter = new ThesisChapterContent { Title = title, Markdown = "" }; content.Chapters.Add(chapter); return chapter;
    }
    public sealed class MarkdownMetadata
    {
        public string? Author { get; set; }
        public List<string> KeywordsZh { get; set; } = [];
        public List<string> KeywordsEn { get; set; } = [];
    }
}
