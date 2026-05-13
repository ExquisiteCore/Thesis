using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static class ThesisDocumentGenerator
{
    public static void Generate(ThesisContent content, TemplateProfile rules, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        AppendParagraph(body, RequiredTitle(content), ResolveParagraphFormat(rules, "title"), "Title");
        AppendOptionalParagraph(body, content.Author, ResolveParagraphFormat(rules, "body"), "Normal");
        AppendAbstracts(body, content, rules);
        AppendTableOfContents(body, rules);
        AppendChapters(body, content.Chapters, rules);
        AppendReferences(body, content.References, rules);
        AppendAcknowledgements(body, content.Acknowledgements, rules);
        MarkDocumentFieldsDirty(mainPart);
        body.AppendChild(CreateSectionProperties(rules.PageSetup));
        mainPart.Document.Save();
    }

    private static void AppendAbstracts(Body body, ThesisContent content, TemplateProfile rules)
    {
        if (!string.IsNullOrWhiteSpace(content.AbstractZh) || content.KeywordsZh.Count > 0)
        {
            AppendParagraph(body, "摘要", ResolveParagraphFormat(rules, "abstract.zh", "heading1"), "Heading1");
            AppendOptionalParagraph(body, content.AbstractZh, ResolveParagraphFormat(rules, "body"), "Normal");
            AppendKeywords(body, "关键词：", content.KeywordsZh, ResolveParagraphFormat(rules, "keywords.zh", "body"));
        }

        if (!string.IsNullOrWhiteSpace(content.AbstractEn) || content.KeywordsEn.Count > 0)
        {
            AppendParagraph(body, "Abstract", ResolveParagraphFormat(rules, "abstract.en", "heading1"), "Heading1");
            AppendOptionalParagraph(body, content.AbstractEn, ResolveParagraphFormat(rules, "body"), "Normal");
            AppendKeywords(body, "Keywords: ", content.KeywordsEn, ResolveParagraphFormat(rules, "keywords.en", "body"));
        }
    }

    private static void AppendKeywords(Body body, string prefix, List<string> keywords, ParagraphFormatSample? format)
    {
        if (keywords.Count == 0)
        {
            return;
        }

        var separator = prefix.EndsWith('：') ? "；" : "; ";
        AppendParagraph(body, prefix + string.Join(separator, keywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword))), format, "Normal");
    }

    private static void AppendChapters(Body body, List<ThesisChapterContent> chapters, TemplateProfile rules)
    {
        for (var chapterIndex = 0; chapterIndex < chapters.Count; chapterIndex++)
        {
            var chapter = chapters[chapterIndex];
            var chapterNumber = chapterIndex + 1;
            AppendParagraph(body, FormatChapterTitle(chapter.Title, chapterNumber), ResolveParagraphFormat(rules, "heading1"), "Heading1");
            AppendBodyParagraphs(body, chapter.Paragraphs, rules);
            AppendTables(body, chapter.Tables, rules);

            for (var sectionIndex = 0; sectionIndex < chapter.Sections.Count; sectionIndex++)
            {
                var section = chapter.Sections[sectionIndex];
                AppendParagraph(body, FormatSectionTitle(section.Title, chapterNumber, sectionIndex + 1), ResolveParagraphFormat(rules, "heading2", "heading1"), "Heading2");
                AppendBodyParagraphs(body, section.Paragraphs, rules);
                AppendTables(body, section.Tables, rules);
            }
        }
    }

    private static void AppendBodyParagraphs(Body body, List<string> paragraphs, TemplateProfile rules)
    {
        var format = ResolveParagraphFormat(rules, "body");
        foreach (var paragraph in paragraphs.Where(paragraph => !string.IsNullOrWhiteSpace(paragraph)))
        {
            AppendParagraph(body, paragraph, format, "Normal");
        }
    }

    private static void AppendTables(Body body, List<ThesisTableContent> tables, TemplateProfile rules)
    {
        foreach (var table in tables)
        {
            AppendOptionalParagraph(body, table.Caption, ResolveParagraphFormat(rules, "tableCaption", "body"), "Normal");
            body.AppendChild(CreateTable(table, ResolveTableFormat(rules)));
        }
    }

    private static void AppendReferences(Body body, List<string> references, TemplateProfile rules)
    {
        if (references.Count == 0)
        {
            return;
        }

        AppendParagraph(body, "参考文献", ResolveParagraphFormat(rules, "references", "heading1"), "Heading1");
        var format = ResolveParagraphFormat(rules, "referenceItem", "body");
        for (var index = 0; index < references.Count; index++)
        {
            AppendParagraph(body, $"[{index + 1}] {StripReferenceNumber(references[index])}", format, "Normal");
        }
    }

    private static void AppendTableOfContents(Body body, TemplateProfile rules)
    {
        AppendParagraph(body, "目录", ResolveTableOfContentsTitleFormat(rules), "Normal");
        body.AppendChild(CreateTocParagraph("1-3"));
    }

    private static void AppendAcknowledgements(Body body, string? acknowledgements, TemplateProfile rules)
    {
        if (string.IsNullOrWhiteSpace(acknowledgements))
        {
            return;
        }

        AppendParagraph(body, "致谢", ResolveParagraphFormat(rules, "acknowledgements", "heading1"), "Heading1");
        AppendParagraph(body, acknowledgements, ResolveParagraphFormat(rules, "body"), "Normal");
    }

    private static void AppendOptionalParagraph(Body body, string? text, ParagraphFormatSample? format, string fallbackStyleId)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            AppendParagraph(body, text, format, fallbackStyleId);
        }
    }

    private static Paragraph AppendParagraph(Body body, string text, ParagraphFormatSample? format, string fallbackStyleId)
    {
        var paragraph = new Paragraph();
        paragraph.AppendChild(new Run(new Text(text)
        {
            Space = NeedsPreservedSpace(text) ? SpaceProcessingModeValues.Preserve : null
        }));

        var effectiveFormat = format is null
            ? new ParagraphFormatSample { StyleId = fallbackStyleId }
            : OpenXmlFormatMerger.Clone(format);
        effectiveFormat.StyleId ??= fallbackStyleId;
        OpenXmlFormatApplier.ApplyParagraphFormat(paragraph, effectiveFormat);
        body.AppendChild(paragraph);
        return paragraph;
    }

    private static Table CreateTable(ThesisTableContent content, TableFormatSample? format)
    {
        var table = new Table();
        if (content.Headers.Count > 0)
        {
            table.AppendChild(CreateTableRow(content.Headers));
        }

        foreach (var row in content.Rows)
        {
            table.AppendChild(CreateTableRow(row));
        }

        if (!table.Elements<TableRow>().Any())
        {
            table.AppendChild(CreateTableRow([""]));
        }

        if (format is not null)
        {
            OpenXmlFormatApplier.ApplyTableFormat(table, OpenXmlFormatMerger.Clone(format));
        }
        else
        {
            OpenXmlFormatApplier.EnsureTableGrid(table);
        }

        return table;
    }

    private static TableRow CreateTableRow(IEnumerable<string> cells)
    {
        var row = new TableRow();
        foreach (var cellText in cells)
        {
            row.AppendChild(new TableCell(new Paragraph(new Run(new Text(cellText ?? "")))));
        }

        return row;
    }

    private static SectionProperties CreateSectionProperties(ProfilePageSetup? pageSetup)
    {
        var section = new SectionProperties();
        if (pageSetup?.PageSize is not null)
        {
            var pageSize = new PageSize();
            if (pageSetup.PageSize.WidthTwips is not null)
            {
                pageSize.Width = (UInt32Value)(uint)pageSetup.PageSize.WidthTwips.Value;
            }

            if (pageSetup.PageSize.HeightTwips is not null)
            {
                pageSize.Height = (UInt32Value)(uint)pageSetup.PageSize.HeightTwips.Value;
            }

            if (string.Equals(pageSetup.PageSize.Orientation, "landscape", StringComparison.OrdinalIgnoreCase))
            {
                pageSize.Orient = PageOrientationValues.Landscape;
            }
            else if (string.Equals(pageSetup.PageSize.Orientation, "portrait", StringComparison.OrdinalIgnoreCase))
            {
                pageSize.Orient = PageOrientationValues.Portrait;
            }

            section.AppendChild(pageSize);
        }

        if (pageSetup?.Margins is not null)
        {
            var margins = new PageMargin();
            if (pageSetup.Margins.TopTwips is not null)
            {
                margins.Top = pageSetup.Margins.TopTwips.Value;
            }

            if (pageSetup.Margins.RightTwips is not null)
            {
                margins.Right = (UInt32Value)(uint)pageSetup.Margins.RightTwips.Value;
            }

            if (pageSetup.Margins.BottomTwips is not null)
            {
                margins.Bottom = pageSetup.Margins.BottomTwips.Value;
            }

            if (pageSetup.Margins.LeftTwips is not null)
            {
                margins.Left = (UInt32Value)(uint)pageSetup.Margins.LeftTwips.Value;
            }

            if (pageSetup.Margins.HeaderTwips is not null)
            {
                margins.Header = (UInt32Value)(uint)pageSetup.Margins.HeaderTwips.Value;
            }

            if (pageSetup.Margins.FooterTwips is not null)
            {
                margins.Footer = (UInt32Value)(uint)pageSetup.Margins.FooterTwips.Value;
            }

            if (pageSetup.Margins.GutterTwips is not null)
            {
                margins.Gutter = (UInt32Value)(uint)pageSetup.Margins.GutterTwips.Value;
            }

            section.AppendChild(margins);
        }

        return section;
    }

    private static Paragraph CreateTocParagraph(string levels)
    {
        return new Paragraph(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin, Dirty = true }),
            new Run(new FieldCode($" TOC \\o \"{levels}\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("目录待更新")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
    }

    private static void MarkDocumentFieldsDirty(MainDocumentPart mainPart)
    {
        var settingsPart = mainPart.DocumentSettingsPart ?? mainPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings ??= new Settings();
        settingsPart.Settings.RemoveAllChildren<UpdateFieldsOnOpen>();
        settingsPart.Settings.AppendChild(new UpdateFieldsOnOpen { Val = true });
        settingsPart.Settings.Save();
    }

    private static ParagraphFormatSample? ResolveParagraphFormat(TemplateProfile rules, string role, string? fallbackRole = null)
    {
        var roleFormat = rules.StyleRoles
            .Where(candidate => RoleMatches(candidate.Role, role))
            .OrderByDescending(candidate => candidate.Confidence)
            .Select(candidate => candidate.Format)
            .FirstOrDefault(candidate => candidate is not null);
        if (roleFormat is not null)
        {
            return roleFormat;
        }

        var policyFormat = rules.RolePolicies
            .Where(candidate => RoleMatches(candidate.Role, role))
            .OrderByDescending(candidate => candidate.Priority)
            .Select(candidate => candidate.Format)
            .FirstOrDefault(candidate => candidate is not null);
        if (policyFormat is not null)
        {
            return policyFormat;
        }

        return fallbackRole is null ? null : ResolveParagraphFormat(rules, fallbackRole);
    }

    private static ParagraphFormatSample? ResolveTableOfContentsTitleFormat(TemplateProfile rules)
    {
        var format = ResolveParagraphFormat(rules, "toc.title")
            ?? ResolveParagraphFormat(rules, "heading1");
        if (format is null)
        {
            return new ParagraphFormatSample { Alignment = "center" };
        }

        var clone = OpenXmlFormatMerger.Clone(format);
        clone.StyleId = null;
        return clone;
    }

    private static TableFormatSample? ResolveTableFormat(TemplateProfile rules)
    {
        return rules.TablePolicy.Default?.Format
            ?? rules.TableArchetypes
                .OrderByDescending(candidate => candidate.Confidence)
                .Select(candidate => candidate.Format)
                .FirstOrDefault(candidate => candidate is not null);
    }

    private static bool RoleMatches(string? candidate, string role)
    {
        return string.Equals(candidate, role, StringComparison.OrdinalIgnoreCase);
    }

    private static string RequiredTitle(ThesisContent content)
    {
        return string.IsNullOrWhiteSpace(content.Title) ? "论文题目" : content.Title;
    }

    private static string ToChineseOrdinal(int value)
    {
        return value switch
        {
            1 => "一",
            2 => "二",
            3 => "三",
            4 => "四",
            5 => "五",
            6 => "六",
            7 => "七",
            8 => "八",
            9 => "九",
            10 => "十",
            _ => value.ToString()
        };
    }

    private static string FormatChapterTitle(string title, int chapterNumber)
    {
        var trimmed = title.Trim();
        var spacedMatch = Regex.Match(
            trimmed,
            @"^第[一二三四五六七八九十百千万零〇两0-9Xx]+章\s+\S.*$",
            RegexOptions.CultureInvariant);
        if (spacedMatch.Success)
        {
            return trimmed;
        }

        var compactMatch = Regex.Match(
            trimmed,
            @"^(?<prefix>第[一二三四五六七八九十百千万零〇两0-9Xx]+章)(?<title>\S.*)$",
            RegexOptions.CultureInvariant);
        if (compactMatch.Success)
        {
            return $"{compactMatch.Groups["prefix"].Value} {compactMatch.Groups["title"].Value}";
        }

        return $"第{ToChineseOrdinal(chapterNumber)}章 {trimmed}";
    }

    private static string FormatSectionTitle(string title, int chapterNumber, int sectionNumber)
    {
        var trimmed = title.Trim();
        return Regex.IsMatch(trimmed, @"^\d{1,2}[\.．]\d{1,2}(?:[\.．]\d{1,2})?\s+\S+", RegexOptions.CultureInvariant)
            ? trimmed
            : $"{chapterNumber}.{sectionNumber} {trimmed}";
    }

    private static string StripReferenceNumber(string text)
    {
        return Regex.Replace(text.Trim(), @"^\s*\[\d+\]\s*", "", RegexOptions.CultureInvariant);
    }

    private static bool NeedsPreservedSpace(string text)
    {
        return text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
    }
}
