using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

/// <summary>Suggest mappings from semantic zones and actual template samples, never from style IDs alone.</summary>
public static class TemplatePlanDraftBuilder
{
    public static IReadOnlyList<(string Title, int Level)> ExtractOutline(string templatePath)
    {
        using var template = WordprocessingDocument.Open(templatePath, false);
        return Describe(template.MainDocumentPart!)
            .Where(item => item.Zone == "body" && !item.IsToc && (item.Role is "heading1" or "heading2" or "heading3"))
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .Where(item => !TemplateSampleText.IsInstruction(item.Text))
            .Select(item => (item.Text.Trim(), item.Role[^1] - '0'))
            .ToArray();
    }

    public static TemplateApplicationPlan Build(string sourcePath, string templatePath)
    {
        using var source = WordprocessingDocument.Open(sourcePath, false);
        using var template = WordprocessingDocument.Open(templatePath, false);
        var from = Describe(source.MainDocumentPart!);
        var to = Describe(template.MainDocumentPart!);
        var plan = new TemplateApplicationPlan { RequireSourceCoverage = true };
        var chapterPageBreaks = template.MainDocumentPart!.Document!.Descendants<TextBoxContent>()
            .Any(box => Regex.IsMatch(box.InnerText, @"每章标题.{0,12}另起一页"));
        var samples = to.Where(d => d.Role != "cover" && (d.Text.Length > 0 || d.Role == "image") && d.IsParagraph && !d.IsToc)
            .Where(d => !TemplateSampleText.IsInstruction(d.Text))
            .GroupBy(d => d.Role).ToDictionary(g => g.Key, SelectSample);
        if (!samples.TryGetValue("body", out var body)) throw new InvalidDataException("Template needs a recognizable body paragraph; inspect its structure before drafting a plan.");
        foreach (var role in from.Select(d => d.Role).Concat(["body", "table", "image", "toc1", "toc2", "toc3"]).Distinct())
        {
            var sample = samples.GetValueOrDefault(role, body);
            int? level = role is "heading1" or "heading2" or "heading3" ? role[^1] - '1'
                : role is "referencesHeading" or "backMatterHeading" or "conclusionHeading" ? 0
                : role is "abstractHeading" or "abstractEnglishHeading" && to.Where(d => d.IsToc).Any(d => Regex.Replace(d.Text, @"\s+", "").Contains(Regex.Replace(sample.Text, @"\s+", ""), StringComparison.OrdinalIgnoreCase)) ? 0 : null;
            plan.Roles[role] = new() { TemplateParagraphIndex = sample.Index, ApplyTemplateInstructions = true,
                OutlineLevel = level, PreserveEmphasis = level is null, PageBreakBefore = role == "heading1" && chapterPageBreaks };
            if (!samples.ContainsKey(role) && role is not ("cover" or "table" or "image"))
                plan.PendingReviews.Add($"Confirm template format for role '{role}': body sample {body.Index} is a fallback.");
        }
        var sourceStart = from.FirstOrDefault(d => d.Role is "abstractHeading" or "abstractEnglishHeading" or "tocHeading" or "heading1")?.Index ?? 0;
        var templateStart = to.FirstOrDefault(d => d.Role is "abstractHeading" or "abstractEnglishHeading" or "tocHeading" or "heading1")?.Index ?? 0;
        string? coverTitle = null;
        if (sourceStart > 0 && templateStart > 0)
        {
            AddFrontMatter(plan, to, templateStart);
            var sourceTitle = source.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
                .FirstOrDefault(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Title");
            coverTitle = to.Take(templateStart).Select(item => Regex.Match(item.Text, @"^论文题目[：:]\s*(?<title>\S.*)$"))
                .FirstOrDefault(match => match.Success)?.Groups["title"].Value;
            if (sourceTitle is not null && !string.IsNullOrWhiteSpace(coverTitle))
                plan.TemplateTextReplacements[coverTitle] = TemplateSampleText.FlowText(sourceTitle);
            plan.SkippedSourceRanges.Add(new() { Start = 0, End = sourceStart, Reason = "Source cover replaced by template front matter; review identity fields." });
            plan.PendingReviews.Add("Replace the template cover's title, author, student ID, department, supervisor and dates; confirm declaration text.");
        }
        else sourceStart = 0;
        if (sourceStart > 0 && templateStart > 0 && to.Any(d => d.Index < templateStart && d.IsToc))
        {
            plan.Blocks.Clear();
            var beforeToc = to.First(d => d.Index < templateStart && d.IsToc).Index;
            if (beforeToc > 0) AddFrontMatter(plan, to, beforeToc);
            plan.Blocks.Add(new() { Type = "toc" });
        }
        var templateSections = template.MainDocumentPart!.Document!.Body!.Descendants<SectionProperties>().ToArray();
        if (templateSections.Length == 0) throw new InvalidDataException("Template has no section layout.");
        plan.TemplateTableIndex = to.FirstOrDefault(d => d.Index >= templateStart && d.Role == "table")?.Index;
        var zones = to.Where(d => d.Role != "cover").GroupBy(d => d.Zone).ToDictionary(g => g.Key, g => g.First().Section);
        var currentSection = to.Where(d => d.Index < templateStart).LastOrDefault()?.Section ?? 0;
        var chapterSamples = to.Where(d => d.Role == "heading1" && d.Zone == "body").ToArray();
        var chapter = -1;
        var usedSections = plan.Blocks.Where(b => b.Type == "section").Select(b => b.SectionTemplateIndex).ToHashSet();
        var previousZone = "cover";
        foreach (var descriptor in from.Where(d => d.Index >= sourceStart))
        {
            if (descriptor.IsSection) continue;
            if (descriptor.Role == "heading1" && descriptor.Zone == "body") chapter++;
            var chapterSample = chapter >= 0 && chapterSamples.Length > 0 ? chapterSamples[Math.Min(chapter, chapterSamples.Length - 1)] : null;
            var section = descriptor.Zone == "body" && chapterSample is not null ? chapterSample.Section : zones.GetValueOrDefault(descriptor.Zone, currentSection);
            if (section != currentSection && plan.Blocks.Count > 0)
            {
                plan.Blocks.Add(new() { Type = "section", SectionTemplateIndex = currentSection, ContinuePageNumbering = !usedSections.Add(currentSection) });
                currentSection = section;
            }
            if (descriptor.IsToc)
            {
                plan.Blocks.Add(new() { Type = "toc" });
                plan.SkippedSourceRanges.Add(new() { Start = descriptor.Index, End = descriptor.Index + 1, Reason = "Regenerate source table of contents from migrated headings." });
                continue;
            }
            currentSection = section;
            var sample = descriptor.Role == "heading1" ? chapterSample : samples.GetValueOrDefault(descriptor.Role);
            var sharesFrontMatterPage = descriptor.Role == "abstractHeading" && descriptor.Index == sourceStart && coverTitle is not null
                && to.Take(templateStart).Any(item => item.Section == section
                    && Regex.Replace(item.Text, @"\s+", "") == Regex.Replace(coverTitle, @"\s+", ""));
            var startNewPage = descriptor.Role is "abstractHeading" or "abstractEnglishHeading" or "englishTitle" && descriptor.Zone != previousZone && !sharesFrontMatterPage
                || descriptor.Role == "tocHeading"
                || descriptor.Role == "heading1" && (chapterSample?.PageBoundary == true || chapterPageBreaks)
                || descriptor.Zone != previousZone && sample?.PageBoundary == true;
            plan.Blocks.Add(new() { Type = "sourceRange", Start = descriptor.Index, End = descriptor.Index + 1, Role = descriptor.Role, PageBreakBefore = startNewPage });
            if (descriptor.Text.Length > 0) previousZone = descriptor.Zone;
        }
        plan.Blocks.Add(new() { Type = "section", SectionTemplateIndex = Math.Min(currentSection, templateSections.Length - 1), FinalSection = true, ContinuePageNumbering = !usedSections.Add(currentSection) });
        plan.PendingReviews.Add("Review all zone boundaries, template sample indexes, section transitions, numbering and school requirements before binding.");
        return plan;
    }

    private static void AddFrontMatter(TemplateApplicationPlan plan, List<Descriptor> template, int end)
    {
        int? previousSection = null;
        foreach (var group in template.Take(end).GroupBy(item => item.Section))
        {
            var retained = group.Where(item => !TemplateSampleText.IsInstruction(item.Text)).Select(item => item.Index).ToArray();
            if (retained.Length == 0) continue;
            if (previousSection is int section) plan.Blocks.Add(new() { Type = "section", SectionTemplateIndex = section });
            for (var i = 0; i < retained.Length;)
            {
                var start = retained[i++];
                var next = start + 1;
                while (i < retained.Length && retained[i] == next) { i++; next++; }
                plan.Blocks.Add(new() { Type = "templateRange", Start = start, End = next });
            }
            previousSection = group.Key;
        }
    }

    private static Descriptor SelectSample(IGrouping<string, Descriptor> candidates)
    {
        // Repeated code snippets and short example labels are not representative thesis prose.
        if (candidates.Key == "body")
        {
            var prose = candidates.FirstOrDefault(item => item.Text.Length >= 40 && Regex.IsMatch(item.Text, "[。！？]")
                && !Regex.IsMatch(item.Text, @"[{};]|^(SELECT|INSERT|CREATE|function|public)\b", RegexOptions.IgnoreCase));
            if (prose is not null) return prose;
        }
        return candidates.GroupBy(item => item.FormatKey).OrderByDescending(group => group.Count()).First()
            .OrderByDescending(item => item.Text.Length).First();
    }

    public static object Analyze(string templatePath)
    {
        using var doc = WordprocessingDocument.Open(templatePath, false);
        var descriptions = Describe(doc.MainDocumentPart!);
        return new { Fingerprint = TemplatePlanPreflight.Fingerprint(templatePath), Blocks = descriptions,
            Sections = doc.MainDocumentPart!.Document!.Body!.Descendants<SectionProperties>().Select((s, i) => new { Index = i, Xml = s.OuterXml }).ToList() };
    }

    private static List<Descriptor> Describe(MainDocumentPart main)
    {
        var blocks = main.Document!.Body!.ChildElements;
        var sections = new int[blocks.Count];
        var pending = 0;
        var section = 0;
        for (var i = 0; i < blocks.Count; i++)
            if (blocks[i] is SectionProperties || blocks[i].Descendants<SectionProperties>().Any())
            {
                for (var j = pending; j <= i; j++) sections[j] = section;
                pending = i + 1;
                section++;
            }
        var result = new List<Descriptor>();
        var zone = "cover";
        var pageBoundary = false;
        var previousSection = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var text = TemplateSampleText.FlowText(block);
            var compact = Regex.Replace(text, @"\s+", "");
            var toc = block.Descendants<FieldCode>().Any(f => Regex.IsMatch(f.Text, @"^\s*TOC\b", RegexOptions.IgnoreCase))
                || block.Descendants<SimpleField>().Any(f => Regex.IsMatch(f.Instruction?.Value ?? "", @"^\s*TOC\b", RegexOptions.IgnoreCase))
                || block is SdtBlock sdt && sdt.SdtProperties?.Descendants<DocPartGallery>().Any(g => g.Val?.Value == "Table of Contents") == true;
            var role = zone;
            var p = block as Paragraph;
            var level = p is null ? null : NumberedOutline(main, p) ?? Outline(main, p);
            pageBoundary |= sections[i] != previousSection || p?.ParagraphProperties?.PageBreakBefore?.Val?.Value == true
                || p?.ParagraphProperties?.PageBreakBefore is { Val: null };
            if (toc) { role = "toc"; zone = "toc"; }
            else if (Regex.IsMatch(compact, @"^(摘要|Abstract)$", RegexOptions.IgnoreCase)) { role = compact == "摘要" ? "abstractHeading" : "abstractEnglishHeading"; zone = compact == "摘要" ? "abstract" : "abstractEnglish"; }
            else if (Regex.IsMatch(compact, @"^(目录|Contents|TableofContents)$", RegexOptions.IgnoreCase)) { role = "tocHeading"; zone = "toc"; }
            else if (Regex.IsMatch(text, @"^Title(?:\s|[:：]|(?=[A-Z]))")) { role = "englishTitle"; zone = "abstractEnglish"; }
            else if (Regex.IsMatch(text, @"^(关键词|关键字|key\s*words?)\s*[:：]", RegexOptions.IgnoreCase)) role = "keywords";
            else if (Regex.IsMatch(compact, @"^(参考文献|References)$", RegexOptions.IgnoreCase)) { role = "referencesHeading"; zone = "references"; }
            else if (Regex.IsMatch(compact, @"^(致谢|Acknowledg(e)?ments?|附录.*|Appendix.*)$", RegexOptions.IgnoreCase)) { role = "backMatterHeading"; zone = "backMatter"; }
            else if (zone != "cover" && Regex.IsMatch(compact, @"(原创承诺书|原创性声明|诚信声明|授权书)$")) { role = "backMatterHeading"; zone = "backMatter"; }
            else if (Regex.IsMatch(compact, @"^(结论|结论与展望|总结|结语|结束语)$") && level is not (1 or 2)) { role = "conclusionHeading"; zone = "conclusion"; }
            else if (text.Length < 80 && !Regex.IsMatch(text, "[，。；]") && Regex.IsMatch(compact, @"^(第[一二三四五六七八九十百\d]+[章节]|Chapter\d+)", RegexOptions.IgnoreCase)) { role = "heading1"; zone = "body"; }
            else if (text.Length < 80 && !Regex.IsMatch(text, "[，。；]") && Regex.IsMatch(text, @"^\d{1,2}\s+\S")) { role = "heading1"; zone = "body"; }
            else if (Regex.IsMatch(text, @"^\d+\.\d+\.\d+\s*\S")) { role = "heading3"; zone = "body"; }
            else if (Regex.IsMatch(text, @"^\d+\.\d+\s*\S")) { role = "heading2"; zone = "body"; }
            else if (zone != "cover" && level is >= 0 and <= 2 && !toc && text.Length is > 0 and < 100 && !Regex.IsMatch(text, "[，。；]")) { role = "heading" + (level + 1); zone = "body"; }
            else if (Regex.IsMatch(text, @"^(图|表|Figure|Table)\s*\d+[.\-－—．]\d+", RegexOptions.IgnoreCase)) role = "caption";
            else if (block is Table) role = "table";
            else if (block.Descendants<Drawing>().Any() && string.IsNullOrWhiteSpace(text)) role = "image";
            var properties = p?.ParagraphProperties?.CloneNode(true);
            if (properties is not null)
                foreach (var value in properties.ChildElements.Where(e => e is NumberingProperties or SectionProperties).ToArray()) value.Remove();
            var run = p is null ? null : TemplateSampleText.DominantRun(p);
            result.Add(new(i, role, zone, text, sections[i], p is not null, block is SectionProperties, toc, level, pageBoundary, (properties?.OuterXml ?? "") + (run?.RunProperties?.OuterXml ?? "")));
            if (text.Length > 0) pageBoundary = false;
            pageBoundary |= block.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page);
            previousSection = sections[i];
        }
        return result;
    }

    private static int? Outline(MainDocumentPart main, Paragraph paragraph)
    {
        if (paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value is int direct) return direct;
        var id = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var seen = new HashSet<string>();
        while (id is not null && seen.Add(id))
        {
            var style = main.StyleDefinitionsPart?.Styles?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == id);
            if (style?.StyleParagraphProperties?.OutlineLevel?.Val?.Value is int level) return level;
            id = style?.BasedOn?.Val?.Value;
        }
        return null;
    }

    private static int? NumberedOutline(MainDocumentPart main, Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties?.NumberingProperties;
        var id = properties?.NumberingId?.Val?.Value;
        if (id is null or 0) return null;
        var definitions = main.NumberingDefinitionsPart?.Numbering;
        var number = definitions?.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == id);
        var levelIndex = properties?.NumberingLevelReference?.Val?.Value ?? 0;
        var level = number?.Elements<LevelOverride>().FirstOrDefault(l => l.LevelIndex?.Value == levelIndex)?.GetFirstChild<Level>()
            ?? definitions?.Elements<AbstractNum>().FirstOrDefault(n => n.AbstractNumberId?.Value == number?.AbstractNumId?.Val?.Value)?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == levelIndex);
        var text = level?.LevelText?.Val?.Value ?? "";
        if (Regex.IsMatch(text, @"第%\d+[章节]")) return 0;
        if (Regex.IsMatch(text, @"^(?:%\d+|\d+)(?:\.(?:%\d+|\d+)){1,2}\.?$")) return Math.Min(2, text.TrimEnd('.').Count(c => c == '.'));
        return null;
    }

    private sealed record Descriptor(int Index, string Role, string Zone, string Text, int Section, bool IsParagraph, bool IsSection, bool IsToc,
        int? OutlineLevel, bool PageBoundary, string FormatKey);
}
