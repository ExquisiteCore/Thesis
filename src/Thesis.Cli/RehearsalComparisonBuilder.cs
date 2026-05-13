using System.Text.RegularExpressions;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class RehearsalComparisonBuilder
{
    private const int MaxReportedContentGaps = 20;

    public static RehearsalComparisonReport Build(
        DocumentMap candidate,
        DocumentMap reference,
        ValidationReport? validation)
    {
        var report = new RehearsalComparisonReport
        {
            CandidateDocument = candidate.Path,
            ReferenceDocument = reference.Path,
            Candidate = Summarize(candidate),
            Reference = Summarize(reference),
            Validation = validation
        };

        AddStructureDiagnostics(candidate, reference, report);
        AddHeadingCoverage(candidate, reference, report);
        AddContentGaps(candidate, reference, report);
        AddHeadingQualityDiagnostics(candidate, report);
        AddFinalizationDiagnostics(candidate, report);
        AddValidationDiagnostics(validation, report);
        report.ReadyForFinalReview = report.Diagnostics.All(diagnostic =>
            !string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(diagnostic.Severity, "warning", StringComparison.OrdinalIgnoreCase));
        return report;
    }

    private static RehearsalDocumentSummary Summarize(DocumentMap map)
    {
        return new RehearsalDocumentSummary
        {
            ParagraphCount = map.Paragraphs.Count,
            NonEmptyParagraphCount = map.Paragraphs.Count(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text)),
            CharacterCount = map.Paragraphs.Sum(paragraph => paragraph.Text.Length),
            TableCount = map.Tables.Count,
            SectionCount = map.Sections.Count,
            RequiresFinalization = map.RequiresFinalization,
            HostFinalizationCurrent = map.HostFinalization?.IsCurrent == true,
            Headings = ExtractHeadings(map)
        };
    }

    private static void AddStructureDiagnostics(
        DocumentMap candidate,
        DocumentMap reference,
        RehearsalComparisonReport report)
    {
        if (candidate.Paragraphs.Count < reference.Paragraphs.Count)
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Code = "paragraph_count_gap",
                Message = $"Candidate has {candidate.Paragraphs.Count} paragraphs; reference has {reference.Paragraphs.Count}.",
                Path = candidate.Path
            });
        }

        if (candidate.Tables.Count < reference.Tables.Count)
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Code = "table_count_gap",
                Message = $"Candidate has {candidate.Tables.Count} tables; reference has {reference.Tables.Count}.",
                Path = candidate.Path
            });
        }

        if (candidate.Sections.Count < reference.Sections.Count)
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Code = "section_count_gap",
                Message = $"Candidate has {candidate.Sections.Count} sections; reference has {reference.Sections.Count}.",
                Path = candidate.Path
            });
        }
    }

    private static void AddHeadingCoverage(
        DocumentMap candidate,
        DocumentMap reference,
        RehearsalComparisonReport report)
    {
        var candidateHeadings = ExtractHeadings(candidate)
            .Select(NormalizeHeading)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referenceHeadings = ExtractHeadings(reference)
            .Where(heading => !IsTemplateInstructionHeading(heading))
            .ToList();
        var matched = 0;
        foreach (var heading in referenceHeadings)
        {
            if (candidateHeadings.Contains(NormalizeHeading(heading)))
            {
                matched++;
                continue;
            }

            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Code = "missing_reference_heading",
                Message = $"Candidate is missing reference heading: {heading}",
                Path = candidate.Path
            });
        }

        report.ContentCoverage.ReferenceHeadingCount = referenceHeadings.Count;
        report.ContentCoverage.MatchedHeadingCount = matched;
        report.ContentCoverage.HeadingCoverage = referenceHeadings.Count == 0
            ? 1
            : Math.Round((double)matched / referenceHeadings.Count, 4);
    }

    private static void AddFinalizationDiagnostics(DocumentMap candidate, RehearsalComparisonReport report)
    {
        if (!candidate.RequiresFinalization)
        {
            return;
        }

        report.Diagnostics.Add(new Diagnostic
        {
            Severity = "warning",
            Code = "candidate_requires_finalization",
            Message = "Candidate still requires Word/WPS finalization for fields, TOC page numbers, or true pagination.",
            Path = candidate.Path
        });
    }

    private static void AddContentGaps(
        DocumentMap candidate,
        DocumentMap reference,
        RehearsalComparisonReport report)
    {
        AddMissingParagraphGaps(candidate, reference, report);
        AddMissingTableGaps(candidate, reference, report);
    }

    private static void AddMissingParagraphGaps(
        DocumentMap candidate,
        DocumentMap reference,
        RehearsalComparisonReport report)
    {
        var candidateBodyStart = FindContentBodyStart(candidate);
        var referenceBodyStart = FindContentBodyStart(reference);
        var candidateParagraphs = candidate.Paragraphs
            .Where(paragraph => IsAtOrAfterBodyStart(paragraph.BodyElementIndex, candidateBodyStart))
            .Where(IsComparableBodyParagraph)
            .Select(paragraph => NormalizeContentText(paragraph.Text))
            .Where(text => text.Length > 0)
            .ToList();

        foreach (var paragraph in reference.Paragraphs
            .Where(paragraph => IsAtOrAfterBodyStart(paragraph.BodyElementIndex, referenceBodyStart))
            .Where(IsComparableBodyParagraph))
        {
            var normalized = NormalizeContentText(paragraph.Text);
            if (normalized.Length == 0 || HasComparableText(candidateParagraphs, normalized))
            {
                continue;
            }

            report.ContentCoverage.MissingReferenceParagraphCount++;
            AddGap(
                report,
                new RehearsalContentGap
                {
                    GapType = "paragraph",
                    ReferenceIndex = paragraph.Index,
                    ReferenceContext = FindNearestHeading(reference, paragraph.Index),
                    ReferenceTextPreview = PreviewText(paragraph.Text),
                    Message = $"Reference paragraph {paragraph.Index} is not represented in the candidate document."
                });
        }
    }

    private static void AddMissingTableGaps(
        DocumentMap candidate,
        DocumentMap reference,
        RehearsalComparisonReport report)
    {
        var candidateBodyStart = FindContentBodyStart(candidate);
        var referenceBodyStart = FindContentBodyStart(reference);
        var candidateTables = candidate.Tables
            .Where(table => IsAtOrAfterBodyStart(table.BodyElementIndex, candidateBodyStart))
            .Select(table => NormalizeContentText(table.TextPreview))
            .Where(text => text.Length > 0)
            .ToList();

        foreach (var table in reference.Tables
            .Where(table => IsAtOrAfterBodyStart(table.BodyElementIndex, referenceBodyStart)))
        {
            var normalized = NormalizeContentText(table.TextPreview);
            if (normalized.Length == 0 || HasComparableText(candidateTables, normalized))
            {
                continue;
            }

            report.ContentCoverage.MissingReferenceTableCount++;
            AddGap(
                report,
                new RehearsalContentGap
                {
                    GapType = "table",
                    ReferenceIndex = table.Index,
                    ReferenceContext = FindNearestHeadingBeforeBodyElement(reference, table.BodyElementIndex),
                    ReferenceTextPreview = PreviewText(table.TextPreview),
                    Message = $"Reference table {table.Index} is not represented in the candidate document."
                });
        }
    }

    private static int FindContentBodyStart(DocumentMap map)
    {
        var firstContentHeading = map.Paragraphs
            .Where(paragraph => IsContentStartHeading(paragraph.Text) || IsLikelyChapterHeading(paragraph.Text))
            .OrderBy(paragraph => paragraph.BodyElementIndex)
            .FirstOrDefault();

        return firstContentHeading?.BodyElementIndex ?? 0;
    }

    private static bool IsAtOrAfterBodyStart(int bodyElementIndex, int bodyStart)
    {
        return bodyElementIndex < 0 || bodyElementIndex >= bodyStart;
    }

    private static bool IsContentStartHeading(string text)
    {
        return ThesisTextHeuristics.IsChineseAbstractHeading(text)
            || ThesisTextHeuristics.IsEnglishAbstractHeading(text)
            || IsLikelyChapterHeading(text);
    }

    private static bool IsLikelyChapterHeading(string text)
    {
        return Regex.IsMatch(
            text.Trim(),
            @"^第[一二三四五六七八九十百千万零〇两0-9Xx]+章(?:\s+\S.*)?$",
            RegexOptions.CultureInvariant);
    }

    private static void AddGap(RehearsalComparisonReport report, RehearsalContentGap gap)
    {
        if (report.ContentCoverage.Gaps.Count < MaxReportedContentGaps)
        {
            report.ContentCoverage.Gaps.Add(gap);
        }
    }

    private static bool IsComparableBodyParagraph(DocumentParagraph paragraph)
    {
        var text = paragraph.Text.Trim();
        var comparableText = NormalizeContentText(text);
        if (comparableText.Length < 8
            || IsLikelyHeading(paragraph)
            || IsLikelyTocEntry(text)
            || ThesisTextHeuristics.IsFigureCaption(text)
            || ThesisTextHeuristics.IsTableCaption(text))
        {
            return false;
        }

        return true;
    }

    private static bool HasComparableText(List<string> candidates, string reference)
    {
        return candidates.Any(candidate => IsComparableText(candidate, reference));
    }

    private static bool IsComparableText(string candidate, string reference)
    {
        if (candidate.Length == 0 || reference.Length == 0)
        {
            return false;
        }

        if (candidate.Contains(reference, StringComparison.Ordinal)
            || reference.Contains(candidate, StringComparison.Ordinal))
        {
            return true;
        }

        return TextSimilarity(candidate, reference) >= 0.82;
    }

    private static double TextSimilarity(string left, string right)
    {
        var leftGrams = CharacterGrams(left).ToHashSet(StringComparer.Ordinal);
        var rightGrams = CharacterGrams(right).ToHashSet(StringComparer.Ordinal);
        if (leftGrams.Count == 0 || rightGrams.Count == 0)
        {
            return 0;
        }

        var intersection = leftGrams.Count(gram => rightGrams.Contains(gram));
        var union = leftGrams.Count + rightGrams.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static IEnumerable<string> CharacterGrams(string text)
    {
        if (text.Length <= 4)
        {
            yield return text;
            yield break;
        }

        for (var index = 0; index <= text.Length - 4; index++)
        {
            yield return text.Substring(index, 4);
        }
    }

    private static string FindNearestHeading(DocumentMap map, int referenceParagraphIndex)
    {
        for (var index = referenceParagraphIndex; index >= 0; index--)
        {
            var paragraph = map.Paragraphs.FirstOrDefault(item => item.Index == index);
            if (paragraph is not null && IsLikelyHeading(paragraph))
            {
                return paragraph.Text.Trim();
            }
        }

        return "";
    }

    private static string FindNearestHeadingBeforeBodyElement(DocumentMap map, int bodyElementIndex)
    {
        foreach (var paragraph in map.Paragraphs
            .Where(paragraph => paragraph.BodyElementIndex <= bodyElementIndex)
            .OrderByDescending(paragraph => paragraph.BodyElementIndex)
            .ThenByDescending(paragraph => paragraph.Index))
        {
            if (IsLikelyHeading(paragraph))
            {
                return paragraph.Text.Trim();
            }
        }

        return "";
    }

    private static string NormalizeContentText(string text)
    {
        var withoutFields = RemoveWordFieldInstructions(text);
        return Regex.Replace(withoutFields.Trim(), @"\s+", "", RegexOptions.CultureInvariant)
            .Replace("，", ",", StringComparison.Ordinal)
            .Replace("。", ".", StringComparison.Ordinal)
            .Replace("；", ";", StringComparison.Ordinal)
            .Replace("：", ":", StringComparison.Ordinal)
            .Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal)
            .Replace("．", ".", StringComparison.Ordinal);
    }

    private static string RemoveWordFieldInstructions(string text)
    {
        var withoutToc = Regex.Replace(
            text,
            @"\bTOC(?:\s+\\[A-Za-z]+(?:\s+""[^""]*"")?)*",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Regex.Replace(
            withoutToc,
            @"\b(?:REF|PAGEREF|NOTEREF)\s+\S+(?:\s+\\[A-Za-z]+|\s+\\\*\s*[A-Za-z]+|\s+MERGEFORMAT)*",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsLikelyTocEntry(string text)
    {
        var withoutFields = RemoveWordFieldInstructions(text).Trim();
        if (withoutFields.Length == 0
            || withoutFields.Contains('\t')
            || withoutFields.Contains("……", StringComparison.Ordinal)
            || withoutFields.Contains("......", StringComparison.Ordinal)
            || Regex.IsMatch(withoutFields, @"\.{3,}", RegexOptions.CultureInvariant))
        {
            return true;
        }

        var compact = Regex.Replace(withoutFields, @"\s+", "", RegexOptions.CultureInvariant);
        return IsSpecialTocEntry(compact) || IsNumberedHeadingTocEntry(withoutFields);
    }

    private static bool IsSpecialTocEntry(string compactText)
    {
        return Regex.IsMatch(
            compactText,
            @"^(?:摘要|中文摘要|Abstract|目录|Contents|参考文献|References|Bibliography|致谢|谢辞|Acknowledgements|Acknowledgments|附录.+|Appendix.+)(?:[IVXLCDM]+|\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsNumberedHeadingTocEntry(string text)
    {
        var trimmed = text.Trim();
        return Regex.IsMatch(
                trimmed,
                @"^第[一二三四五六七八九十百千万零〇两0-9Xx]+章\s+\S.+?\d+\s*$",
                RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                trimmed,
                @"^\d{1,2}[\.．]\d{1,2}(?:[\.．]\d{1,2})?\s+\S.+?\d+\s*$",
                RegexOptions.CultureInvariant);
    }

    private static string PreviewText(string text)
    {
        var normalized = Regex.Replace(RemoveWordFieldInstructions(text).Trim(), @"\s+", " ", RegexOptions.CultureInvariant);
        return normalized.Length <= 120 ? normalized : normalized[..120];
    }

    private static void AddValidationDiagnostics(ValidationReport? validation, RehearsalComparisonReport report)
    {
        if (validation is null || validation.Compliant)
        {
            return;
        }

        report.Diagnostics.Add(new Diagnostic
        {
            Severity = "warning",
            Code = "candidate_profile_validation_failed",
            Message = $"Candidate has {validation.Diagnostics.Count} profile validation diagnostics.",
            Path = report.CandidateDocument
        });
    }

    private static List<string> ExtractHeadings(DocumentMap map)
    {
        return map.Paragraphs
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text))
            .Where(paragraph => paragraph.OutlineLevel is not null
                || IsLikelyHeading(paragraph))
            .Select(paragraph => paragraph.Text.Trim())
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsLikelyHeading(DocumentParagraph paragraph)
    {
        var text = paragraph.Text.Trim();
        return ThesisTextHeuristics.IsSpecialSemanticHeading(text)
            || ThesisTextHeuristics.IsDirectHeading1(paragraph)
            || ThesisTextHeuristics.IsDirectHeading2(paragraph)
            || ThesisTextHeuristics.IsDirectHeading3(paragraph)
            || Regex.IsMatch(
                text,
                @"^(?:第[一二三四五六七八九十百千万零〇两0-9Xx]+章\s+\S.*|\d{1,2}\.\d{1,2}(?:\.\d{1,2})?\s+\S.*)$",
                RegexOptions.CultureInvariant);
    }

    private static bool IsTemplateInstructionHeading(string heading)
    {
        return heading.Contains('□', StringComparison.Ordinal);
    }

    private static string NormalizeHeading(string text)
    {
        var normalized = CollapseDuplicateHeadingPrefix(text.Trim());
        normalized = RemoveTocTrailingPageNumber(normalized);
        normalized = CollapseDuplicateHeadingPrefix(normalized);
        normalized = RemoveTocTrailingPageNumber(normalized);

        return Regex.Replace(normalized, @"\s+", "", RegexOptions.CultureInvariant)
            .Replace("：", ":", StringComparison.Ordinal)
            .Replace("．", ".", StringComparison.Ordinal)
            .Trim();
    }

    private static void AddHeadingQualityDiagnostics(DocumentMap candidate, RehearsalComparisonReport report)
    {
        foreach (var heading in ExtractHeadings(candidate).Where(HasDuplicateHeadingPrefix))
        {
            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Code = "candidate_duplicate_heading_prefix",
                Message = $"Candidate heading appears to repeat its numbering prefix: {heading}",
                Path = candidate.Path
            });
        }
    }

    private static bool HasDuplicateHeadingPrefix(string text)
    {
        return !string.Equals(text, CollapseDuplicateHeadingPrefix(text), StringComparison.Ordinal);
    }

    private static string CollapseDuplicateHeadingPrefix(string text)
    {
        var normalized = text.Trim();
        while (true)
        {
            var collapsed = Regex.Replace(
                normalized,
                @"^\s*(?<prefix>(?:第[一二三四五六七八九十百千万零〇两0-9Xx]+章)|(?:\d{1,2}[\.．]\d{1,2}(?:[\.．]\d{1,2})?))\s+\k<prefix>(?=\s)",
                "${prefix}",
                RegexOptions.CultureInvariant);
            if (string.Equals(collapsed, normalized, StringComparison.Ordinal))
            {
                return normalized;
            }

            normalized = collapsed.Trim();
        }
    }

    private static string RemoveTocTrailingPageNumber(string text)
    {
        var normalized = Regex.Replace(
            text.Trim(),
            @"[\s\t]*(?:\.|．|…){2,}\s*\d+\s*$",
            "",
            RegexOptions.CultureInvariant);

        return Regex.Replace(
            normalized,
            @"^(?<prefix>(?:第[一二三四五六七八九十百千万零〇两0-9Xx]+章)|(?:\d{1,2}[\.．]\d{1,2}(?:[\.．]\d{1,2})?))\s+(?<title>.+?)(?<page>\d+)\s*$",
            "${prefix} ${title}",
            RegexOptions.CultureInvariant).Trim();
    }
}
