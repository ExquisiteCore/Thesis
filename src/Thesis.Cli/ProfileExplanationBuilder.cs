using Thesis.Schema;

namespace Thesis.Cli;

internal static class ProfileExplanationBuilder
{
    public static ProfileExplanation Build(TemplateProfile profile, string profilePath)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);

        var explanation = new ProfileExplanation
        {
            ProfilePath = Path.GetFullPath(profilePath),
            SourceType = profile.SourceType,
            SourceDocument = profile.SourceDocument,
            RequiresFinalization = profile.RequiresFinalization,
            SourceEvidence = profile.SourceEvidence ?? new ProfileSourceEvidence(),
            RoleSummaries = BuildRoleSummaries(profile),
            TableSummary = BuildTableSummary(profile),
            Risks = BuildRisks(profile)
        };

        return explanation;
    }

    private static List<ProfileRoleExplanation> BuildRoleSummaries(TemplateProfile profile)
    {
        return profile.StyleRoles
            .OrderByDescending(role => role.Confidence)
            .ThenBy(role => role.Role, StringComparer.Ordinal)
            .Select(role => new ProfileRoleExplanation
            {
                Role = role.Role,
                StyleId = role.StyleId,
                Confidence = role.Confidence,
                EvidenceCount = role.Evidence.Count,
                HasFormat = role.Format is not null || !string.IsNullOrWhiteSpace(role.StyleId),
                SampleText = role.Evidence.FirstOrDefault()?.TextPreview
            })
            .ToList();
    }

    private static ProfileTableExplanation BuildTableSummary(TemplateProfile profile)
    {
        return new ProfileTableExplanation
        {
            Detected = profile.TablePolicy.Detected,
            TableCount = profile.TablePolicy.TableCount,
            ObservedColumnCounts = [.. profile.TablePolicy.ObservedColumnCounts],
            HasDefaultFormat = profile.TablePolicy.Default?.Format is not null,
            ArchetypeCount = profile.TableArchetypes.Count
        };
    }

    private static List<ProfileRisk> BuildRisks(TemplateProfile profile)
    {
        var risks = new List<ProfileRisk>();

        if (profile.RequiresFinalization)
        {
            risks.Add(new ProfileRisk
            {
                Severity = "warning",
                Code = "finalization_required",
                Message = "The source document contains fields or layout-dependent content that may require a host application to update."
            });
        }

        risks.AddRange(profile.Diagnostics.Select(diagnostic => new ProfileRisk
        {
            Severity = diagnostic.Severity,
            Code = diagnostic.Code,
            Message = diagnostic.Message
        }));

        if (profile.StyleRoles.Count == 0)
        {
            risks.Add(new ProfileRisk
            {
                Severity = "warning",
                Code = "profile_roles_empty",
                Message = "No style roles were detected; role-based operations may need explicit targets."
            });
        }

        if (!profile.TablePolicy.Detected)
        {
            risks.Add(new ProfileRisk
            {
                Severity = "info",
                Code = "profile_tables_empty",
                Message = "No table profile was detected; table operations should use explicit formatting or archetype overrides."
            });
        }

        return risks;
    }
}
