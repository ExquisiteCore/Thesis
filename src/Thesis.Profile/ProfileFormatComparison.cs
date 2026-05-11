using Thesis.Schema;

namespace Thesis.Profile;

internal static class ProfileFormatComparison
{
    public static ParagraphFormatSample NormalizePolicyFormat(ParagraphFormatSample format)
    {
        var clone = ProfileSampleCloner.Clone(format) ?? new ParagraphFormatSample();
        clone.StyleId = null;
        if (clone.RunFormat is not null && clone.RunFormat.Bold is null)
        {
            clone.RunFormat.Bold = false;
        }

        return clone;
    }

    public static bool IsSamePolicyFormat(ParagraphFormatSample left, ParagraphFormatSample right)
    {
        return string.Equals(left.StyleId, right.StyleId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Alignment, right.Alignment, StringComparison.OrdinalIgnoreCase)
            && left.SpacingBeforeTwips == right.SpacingBeforeTwips
            && left.SpacingAfterTwips == right.SpacingAfterTwips
            && string.Equals(left.LineSpacing, right.LineSpacing, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.LineSpacingRule, right.LineSpacingRule, StringComparison.OrdinalIgnoreCase)
            && left.FirstLineIndentTwips == right.FirstLineIndentTwips
            && left.LeftIndentTwips == right.LeftIndentTwips
            && left.RightIndentTwips == right.RightIndentTwips
            && IsSameRunFormat(left.RunFormat, right.RunFormat);
    }

    public static bool IsSameRunFormat(RunFormatSample? left, RunFormatSample? right)
    {
        return string.Equals(left?.FontSizeHalfPoints, right?.FontSizeHalfPoints, StringComparison.OrdinalIgnoreCase)
            && left?.Bold == right?.Bold
            && left?.Italic == right?.Italic
            && string.Equals(left?.AsciiFont, right?.AsciiFont, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left?.HighAnsiFont, right?.HighAnsiFont, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left?.EastAsiaFont, right?.EastAsiaFont, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left?.ComplexScriptFont, right?.ComplexScriptFont, StringComparison.OrdinalIgnoreCase);
    }
}
