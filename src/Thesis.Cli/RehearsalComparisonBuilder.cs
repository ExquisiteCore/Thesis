using System.Text.RegularExpressions;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class RehearsalComparisonBuilder
{
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
