using System.Text.RegularExpressions;
using Thesis.Schema;

namespace Thesis.Profile;

internal static class ProfileTextHeuristics
{
    public static bool IsChineseAbstractHeading(string text)
    {
        var normalized = NormalizeHeading(text);
        return normalized is "摘要" or "中文摘要";
    }

    public static bool IsEnglishAbstractHeading(string text)
    {
        return string.Equals(NormalizeHeading(text), "abstract", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTocHeading(string text)
    {
        var normalized = NormalizeHeading(text);
        return normalized is "目录" or "目次" or "contents" or "tableofcontents";
    }

    public static bool IsReferencesHeading(string text)
    {
        var normalized = NormalizeHeading(text);
        return normalized is "参考文献" or "references" or "bibliography";
    }

    public static bool IsDirectHeading1(DocumentParagraph paragraph)
    {
        if (IsSpecialSemanticHeading(paragraph.Text) || IsLikelyTocLine(paragraph.Text))
        {
            return false;
        }

        return Regex.IsMatch(paragraph.Text.Trim(), @"^第[一二三四五六七八九十百千万零〇两0-9Xx]+章(?![\d.．、])", RegexOptions.CultureInvariant)
            && string.Equals(paragraph.Format.Alignment, "center", StringComparison.OrdinalIgnoreCase)
            && paragraph.Format.RunFormat?.Bold == true
            && GetFontSize(paragraph) >= 30;
    }

    public static bool IsDirectHeading2(DocumentParagraph paragraph)
    {
        return !IsLikelyTocLine(paragraph.Text)
            && Regex.IsMatch(paragraph.Text.Trim(), @"^\d{1,2}\.\d{1,2}(?!\.)\s+", RegexOptions.CultureInvariant)
            && IsLeftOrDefaultAligned(paragraph)
            && paragraph.Format.RunFormat?.Bold == true
            && GetFontSize(paragraph) is >= 23 and <= 26
            && !HasFirstLineIndent(paragraph);
    }

    public static bool IsDirectHeading3(DocumentParagraph paragraph)
    {
        return !IsLikelyTocLine(paragraph.Text)
            && Regex.IsMatch(paragraph.Text.Trim(), @"^\d{1,2}\.\d{1,2}\.\d{1,2}(?!\.)\s+", RegexOptions.CultureInvariant)
            && IsLeftOrDefaultAligned(paragraph)
            && paragraph.Format.RunFormat?.Bold == true
            && GetFontSize(paragraph) is >= 20 and <= 22
            && !HasFirstLineIndent(paragraph);
    }

    public static bool IsDirectBody(DocumentParagraph paragraph)
    {
        var text = paragraph.Text.Trim();
        if (text.Length < 8
            || IsSpecialSemanticHeading(text)
            || IsLikelyTocLine(text)
            || IsDirectHeading1(paragraph)
            || IsDirectHeading2(paragraph)
            || IsDirectHeading3(paragraph)
            || Regex.IsMatch(text, @"^(?:注：|\d+、|\[序号\])", RegexOptions.CultureInvariant))
        {
            return false;
        }

        return paragraph.Format.RunFormat?.Bold != true
            && GetFontSize(paragraph) is >= 20 and <= 22
            && string.Equals(paragraph.Format.LineSpacing, "360", StringComparison.OrdinalIgnoreCase)
            && string.Equals(paragraph.Format.LineSpacingRule, "atleast", StringComparison.OrdinalIgnoreCase)
            && paragraph.Format.FirstLineIndentTwips is >= 360 and <= 560;
    }

    public static bool IsSpecialSemanticHeading(string text)
    {
        return IsChineseAbstractHeading(text)
            || IsEnglishAbstractHeading(text)
            || IsTocHeading(text)
            || IsReferencesHeading(text);
    }

    public static bool IsLikelyTocLine(string text)
    {
        return text.Contains('\t')
            || text.Contains("……", StringComparison.Ordinal)
            || text.Contains("......", StringComparison.Ordinal)
            || Regex.IsMatch(text, @"\.{3,}|\d\s*$", RegexOptions.CultureInvariant)
                && Regex.IsMatch(text, @"^(?:第[一二三四五六七八九十百千万零〇两0-9Xx]+章|\d{1,2}\.\d{1,2})", RegexOptions.CultureInvariant);
    }

    public static string NormalizeHeading(string text)
    {
        var normalized = text.Trim()
            .Trim(':', '：')
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal)
            .Replace("\u3000", "", StringComparison.Ordinal);

        while (normalized.Length > 0 && (char.IsDigit(normalized[0]) || normalized[0] is '.' or '．' or '、' or ')' or '）'))
        {
            normalized = normalized[1..];
        }

        return normalized.Trim().ToLowerInvariant();
    }

    public static string CreateExactTextPattern(string text)
    {
        return "^" + Regex.Escape(text.Trim()) + "$";
    }

    private static bool IsLeftOrDefaultAligned(DocumentParagraph paragraph)
    {
        return string.IsNullOrWhiteSpace(paragraph.Format.Alignment)
            || string.Equals(paragraph.Format.Alignment, "left", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFirstLineIndent(DocumentParagraph paragraph)
    {
        return paragraph.Format.FirstLineIndentTwips is not null
            && Math.Abs(paragraph.Format.FirstLineIndentTwips.Value) > 0;
    }

    private static int GetFontSize(DocumentParagraph paragraph)
    {
        return int.TryParse(paragraph.Format.RunFormat?.FontSizeHalfPoints, out var result)
            ? result
            : 0;
    }
}
