using Thesis.Schema;

namespace Thesis.Profile;

internal static class ProfileFormatClusterBuilder
{
    public static List<ProfileFormatCluster> Build(DocumentMap map)
    {
        return [.. map.Paragraphs
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text))
            .Where(paragraph => !ProfileTextHeuristics.IsSpecialSemanticHeading(paragraph.Text))
            .GroupBy(FormatKey, StringComparer.Ordinal)
            .Select((group, index) => BuildCluster(group.ToList(), index + 1))
            .Where(cluster => cluster.Count >= 2)
            .OrderByDescending(cluster => cluster.Count)
            .ThenByDescending(cluster => cluster.Confidence)
            .ThenBy(cluster => cluster.Id, StringComparer.Ordinal)];
    }

    private static ProfileFormatCluster BuildCluster(IReadOnlyList<DocumentParagraph> paragraphs, int ordinal)
    {
        var sample = paragraphs[0];
        var roleHint = InferRoleHint(paragraphs);
        var format = ProfileSampleCloner.Clone(sample.Format) ?? new ParagraphFormatSample();

        return new ProfileFormatCluster
        {
            Id = $"paragraph-format-{ordinal}",
            AppliesTo = "paragraph",
            RoleHint = roleHint,
            Count = paragraphs.Count,
            Confidence = CalculateConfidence(roleHint, paragraphs.Count),
            StyleIds = [.. paragraphs
                .Select(paragraph => paragraph.StyleId)
                .Where(styleId => !string.IsNullOrWhiteSpace(styleId))
                .Select(styleId => styleId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(styleId => styleId, StringComparer.OrdinalIgnoreCase)],
            Match = new ProfileRoleMatch
            {
                StyleIds = [],
                Format = BuildFormatMatch(format)
            },
            Format = format,
            Evidence = [.. paragraphs.Take(5).Select(ToParagraphEvidence)]
        };
    }

    private static string InferRoleHint(IReadOnlyList<DocumentParagraph> paragraphs)
    {
        if (paragraphs.Any(ProfileTextHeuristics.IsDirectHeading1))
        {
            return "heading1";
        }

        if (paragraphs.Any(ProfileTextHeuristics.IsDirectHeading2))
        {
            return "heading2";
        }

        if (paragraphs.Any(ProfileTextHeuristics.IsDirectHeading3))
        {
            return "heading3";
        }

        if (paragraphs.Any(ProfileTextHeuristics.IsDirectBody))
        {
            return "body";
        }

        return "unknown";
    }

    private static double CalculateConfidence(string roleHint, int count)
    {
        var baseConfidence = string.Equals(roleHint, "unknown", StringComparison.Ordinal) ? 0.55 : 0.72;
        return Math.Min(0.95, baseConfidence + Math.Min(count, 6) * 0.03);
    }

    private static ProfileRoleFormatMatch BuildFormatMatch(ParagraphFormatSample format)
    {
        return new ProfileRoleFormatMatch
        {
            StyleId = null,
            Alignment = format.Alignment,
            FontSizeHalfPoints = format.RunFormat?.FontSizeHalfPoints,
            Bold = format.RunFormat?.Bold ?? false,
            Italic = format.RunFormat?.Italic,
            LineSpacing = format.LineSpacing,
            LineSpacingRule = format.LineSpacingRule,
            FirstLineIndentTwips = CreateExactMatch(format.FirstLineIndentTwips),
            LeftIndentTwips = CreateExactMatch(format.LeftIndentTwips),
            RightIndentTwips = CreateExactMatch(format.RightIndentTwips)
        };
    }

    private static IntRangeMatch? CreateExactMatch(int? value)
    {
        return value is null ? null : new IntRangeMatch { Exact = value.Value };
    }

    private static ProfileParagraphEvidence ToParagraphEvidence(DocumentParagraph paragraph)
    {
        return new ProfileParagraphEvidence
        {
            ParagraphIndex = paragraph.Index,
            StyleId = paragraph.StyleId,
            TextPreview = Preview(paragraph.Text)
        };
    }

    private static string FormatKey(DocumentParagraph paragraph)
    {
        var format = paragraph.Format;
        var run = format.RunFormat;
        return string.Join("|",
            Lower(format.Alignment),
            format.SpacingBeforeTwips,
            format.SpacingAfterTwips,
            Lower(format.LineSpacing),
            Lower(format.LineSpacingRule),
            format.FirstLineIndentTwips,
            format.LeftIndentTwips,
            format.RightIndentTwips,
            run?.Bold,
            run?.Italic,
            Lower(run?.FontSizeHalfPoints),
            Lower(run?.AsciiFont),
            Lower(run?.HighAnsiFont),
            Lower(run?.EastAsiaFont),
            Lower(run?.ComplexScriptFont));
    }

    private static string Lower(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.ToLowerInvariant();
    }

    private static string Preview(string text)
    {
        return text.Length <= 80 ? text : text[..80];
    }
}
