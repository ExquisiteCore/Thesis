using System.Text.RegularExpressions;
using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Application;

public static class ContentExtractBuilder
{
    public static (ThesisContent Content, ContentExtractReport Report) Build(
        DocumentMap map,
        TemplateProfile? profile,
        ProjectRules? projectRules,
        string? profilePath,
        string? projectRulesPath)
    {
        var analysis = new ContentExtractionAnalysis(map);
        var content = new ThesisContent
        {
            SchemaVersion = "1.0",
            Title = analysis.ExtractTitle(),
            Author = analysis.ExtractAuthor(),
            AbstractZh = analysis.ExtractAbstract(ThesisTextHeuristics.IsChineseAbstractHeading),
            AbstractEn = analysis.ExtractAbstract(ThesisTextHeuristics.IsEnglishAbstractHeading),
            KeywordsZh = analysis.ExtractKeywords(ThesisTextHeuristics.IsChineseKeywords, "关键词"),
            KeywordsEn = analysis.ExtractKeywords(ThesisTextHeuristics.IsEnglishKeywords, "keywords"),
            Chapters = analysis.ExtractChapters(),
            References = analysis.ExtractReferences(),
            Acknowledgements = analysis.ExtractAcknowledgements()
        };

        var report = new ContentExtractReport
        {
            Document = map.Path,
            ProfilePath = profilePath is null ? null : Path.GetFullPath(profilePath),
            ProjectRulesPath = projectRulesPath is null ? null : Path.GetFullPath(projectRulesPath),
            Ready = true,
            Summary = new ContentExtractSummary
            {
                Title = content.Title,
                ChapterCount = content.Chapters.Count,
                TableCount = CountTables(content),
                ReferenceCount = content.References.Count,
                ParagraphCount = map.Paragraphs.Count,
                HeadingCount = map.Paragraphs.Count(paragraph => analysis.IsHeading(paragraph)),
                SectionCount = map.Sections.Count,
                RequirementHintCount = map.RequirementHints.Count
            },
            Findings = []
        };

        var nativeFindings = OrderedContentExtractor.Populate(content, map);
        report.Findings.AddRange(nativeFindings);
        report.Summary.OrderedBlockCount = Thesis.Core.ContentPaths.Blocks(content).Count();
        if (nativeFindings.Any(f => f.Severity == "error")) report.Ready = false;
        AddReportFindings(report, content, map, profile, projectRules);
        return (content, report);
    }

    private static void AddReportFindings(
        ContentExtractReport report,
        ThesisContent content,
        DocumentMap map,
        TemplateProfile? profile,
        ProjectRules? projectRules)
    {
        if (string.IsNullOrWhiteSpace(content.Title))
        {
            report.Ready = false;
            report.Findings.Add(Finding("warning", "title_missing", "Title could not be extracted.", map.Path));
        }

        if (content.Chapters.Count == 0)
        {
            report.Ready = false;
            report.Findings.Add(Finding("warning", "chapter_missing", "No chapter headings were extracted.", map.Path));
        }

        if (content.References.Count == 0 && map.Paragraphs.Any(paragraph => ThesisTextHeuristics.IsReferencesHeading(paragraph.Text)))
        {
            report.Ready = false;
            report.Findings.Add(Finding("warning", "reference_block_empty", "References heading was found but no items were extracted.", map.Path));
        }

        if (map.RequirementHints.Count > 0 && projectRules is null)
        {
            report.Findings.Add(Finding("info", "project_rules_recommended", "Requirement hints were found; review them into project-rules.json before production assembly.", map.Path));
        }

        if (profile is null)
        {
            report.Findings.Add(Finding("info", "profile_not_supplied", "No profile was supplied; extraction used document structure only.", map.Path));
        }
    }

    private static int CountTables(ThesisContent content)
    {
        return content.Chapters.Sum(chapter =>
            chapter.Tables.Count + chapter.Sections.Sum(section => section.Tables.Count));
    }

    private static ContentExtractFinding Finding(string severity, string code, string message, string? path)
    {
        return new ContentExtractFinding
        {
            Severity = severity,
            Code = code,
            Message = message,
            Path = path
        };
    }
}

