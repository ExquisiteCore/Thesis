using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal sealed partial class OpenXmlTargetResolver
{
    private static string? GetParagraphStyleId(Paragraph paragraph)
    {
        return paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
    }

    private bool RolePolicyMatches(Paragraph paragraph, ProfileRolePolicy policy)
    {
        var match = policy.Match;
        return StyleMatches(paragraph, match.StyleIds)
            && TextPatternMatches(paragraph, match.TextPatterns)
            && OutlineLevelMatches(paragraph, match.OutlineLevels)
            && FormatMatches(paragraph, match.Format);
    }

    private static bool StyleMatches(Paragraph paragraph, List<string> styleIds)
    {
        if (styleIds.Count == 0)
        {
            return true;
        }

        var paragraphStyleId = GetParagraphStyleId(paragraph);
        return paragraphStyleId is not null
            && styleIds.Any(styleId => string.Equals(styleId, paragraphStyleId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TextPatternMatches(Paragraph paragraph, List<string> textPatterns)
    {
        if (textPatterns.Count == 0)
        {
            return true;
        }

        return textPatterns.Any(pattern => Regex.IsMatch(paragraph.InnerText, pattern, RegexOptions.CultureInvariant));
    }

    private bool OutlineLevelMatches(Paragraph paragraph, List<int> outlineLevels)
    {
        if (outlineLevels.Count == 0)
        {
            return true;
        }

        var outlineLevel = ReadOutlineLevel(paragraph);
        return outlineLevel is not null && outlineLevels.Contains(outlineLevel.Value);
    }

    private int? ReadOutlineLevel(Paragraph paragraph)
    {
        var directOutlineLevel = paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (directOutlineLevel is not null)
        {
            return directOutlineLevel;
        }

        var styleId = GetParagraphStyleId(paragraph);
        return styleId is not null && _styleOutlineLevels.TryGetValue(styleId, out var styleOutlineLevel)
            ? styleOutlineLevel
            : null;
    }

    private static bool FormatMatches(Paragraph paragraph, ProfileRoleFormatMatch? match)
    {
        if (match is null)
        {
            return true;
        }

        var properties = paragraph.ParagraphProperties;
        var spacing = properties?.SpacingBetweenLines;
        var indentation = properties?.Indentation;
        var runFormat = ReadFirstTextRunFormat(paragraph);

        return StringMatches(GetParagraphStyleId(paragraph), match.StyleId)
            && StringMatches(LowerInnerText(properties?.Justification?.Val), match.Alignment)
            && StringMatches(runFormat.FontSizeHalfPoints, match.FontSizeHalfPoints)
            && BoolMatches(runFormat.Bold, match.Bold)
            && BoolMatches(runFormat.Italic, match.Italic)
            && StringMatches(spacing?.Line?.Value, match.LineSpacing)
            && StringMatches(LowerInnerText(spacing?.LineRule), match.LineSpacingRule)
            && RangeMatches(ReadIndentTwips(indentation?.FirstLine), match.FirstLineIndentTwips)
            && RangeMatches(ReadIndentTwips(indentation?.Left), match.LeftIndentTwips)
            && RangeMatches(ReadIndentTwips(indentation?.Right), match.RightIndentTwips);
    }

    private static RunFormatFacts ReadFirstTextRunFormat(Paragraph paragraph)
    {
        var properties = paragraph
            .Descendants<Run>()
            .FirstOrDefault(run => !string.IsNullOrWhiteSpace(run.InnerText))
            ?.RunProperties;

        return new RunFormatFacts(
            ReadOnOffValue(properties?.Bold) == true,
            ReadOnOffValue(properties?.Italic) == true,
            properties?.FontSize?.Val?.Value ?? properties?.FontSizeComplexScript?.Val?.Value);
    }

    private static bool StringMatches(string? actual, string? expected)
    {
        return expected is null
            || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool BoolMatches(bool actual, bool? expected)
    {
        return expected is null || actual == expected.Value;
    }

    private static bool RangeMatches(int? actual, IntRangeMatch? expected)
    {
        if (expected is null)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        if (expected.Exact is not null)
        {
            return actual.Value == expected.Exact.Value;
        }

        if (expected.Min is not null && actual.Value < expected.Min.Value)
        {
            return false;
        }

        return expected.Max is null || actual.Value <= expected.Max.Value;
    }

    private static bool? ReadOnOffValue(OnOffType? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Val?.Value ?? true;
    }

    private static string? LowerInnerText(OpenXmlSimpleType? value)
    {
        return string.IsNullOrWhiteSpace(value?.InnerText)
            ? null
            : value.InnerText.ToLowerInvariant();
    }

    private static int? ToInt(StringValue? value)
    {
        return int.TryParse(value?.Value, out var result) ? result : null;
    }

    private static int ReadIndentTwips(StringValue? value)
    {
        return ToInt(value) ?? 0;
    }

    private readonly record struct RunFormatFacts(bool Bold, bool Italic, string? FontSizeHalfPoints);
}
