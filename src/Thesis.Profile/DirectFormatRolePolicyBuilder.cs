using Thesis.Schema;

namespace Thesis.Profile;

internal static class DirectFormatRolePolicyBuilder
{
    public static void AddDirectFormatRolePolicies(List<ProfileRolePolicy> policies, DocumentMap map)
    {
        AddDirectFormatRolePolicy(
            policies,
            map,
            "heading1",
            105,
            0.76,
            ProfileTextHeuristics.IsDirectHeading1,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))第[一二三四五六七八九十百千万零〇两0-9Xx]+章(?![\d.．、])\s*.*$");
        AddDirectFormatRolePolicy(
            policies,
            map,
            "heading2",
            85,
            0.74,
            ProfileTextHeuristics.IsDirectHeading2,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))\d{1,2}\.\d{1,2}(?!\.)\s+.*$");
        AddDirectFormatRolePolicy(
            policies,
            map,
            "heading3",
            75,
            0.72,
            ProfileTextHeuristics.IsDirectHeading3,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))\d{1,2}\.\d{1,2}\.\d{1,2}(?!\.)\s+.*$");
        AddDirectFormatRolePolicy(
            policies,
            map,
            "body",
            15,
            0.68,
            ProfileTextHeuristics.IsDirectBody,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))(?!\s*(?:第[一二三四五六七八九十百千万零〇两0-9Xx]+章\b|\d{1,2}\.\d{1,2}|摘要\b|Abstract\b|目录\b|参考文献\b|注：|\d+、|\[序号\])).{8,}$");
    }

    private static void AddDirectFormatRolePolicy(
        List<ProfileRolePolicy> policies,
        DocumentMap map,
        string role,
        int priority,
        double confidence,
        Func<DocumentParagraph, bool> predicate,
        string textPattern)
    {
        var paragraph = map.Paragraphs.FirstOrDefault(predicate);
        if (paragraph is null)
        {
            return;
        }

        if (policies.Any(policy =>
            string.Equals(policy.Role, role, StringComparison.Ordinal)
            && policy.Format is not null
            && ProfileFormatComparison.IsSamePolicyFormat(policy.Format, paragraph.Format)))
        {
            return;
        }

        policies.Add(new ProfileRolePolicy
        {
            Role = role,
            AppliesTo = "paragraph",
            Priority = priority,
            Confidence = confidence,
            Match = new ProfileRoleMatch
            {
                TextPatterns = [textPattern],
                OutlineLevels = paragraph.OutlineLevel.HasValue ? [paragraph.OutlineLevel.Value] : [],
                Format = BuildFormatMatch(role, paragraph.Format)
            },
            Format = ProfileSampleCloner.Clone(ProfileFormatComparison.NormalizePolicyFormat(paragraph.Format))
        });
    }

    private static ProfileRoleFormatMatch BuildFormatMatch(string role, ParagraphFormatSample format)
    {
        return role switch
        {
            "heading1" => new ProfileRoleFormatMatch
            {
                Alignment = "center",
                Bold = true,
                FontSizeHalfPoints = format.RunFormat?.FontSizeHalfPoints
            },
            "heading2" or "heading3" => new ProfileRoleFormatMatch
            {
                Bold = true,
                FontSizeHalfPoints = format.RunFormat?.FontSizeHalfPoints,
                FirstLineIndentTwips = new IntRangeMatch { Exact = format.FirstLineIndentTwips ?? 0 }
            },
            "body" => new ProfileRoleFormatMatch
            {
                Bold = false,
                FontSizeHalfPoints = format.RunFormat?.FontSizeHalfPoints,
                LineSpacing = format.LineSpacing,
                LineSpacingRule = format.LineSpacingRule,
                FirstLineIndentTwips = CreateBodyFirstLineIndentMatch(format.FirstLineIndentTwips)
            },
            _ => new ProfileRoleFormatMatch()
        };
    }

    private static IntRangeMatch? CreateBodyFirstLineIndentMatch(int? sampleFirstLineIndentTwips)
    {
        if (sampleFirstLineIndentTwips is null)
        {
            return null;
        }

        var min = Math.Min(360, sampleFirstLineIndentTwips.Value);
        var max = Math.Max(560, sampleFirstLineIndentTwips.Value);
        return new IntRangeMatch { Min = min, Max = max };
    }
}