internal sealed class ContentExtractionAnalysis
{
    private static readonly Regex ChapterHeadingPattern = new(
        @"^第[一二三四五六七八九十百千万零〇两0-9Xx]+章(?:\s+\S.*)?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex SectionHeadingPattern = new(
        @"^\d{1,2}[\.．]\d{1,2}(?:[\.．]\d{1,2})?\s+\S.*$",
        RegexOptions.CultureInvariant);

    private readonly DocumentMap map;
    private readonly List<DocumentParagraph> paragraphs;
    private readonly HashSet<int> consumedParagraphIndexes = [];

    public ContentExtractionAnalysis(DocumentMap map)
    {
        this.map = map;
        paragraphs = map.Paragraphs
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text))
            .OrderBy(paragraph => paragraph.BodyElementIndex)
            .ThenBy(paragraph => paragraph.Index)
            .ToList();
    }

    public string ExtractTitle()
    {
        if (TryExtractCoverTitle(out var coverTitle))
        {
            return coverTitle;
        }

        var styleTitle = paragraphs.FirstOrDefault(paragraph =>
            string.Equals(paragraph.StyleId, "Title", StringComparison.OrdinalIgnoreCase)
            && !IsFrontMatterOrBodyHeading(paragraph.Text));
        if (styleTitle is not null)
        {
            consumedParagraphIndexes.Add(styleTitle.Index);
            return styleTitle.Text.Trim();
        }

        var candidates = paragraphs.TakeWhile(paragraph => !IsBodyStart(paragraph))
            .Where(paragraph => !IsNonTitleFrontMatter(paragraph.Text))
            .Where(paragraph => !Regex.IsMatch(paragraph.Text.Trim(), @"(?:学院|大学|学校)$|^(?:本科|专科|硕士|博士)?毕业(?:设计|论文)|^[\d\s年月日]+$"))
            .ToList();
        // An unlabelled cover often starts with the institution and document kind.
        // Prefer a substantial centered title over an earlier decorative cover line.
        var beforeBody = candidates.FirstOrDefault(p => p.Text.Trim().Length >= 10 && p.Format.Alignment == "center")
            ?? candidates.FirstOrDefault();
        if (beforeBody is not null)
        {
            consumedParagraphIndexes.Add(beforeBody.Index);
            return beforeBody.Text.Trim();
        }

        return "";
    }

    public string? ExtractAuthor()
    {
        var beforeBody = paragraphs.TakeWhile(paragraph => !IsBodyStart(paragraph));
        foreach (var paragraph in beforeBody)
        {
            var text = paragraph.Text.Trim();
            if (!Regex.IsMatch(text, @"学生\s*姓名", RegexOptions.CultureInvariant))
            {
                continue;
            }

            var author = Regex.Replace(text, @"^.*?学生\s*姓名\s*[:：]\s*", "", RegexOptions.CultureInvariant);
            author = Regex.Replace(author, @"\s*(?:班级|学号|班级\s*/\s*学号|指导老师|指导教师).*$", "", RegexOptions.CultureInvariant)
                .Trim();
            author = Regex.Replace(author, @"\s+", "", RegexOptions.CultureInvariant);
            if (!string.IsNullOrWhiteSpace(author))
            {
                consumedParagraphIndexes.Add(paragraph.Index);
                return author;
            }
        }

        return null;
    }

    private bool TryExtractCoverTitle(out string title)
    {
        var beforeBody = paragraphs.TakeWhile(paragraph => !IsBodyStart(paragraph)).ToList();
        for (var index = 0; index < beforeBody.Count; index++)
        {
            var text = beforeBody[index].Text.Trim();
            if (!text.Contains("题", StringComparison.Ordinal) || !text.Contains('目', StringComparison.Ordinal))
            {
                continue;
            }

            var inline = Regex.Replace(text, @"^题\s*目\s*[:：]\s*", "", RegexOptions.CultureInvariant).Trim();
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(inline) && !LooksLikeCoverLabel(inline))
            {
                parts.Add(inline);
                consumedParagraphIndexes.Add(beforeBody[index].Index);
            }

            for (var next = index + 1; next < Math.Min(beforeBody.Count, index + 4); next++)
            {
                var candidate = beforeBody[next].Text.Trim();
                if (string.IsNullOrWhiteSpace(candidate) || LooksLikeCoverLabel(candidate))
                {
                    break;
                }

                parts.Add(candidate);
                consumedParagraphIndexes.Add(beforeBody[next].Index);
            }

            title = string.Join("", parts).Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                return true;
            }
        }

        title = "";
        return false;
    }

    public string? ExtractAbstract(Func<string, bool> headingPredicate)
    {
        var heading = paragraphs.FirstOrDefault(paragraph => headingPredicate(paragraph.Text));
        if (heading is null)
        {
            return null;
        }

        consumedParagraphIndexes.Add(heading.Index);
        var abstractParagraphs = paragraphs
            .Where(paragraph => paragraph.BodyElementIndex > heading.BodyElementIndex)
            .TakeWhile(paragraph => !IsAnyFrontMatterBoundary(paragraph.Text) && !IsChapterHeading(paragraph))
            .Where(paragraph => !ThesisTextHeuristics.IsChineseKeywords(paragraph.Text)
                && !ThesisTextHeuristics.IsEnglishKeywords(paragraph.Text))
            .Select(paragraph =>
            {
                consumedParagraphIndexes.Add(paragraph.Index);
                return paragraph.Text.Trim();
            })
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return abstractParagraphs.Count == 0 ? null : string.Join(Environment.NewLine, abstractParagraphs);
    }

    public List<string> ExtractKeywords(Func<string, bool> keywordPredicate, string label)
    {
        var keywordParagraph = paragraphs.FirstOrDefault(paragraph => keywordPredicate(paragraph.Text));
        if (keywordParagraph is null)
        {
            return [];
        }

        consumedParagraphIndexes.Add(keywordParagraph.Index);
        var text = keywordParagraph.Text.Trim();
        var colonIndex = text.IndexOfAny(['：', ':']);
        if (colonIndex >= 0)
        {
            text = text[(colonIndex + 1)..];
        }
        else if (text.Length > label.Length)
        {
            text = text[label.Length..];
        }

        return text
            .Split(['；', ';', '，', ',', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .ToList();
    }

    public List<ThesisChapterContent> ExtractChapters()
    {
        var chapters = new List<ThesisChapterContent>();
        ThesisChapterContent? currentChapter = null;
        ThesisSectionContent? currentSection = null;

        foreach (var paragraph in paragraphs)
        {
            if (paragraphs.Count == consumedParagraphIndexes.Count)
            {
                break;
            }

            if (consumedParagraphIndexes.Contains(paragraph.Index)
                || IsFrontMatterOrBodyHeading(paragraph.Text)
                || IsAfterReferencesHeading(paragraph)
                || ThesisTextHeuristics.IsLikelyTocLine(paragraph.Text)
                || ThesisTextHeuristics.IsTableCaption(paragraph.Text)
                || ThesisTextHeuristics.IsFigureCaption(paragraph.Text)
                || ThesisTextHeuristics.IsReferencesHeading(paragraph.Text))
            {
                continue;
            }

            if (IsChapterHeading(paragraph))
            {
                currentChapter = new ThesisChapterContent { Title = paragraph.Text.Trim() };
                chapters.Add(currentChapter);
                currentSection = null;
                consumedParagraphIndexes.Add(paragraph.Index);
                continue;
            }

            if (IsSectionHeading(paragraph))
            {
                currentChapter ??= CreateUntitledChapter(chapters);
                currentSection = new ThesisSectionContent { Title = paragraph.Text.Trim() };
                currentChapter.Sections.Add(currentSection);
                consumedParagraphIndexes.Add(paragraph.Index);
                continue;
            }

            if (currentSection is not null)
            {
                currentSection.Paragraphs.Add(paragraph.Text.Trim());
                consumedParagraphIndexes.Add(paragraph.Index);
                continue;
            }

            if (currentChapter is not null)
            {
                currentChapter.Paragraphs.Add(paragraph.Text.Trim());
                consumedParagraphIndexes.Add(paragraph.Index);
            }
        }

        AttachTables(chapters);
        RemoveEmptyParagraphs(chapters);
        return chapters;
    }

    public List<string> ExtractReferences()
    {
        var heading = paragraphs.FirstOrDefault(paragraph => ThesisTextHeuristics.IsReferencesHeading(paragraph.Text));
        if (heading is null)
        {
            return [];
        }

        consumedParagraphIndexes.Add(heading.Index);
        var references = new List<string>();
        foreach (var paragraph in paragraphs.Where(paragraph => paragraph.BodyElementIndex > heading.BodyElementIndex))
        {
            var text = paragraph.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (ThesisTextHeuristics.IsAcknowledgementsHeading(text)
                || ThesisTextHeuristics.IsAppendixHeading(text)
                || IsChapterHeading(paragraph))
            {
                break;
            }

            if (IsReferenceItem(text))
            {
                references.Add(text);
                consumedParagraphIndexes.Add(paragraph.Index);
            }
        }

        return references;
    }

    public string? ExtractAcknowledgements()
    {
        var heading = paragraphs.FirstOrDefault(paragraph => ThesisTextHeuristics.IsAcknowledgementsHeading(paragraph.Text));
        if (heading is null)
        {
            return null;
        }

        consumedParagraphIndexes.Add(heading.Index);
        var items = paragraphs
            .Where(paragraph => paragraph.BodyElementIndex > heading.BodyElementIndex)
            .TakeWhile(paragraph => !IsChapterHeading(paragraph)
                && !ThesisTextHeuristics.IsReferencesHeading(paragraph.Text)
                && !ThesisTextHeuristics.IsAppendixHeading(paragraph.Text))
            .Select(paragraph =>
            {
                consumedParagraphIndexes.Add(paragraph.Index);
                return paragraph.Text.Trim();
            })
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return items.Count == 0 ? null : string.Join(Environment.NewLine, items);
    }

    public bool IsHeading(DocumentParagraph paragraph)
    {
        return paragraph.OutlineLevel is not null
            || IsChapterHeading(paragraph)
            || IsSectionHeading(paragraph)
            || ThesisTextHeuristics.IsSpecialSemanticHeading(paragraph.Text);
    }

    private void AttachTables(List<ThesisChapterContent> chapters)
    {
        foreach (var table in map.Tables.OrderBy(table => table.BodyElementIndex))
        {
            var chapter = chapters.LastOrDefault(chapter =>
                ChapterStart(chapter) < table.BodyElementIndex);
            if (chapter is null)
            {
                continue;
            }

            var contentTable = ToContentTable(table);
            var section = chapter.Sections.LastOrDefault(section =>
                SectionStart(section) < table.BodyElementIndex);
            if (section is not null)
            {
                section.Tables.Add(contentTable);
            }
            else
            {
                chapter.Tables.Add(contentTable);
            }
        }
    }

    private ThesisTableContent ToContentTable(DocumentTable table)
    {
        var rows = table.Rows
            .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .ToList();
        return new ThesisTableContent
        {
            Caption = ExtractTableCaption(table),
            Headers = rows.Count == 0 ? [] : rows[0],
            Rows = rows.Count <= 1 ? [] : rows.Skip(1).ToList()
        };
    }

    private string? ExtractTableCaption(DocumentTable table)
    {
        var caption = paragraphs
            .Where(paragraph => paragraph.BodyElementIndex < table.BodyElementIndex)
            .OrderByDescending(paragraph => paragraph.BodyElementIndex)
            .TakeWhile(paragraph => !IsHeading(paragraph))
            .FirstOrDefault(paragraph => ThesisTextHeuristics.IsTableCaption(paragraph.Text));
        if (caption is null)
        {
            return null;
        }

        consumedParagraphIndexes.Add(caption.Index);
        return caption.Text.Trim();
    }

    private int ChapterStart(ThesisChapterContent chapter)
    {
        return paragraphs.FirstOrDefault(paragraph => string.Equals(paragraph.Text.Trim(), chapter.Title, StringComparison.Ordinal))?.BodyElementIndex ?? -1;
    }

    private int SectionStart(ThesisSectionContent section)
    {
        return paragraphs.FirstOrDefault(paragraph => string.Equals(paragraph.Text.Trim(), section.Title, StringComparison.Ordinal))?.BodyElementIndex ?? -1;
    }

    private static ThesisChapterContent CreateUntitledChapter(List<ThesisChapterContent> chapters)
    {
        var chapter = new ThesisChapterContent { Title = "未命名章节" };
        chapters.Add(chapter);
        return chapter;
    }

    private static void RemoveEmptyParagraphs(List<ThesisChapterContent> chapters)
    {
        foreach (var chapter in chapters)
        {
            chapter.Paragraphs = chapter.Paragraphs.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
            foreach (var section in chapter.Sections)
            {
                section.Paragraphs = section.Paragraphs.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
            }
        }
    }

    private static bool IsChapterHeading(DocumentParagraph paragraph)
    {
        var text = paragraph.Text.Trim();
        if (HeadingTitleNormalizer.IsSection(text)) return false;
        return HeadingTitleNormalizer.TryParseChapter(text, out _, out _)
            || paragraph.OutlineLevel == 0
            || ThesisTextHeuristics.IsDirectHeading1(paragraph)
            || ChapterHeadingPattern.IsMatch(text);
    }

    private static bool IsSectionHeading(DocumentParagraph paragraph)
    {
        var text = paragraph.Text.Trim();
        return HeadingTitleNormalizer.IsSection(text)
            || paragraph.OutlineLevel is 1 or 2
            || ThesisTextHeuristics.IsDirectHeading2(paragraph)
            || ThesisTextHeuristics.IsDirectHeading3(paragraph)
            || SectionHeadingPattern.IsMatch(text);
    }

    private static bool IsReferenceItem(string text)
    {
        return Regex.IsMatch(text.Trim(), @"^\[\d+\]", RegexOptions.CultureInvariant)
            || Regex.IsMatch(text, @"\[[JMDCPN]\]", RegexOptions.CultureInvariant);
    }

    private bool IsAfterReferencesHeading(DocumentParagraph paragraph)
    {
        var references = paragraphs.FirstOrDefault(item => ThesisTextHeuristics.IsReferencesHeading(item.Text));
        return references is not null && paragraph.BodyElementIndex > references.BodyElementIndex;
    }

    private static bool IsBodyStart(DocumentParagraph paragraph)
    {
        return ThesisTextHeuristics.IsChineseAbstractHeading(paragraph.Text)
            || ThesisTextHeuristics.IsEnglishAbstractHeading(paragraph.Text)
            || IsChapterHeading(paragraph);
    }

    private static bool IsAnyFrontMatterBoundary(string text)
    {
        return ThesisTextHeuristics.IsEnglishAbstractHeading(text)
            || ThesisTextHeuristics.IsTocHeading(text)
            || ThesisTextHeuristics.IsReferencesHeading(text)
            || ThesisTextHeuristics.IsAcknowledgementsHeading(text)
            || ThesisTextHeuristics.IsAppendixHeading(text);
    }

    private static bool IsFrontMatterOrBodyHeading(string text)
    {
        return ThesisTextHeuristics.IsSpecialSemanticHeading(text)
            || ThesisTextHeuristics.IsChineseKeywords(text)
            || ThesisTextHeuristics.IsEnglishKeywords(text);
    }

    private static bool IsNonTitleFrontMatter(string text)
    {
        return IsFrontMatterOrBodyHeading(text)
            || ThesisTextHeuristics.IsLikelyTocLine(text)
            || ThesisTextHeuristics.IsTableCaption(text)
            || ThesisTextHeuristics.IsFigureCaption(text);
    }

    private static bool LooksLikeCoverLabel(string text)
    {
        return Regex.IsMatch(
            text.Trim(),
            @"^(?:学\s*院|专\s*业|学生姓名|班级|学号|指导老师|指导教师|督导老师|起止时间|作者签名|年\s*月\s*日)\s*[:：]",
            RegexOptions.CultureInvariant)
            || text.Contains("签名", StringComparison.Ordinal)
            || Regex.IsMatch(text.Trim(), @"^年\s*月\s*日", RegexOptions.CultureInvariant);
    }
}
