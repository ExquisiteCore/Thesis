using System.Text.Json.Nodes;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class ProfileComplianceValidator
{
    public static ValidationReport Validate(DocumentMap map, TemplateProfile profile)
    {
        profile.StyleRoles ??= [];
        profile.TableArchetypes ??= [];
        profile.TablePolicy ??= new ProfileTablePolicy();

        var report = new ValidationReport
        {
            CheckedParagraphs = map.Paragraphs.Count,
            CheckedTables = map.Tables.Count
        };

        ValidateRoleEvidence(map, profile, report);
        ValidateTables(map, profile, report);
        report.Compliant = report.Diagnostics.Count == 0;
        return report;
    }

    private static void ValidateRoleEvidence(DocumentMap map, TemplateProfile profile, ValidationReport report)
    {
        foreach (var role in profile.StyleRoles.Where(role => role.Format is not null))
        {
            foreach (var evidence in role.Evidence)
            {
                var paragraph = map.Paragraphs.FirstOrDefault(candidate => candidate.Index == evidence.ParagraphIndex);
                if (paragraph is null)
                {
                    report.Diagnostics.Add(new Diagnostic
                    {
                        Severity = "warning",
                        Code = "profile_role_evidence_missing",
                        Message = $"Role '{role.Role}' evidence paragraph {evidence.ParagraphIndex} was not found.",
                        Path = $"paragraphs[{evidence.ParagraphIndex}]"
                    });
                    continue;
                }

                if (ParagraphFormatMatches(paragraph.Format, role.Format!))
                {
                    continue;
                }

                report.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Code = "profile_role_format_mismatch",
                    Message = $"Paragraph {paragraph.Index} does not match profile role '{role.Role}'.",
                    Path = $"paragraphs[{paragraph.Index}]"
                });
                report.SuggestedOperations.Add(new ThesisOperation
                {
                    Id = $"fix-{SafeId(role.Role)}-{paragraph.Index}",
                    Op = "applyProfileRole",
                    Role = role.Role,
                    Target = new JsonObject
                    {
                        ["type"] = "paragraphIndex",
                        ["index"] = paragraph.Index
                    }
                });
            }
        }
    }

    private static void ValidateTables(DocumentMap map, TemplateProfile profile, ValidationReport report)
    {
        var profileFormat = profile.TablePolicy.Default?.Format;
        if (profileFormat is null)
        {
            return;
        }

        foreach (var table in map.Tables)
        {
            if (TableFormatMatches(table.Format, profileFormat))
            {
                continue;
            }

            report.Diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Code = "profile_table_format_mismatch",
                Message = $"Table {table.Index} does not match the default profile table format.",
                Path = $"tables[{table.Index}]"
            });
            report.SuggestedOperations.Add(new ThesisOperation
            {
                Id = $"fix-table-{table.Index}",
                Op = "applyProfileTable",
                Target = new JsonObject
                {
                    ["type"] = "tableIndex",
                    ["index"] = table.Index
                }
            });
        }
    }

    private static bool ParagraphFormatMatches(ParagraphFormatSample actual, ParagraphFormatSample expected)
    {
        return StringMatches(actual.StyleId, expected.StyleId)
            && StringMatches(actual.Alignment, expected.Alignment)
            && IntMatches(actual.SpacingBeforeTwips, expected.SpacingBeforeTwips)
            && IntMatches(actual.SpacingAfterTwips, expected.SpacingAfterTwips)
            && StringMatches(actual.LineSpacing, expected.LineSpacing)
            && StringMatches(actual.LineSpacingRule, expected.LineSpacingRule)
            && IntMatches(actual.FirstLineIndentTwips, expected.FirstLineIndentTwips)
            && IntMatches(actual.LeftIndentTwips, expected.LeftIndentTwips)
            && IntMatches(actual.RightIndentTwips, expected.RightIndentTwips)
            && RunFormatMatches(actual.RunFormat, expected.RunFormat);
    }

    private static bool RunFormatMatches(RunFormatSample? actual, RunFormatSample? expected)
    {
        if (expected is null)
        {
            return true;
        }

        return BoolMatches(actual?.Bold, expected.Bold)
            && BoolMatches(actual?.Italic, expected.Italic)
            && StringMatches(actual?.FontSizeHalfPoints, expected.FontSizeHalfPoints)
            && StringMatches(actual?.AsciiFont, expected.AsciiFont)
            && StringMatches(actual?.HighAnsiFont, expected.HighAnsiFont)
            && StringMatches(actual?.EastAsiaFont, expected.EastAsiaFont)
            && StringMatches(actual?.ComplexScriptFont, expected.ComplexScriptFont);
    }

    private static bool TableFormatMatches(TableFormatSample actual, TableFormatSample expected)
    {
        return IntMatches(actual.WidthTwips, expected.WidthTwips)
            && StringMatches(actual.WidthType, expected.WidthType)
            && StringMatches(actual.Alignment, expected.Alignment)
            && ListMatches(actual.GridColumnWidthsTwips, expected.GridColumnWidthsTwips)
            && actual.HeaderRowCount == expected.HeaderRowCount;
    }

    private static bool StringMatches(string? actual, string? expected)
    {
        return expected is null || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IntMatches(int? actual, int? expected)
    {
        return expected is null || actual == expected;
    }

    private static bool BoolMatches(bool? actual, bool? expected)
    {
        return expected is null || actual == expected;
    }

    private static bool ListMatches(List<int> actual, List<int> expected)
    {
        return expected.Count == 0 || actual.SequenceEqual(expected);
    }

    private static string SafeId(string value)
    {
        var chars = value
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-').ToLowerInvariant();
    }
}
