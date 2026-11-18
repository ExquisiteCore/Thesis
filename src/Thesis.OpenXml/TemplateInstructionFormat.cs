using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

/// <summary>Applies explicit format notes when a template's visible samples use manual spacing or rounded font sizes.</summary>
internal static class TemplateInstructionFormat
{
    public static void Apply(MainDocumentPart main, string styleId, StyleParagraphProperties paragraph, StyleRunProperties run)
    {
        var label = styleId.EndsWith("_heading1", StringComparison.Ordinal) ? "一级标题排版"
            : styleId.EndsWith("_heading2", StringComparison.Ordinal) ? "二级标题排版"
            : styleId.EndsWith("_heading3", StringComparison.Ordinal) ? "三级标题排版"
            : styleId.EndsWith("_body", StringComparison.Ordinal) ? "正文" : null;
        if (label is null) return;
        var instructions = main.Document!.Descendants<TextBoxContent>().SelectMany(box =>
                Regex.Matches(box.InnerText, Regex.Escape(label) + @"[：:]([^。\r\n]+)").Select(match => match.Groups[1].Value))
            .Select(value => Regex.Replace(value, @"\s+", "")).Distinct(StringComparer.Ordinal).ToArray();
        // Conflicting notes need a human choice. Do not guess between different school rules.
        if (instructions.Length != 1) return;
        var text = instructions[0];
        var font = Regex.Match(text, @"^(宋体|黑体|楷体|仿宋|Times New Roman)");
        if (font.Success)
        {
            var fonts = run.GetFirstChild<RunFonts>();
            if (fonts is null) { fonts = new RunFonts(); run.AddChild(fonts, true); }
            fonts.EastAsia = font.Value;
        }
        var size = Regex.Match(text, @"(?<![大小])(?:小[一二三四五六]|[一二三四五六])号");
        var halfPoints = size.Value switch { "一号" => 52, "小一号" => 48, "二号" => 44, "小二号" => 36,
            "三号" => 32, "小三号" => 30, "四号" => 28, "小四号" => 24, "五号" => 21, "小五号" => 18,
            "六号" => 15, "小六号" => 13, _ => 0 };
        if (halfPoints > 0)
        {
            run.RemoveAllChildren<FontSize>(); run.RemoveAllChildren<FontSizeComplexScript>();
            run.AddChild(new FontSize { Val = halfPoints.ToString(CultureInfo.InvariantCulture) }, true);
            run.AddChild(new FontSizeComplexScript { Val = halfPoints.ToString(CultureInfo.InvariantCulture) }, true);
        }
        var alignment = text.Contains("居中", StringComparison.Ordinal) ? JustificationValues.Center
            : text.Contains("居左", StringComparison.Ordinal) || text.Contains("左对齐", StringComparison.Ordinal) ? JustificationValues.Left
            : (JustificationValues?)null;
        if (alignment.HasValue)
        {
            paragraph.RemoveAllChildren<Justification>();
            paragraph.AddChild(new Justification { Val = alignment.Value }, true);
            // Sample documents sometimes center a fixed title by inserting a large left indent.
            var indent = paragraph.GetFirstChild<Indentation>();
            if (indent is not null) { indent.Left = null; indent.LeftChars = null; indent.Right = null; indent.RightChars = null; }
            if (label != "正文") paragraph.RemoveAllChildren<Indentation>();
        }
        if (text.Contains("加粗", StringComparison.Ordinal))
        {
            run.RemoveAllChildren<Bold>(); run.AddChild(new Bold(), true);
        }
        var spacing = paragraph.GetFirstChild<SpacingBetweenLines>();
        if (spacing is null) { spacing = new SpacingBetweenLines(); paragraph.AddChild(spacing, true); }
        var line = text.Contains("单倍行距", StringComparison.Ordinal) ? 240
            : text.Contains("1.5倍行距", StringComparison.Ordinal) ? 360 : 0;
        if (line > 0) { spacing.Line = line.ToString(CultureInfo.InvariantCulture); spacing.LineRule = LineSpacingRuleValues.Auto; }
        var both = Regex.Match(text, @"段前段后各(?<lines>\d+(?:\.\d+)?)行");
        if (both.Success) { spacing.Before = null; spacing.After = null; spacing.BeforeLines = Lines(both); spacing.AfterLines = Lines(both); }
        var before = Regex.Match(text, @"段前(?<lines>\d+(?:\.\d+)?)行");
        if (before.Success) { spacing.Before = null; spacing.BeforeLines = Lines(before); }
        var after = Regex.Match(text, @"段后(?<lines>\d+(?:\.\d+)?)行");
        if (after.Success) { spacing.After = null; spacing.AfterLines = Lines(after); }
    }

    private static int Lines(Match match)
        => (int)Math.Round(double.Parse(match.Groups["lines"].Value, CultureInfo.InvariantCulture) * 100);
}
