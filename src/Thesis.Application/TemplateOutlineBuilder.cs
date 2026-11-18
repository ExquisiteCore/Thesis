using System.Text.RegularExpressions;
using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Application;

/// <summary>Builds a template outline without copying example prose, and preserves existing authored objects by heading.</summary>
public static class TemplateOutlineBuilder
{
    public static ThesisContent Rebuild(ThesisContent original, string templatePath)
    {
        var result = ThesisJson.Deserialize<ThesisContent>(ThesisJson.Serialize(original));
        ContentNormalizer.Normalize(result);
        if (result.Chapters.Any(chapter => chapter.OrderedContentIncludesSections))
            throw new InvalidDataException("正文保留着导入原稿的完整顺序。请在写作页调整其标题层级，避免重新整理时重复插入标题。");
        var headings = TemplatePlanDraftBuilder.ExtractOutline(templatePath);
        var entries = result.Chapters.SelectMany(chapter => new[] { Entry.From(chapter) }
            .Concat(chapter.Sections.Select(Entry.From))).ToList();
        var used = new HashSet<string>();
        var chapters = new List<ThesisChapterContent>();
        ThesisChapterContent? currentChapter = null;
        ThesisSectionContent? currentSection = null;
        var thirdLevelIndex = 0;
        foreach (var (title, level) in headings)
        {
            var match = entries.FirstOrDefault(entry => !used.Contains(entry.Id) && Key(entry.Title) == Key(title));
            if (level == 1)
            {
                if (match is not null) used.Add(match.Id);
                currentChapter = new() { Title = title, Id = match?.Id ?? Guid.NewGuid().ToString("N"),
                    Markdown = match?.Markdown, Paragraphs = match?.Paragraphs ?? [], Blocks = match?.Blocks ?? [], Tables = match?.Tables ?? [] };
                chapters.Add(currentChapter);
                currentSection = null;
                thirdLevelIndex = 0;
            }
            else if (currentChapter is not null && level == 2)
            {
                if (match is not null) used.Add(match.Id);
                currentSection = new() { Title = title, Id = match?.Id ?? Guid.NewGuid().ToString("N"),
                    Markdown = match?.Markdown, Paragraphs = match?.Paragraphs ?? [], Blocks = match?.Blocks ?? [], Tables = match?.Tables ?? [] };
                currentChapter.Sections.Add(currentSection);
                thirdLevelIndex = 0;
            }
            else if (currentChapter is not null && level == 3)
            {
                // Level 3 lives in the ordered content of its parent section, keeping object order intact.
                var blocks = currentSection?.Blocks ?? currentChapter.Blocks;
                thirdLevelIndex++;
                var numberedTitle = currentSection is null ? title
                    : Regex.Replace(title, @"^\d+[\.．]\d+[\.．]\d+", $"{chapters.Count}.{currentChapter.Sections.Count}.{thirdLevelIndex}");
                if (blocks.Any(block => block.ParagraphStyle == "heading3" && HeadingName(block.Text ?? "") == HeadingName(title))) continue;
                if ((currentSection?.Markdown ?? currentChapter.Markdown) is not null)
                    throw new InvalidDataException($"“{currentSection?.Title ?? currentChapter.Title}”包含 Markdown 正文，请先在写作页转换该部分再整理大纲。");
                if (match?.Markdown is not null)
                    throw new InvalidDataException($"三级标题“{title}”包含 Markdown 正文，请先在写作页转换该部分再整理大纲。");
                blocks.Add(new() { Type = "paragraph", ParagraphStyle = "heading3", Text = numberedTitle });
                if (match is null) continue;
                used.Add(match.Id);
                blocks.AddRange(match.Blocks);
                blocks.AddRange(match.Paragraphs.Select(text => new ThesisContentBlock { Text = text }));
                blocks.AddRange(match.Tables.Select(table => new ThesisContentBlock { Type = "table", Table = table }));
            }
        }
        if (chapters.Count == 0) throw new InvalidDataException("模板尚未识别到正文一级标题，请先在模板工作台核对样本。");
        var unmatched = entries.Where(entry => !used.Contains(entry.Id) && entry.HasContent).Select(entry => entry.Title).ToArray();
        if (unmatched.Length > 0)
            throw new InvalidDataException("以下正文尚未与模板标题匹配，原大纲已保留：" + string.Join("、", unmatched));
        result.Chapters = chapters;
        return result;
    }

    private static string Key(string title)
    {
        var text = Regex.Replace(title, @"[一二三]级标题排版[：:].*$", "").Trim();
        if (HeadingTitleNormalizer.TryParseChapter(text, out var number, out var name)) text = $"第{number}章{name}";
        return Regex.Replace(text, @"\s+", "");
    }

    private static string HeadingName(string title) => Regex.Replace(Key(title), @"^\d+(?:[\.．]\d+){1,2}", "");

    private sealed record Entry(string Id, string Title, string? Markdown, List<string> Paragraphs, List<ThesisContentBlock> Blocks, List<ThesisTableContent> Tables)
    {
        public bool HasContent => !string.IsNullOrWhiteSpace(Markdown) || Paragraphs.Count > 0 || Blocks.Count > 0 || Tables.Count > 0;
        public static Entry From(ThesisChapterContent chapter) => new(chapter.Id, chapter.Title, chapter.Markdown, chapter.Paragraphs, chapter.Blocks, chapter.Tables);
        public static Entry From(ThesisSectionContent section) => new(section.Id, section.Title, section.Markdown, section.Paragraphs, section.Blocks, section.Tables);
    }
}
