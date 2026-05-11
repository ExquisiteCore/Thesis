using Thesis.Schema;

namespace Thesis.Profile;

internal static class TemplateProfileDiagnosticsBuilder
{
    public static List<ProfileDiagnostic> Build(DocumentMap map)
    {
        var diagnostics = new List<ProfileDiagnostic>();

        if (!map.Paragraphs.Any(paragraph => ProfileTextHeuristics.IsChineseAbstractHeading(paragraph.Text)))
        {
            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "warning",
                Code = "profile_role_missing",
                Message = "Chinese abstract heading was not found.",
                Evidence = ["role:abstract.zh"]
            });
        }

        if (!map.Paragraphs.Any(paragraph => ProfileTextHeuristics.IsReferencesHeading(paragraph.Text)))
        {
            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "warning",
                Code = "profile_role_missing",
                Message = "References heading was not found.",
                Evidence = ["role:references"]
            });
        }

        if (map.Tables.Count == 0)
        {
            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "info",
                Code = "profile_table_missing",
                Message = "No table samples were found in the source document.",
                Evidence = ["tables:0"]
            });
        }

        AddDirectFormatDiagnostics(diagnostics, map);
        AddAmbiguousStyleDiagnostics(diagnostics, map);
        return diagnostics;
    }

    private static void AddDirectFormatDiagnostics(List<ProfileDiagnostic> diagnostics, DocumentMap map)
    {
        AddDirectFormatDiagnostic(diagnostics, map, "heading1", ProfileTextHeuristics.IsDirectHeading1);
        AddDirectFormatDiagnostic(diagnostics, map, "heading2", ProfileTextHeuristics.IsDirectHeading2);
        AddDirectFormatDiagnostic(diagnostics, map, "heading3", ProfileTextHeuristics.IsDirectHeading3);
        AddDirectFormatDiagnostic(diagnostics, map, "body", ProfileTextHeuristics.IsDirectBody);
    }

    private static void AddDirectFormatDiagnostic(
        List<ProfileDiagnostic> diagnostics,
        DocumentMap map,
        string role,
        Func<DocumentParagraph, bool> predicate)
    {
        var paragraph = map.Paragraphs.FirstOrDefault(predicate);
        if (paragraph is null)
        {
            return;
        }

        diagnostics.Add(new ProfileDiagnostic
        {
            Severity = "info",
            Code = "profile_role_inferred",
            Message = $"{role} policy inferred from paragraph text and direct formatting.",
            Evidence =
            [
                $"role:{role}",
                $"paragraph:{paragraph.Index}",
                $"fontSize:{paragraph.Format.RunFormat?.FontSizeHalfPoints}"
            ]
        });
    }

    private static void AddAmbiguousStyleDiagnostics(List<ProfileDiagnostic> diagnostics, DocumentMap map)
    {
        foreach (var group in map.Paragraphs
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.StyleId))
            .GroupBy(paragraph => paragraph.StyleId!, StringComparer.OrdinalIgnoreCase))
        {
            var detectedRoles = new List<string>();
            if (group.Any(ProfileTextHeuristics.IsDirectHeading1))
            {
                detectedRoles.Add("heading1");
            }

            if (group.Any(ProfileTextHeuristics.IsDirectHeading2))
            {
                detectedRoles.Add("heading2");
            }

            if (group.Any(ProfileTextHeuristics.IsDirectHeading3))
            {
                detectedRoles.Add("heading3");
            }

            if (group.Any(ProfileTextHeuristics.IsDirectBody))
            {
                detectedRoles.Add("body");
            }

            if (detectedRoles.Distinct(StringComparer.Ordinal).Count() < 2)
            {
                continue;
            }

            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "info",
                Code = "profile_style_ambiguous",
                Message = "A single paragraph style appears to carry multiple semantic roles; direct-format policies were used instead of style-only matching.",
                Evidence =
                [
                    $"style:{group.Key}",
                    "roles:" + string.Join(",", detectedRoles.Distinct(StringComparer.Ordinal))
                ]
            });
        }
    }
}
