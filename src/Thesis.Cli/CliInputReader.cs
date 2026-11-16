using Thesis.Schema;
using Thesis.Core;
using Thesis.Session;
using Thesis.OpenXml;
using static Thesis.Cli.CliCommandSupport;

namespace Thesis.Cli;

internal static class CliInputReader
{
    internal static bool TryReadProfile(string path, out TemplateProfile? profile, out CliResult? error)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            profile = ThesisJson.Deserialize<TemplateProfile>(File.ReadAllText(fullPath));
            if (!IsValidProfile(profile))
            {
                error = ProfileInvalid(fullPath, "Profile has an invalid structure.");
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            profile = null;
            error = new CliResult
            {
                Status = "error",
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "profile_invalid",
                        Message = $"Profile JSON could not be read: {ex.Message}",
                        Path = fullPath
                    }
                ]
            };
            return false;
        }
    }

    internal static bool IsValidProfile(TemplateProfile? profile)
    {
        return profile is not null
            && profile.StyleRoles is not null
            && profile.RolePolicies is not null
            && profile.FormatClusters is not null
            && profile.FinalizationReasons is not null
            && profile.PageSetup is not null
            && profile.StructurePolicy is not null
            && profile.StructurePolicy.Sections is not null
            && profile.StructurePolicy.Sections.All(section => section is not null)
            && profile.StylePolicy is not null
            && profile.StylePolicy.NumericStyleIds is not null
            && profile.StylePolicy.DisallowedGeneratedStyleIds is not null
            && profile.PackagePolicy is not null
            && profile.FieldPolicy is not null
            && profile.ZonePolicy is not null
            && profile.ZonePolicy.Landmarks is not null
            && profile.ZonePolicy.ForbiddenFrontMatterHeadings is not null
            && profile.NumberingPolicy is not null
            && profile.TablePolicy is not null
            && profile.TableArchetypes is not null
            && profile.Diagnostics is not null
            && profile.SourceEvidence is not null
            && profile.StyleRoles.All(role => role.Evidence is not null)
            && profile.RolePolicies.All(policy =>
                policy.Match is not null
                && policy.Match.StyleIds is not null
                && policy.Match.TextPatterns is not null
                && policy.Match.OutlineLevels is not null
                && IsValidRoleFormatMatch(policy.Match.Format))
            && profile.FormatClusters.All(IsValidFormatCluster)
            && profile.NumberingPolicy.Instances is not null
            && profile.NumberingPolicy.ParagraphUses is not null
            && profile.TablePolicy.ObservedColumnCounts is not null
            && profile.TableArchetypes.All(archetype =>
                archetype.Match is not null
                && archetype.Match.ColumnCounts is not null)
            && profile.Diagnostics.All(diagnostic => diagnostic.Evidence is not null)
            && profile.SourceEvidence.ParagraphSamples is not null;
    }

    internal static bool IsValidFormatCluster(ProfileFormatCluster cluster)
    {
        return !string.IsNullOrWhiteSpace(cluster.Id)
            && string.Equals(cluster.AppliesTo, "paragraph", StringComparison.OrdinalIgnoreCase)
            && IsKnownClusterRoleHint(cluster.RoleHint)
            && cluster.Count >= 0
            && cluster.Confidence is >= 0 and <= 1
            && cluster.StyleIds is not null
            && cluster.Match is not null
            && cluster.Match.StyleIds is not null
            && cluster.Match.TextPatterns is not null
            && cluster.Match.OutlineLevels is not null
            && IsValidRoleFormatMatch(cluster.Match.Format)
            && cluster.Format is not null
            && cluster.Evidence is not null
            && cluster.Evidence.All(evidence => evidence is not null);
    }

    internal static bool IsKnownClusterRoleHint(string? roleHint)
    {
        return roleHint is "unknown" or "title" or "heading1" or "heading2" or "heading3" or "body" or "abstract.zh" or "abstract.en" or "toc" or "toc.title" or "references";
    }

    internal static bool IsValidRoleFormatMatch(ProfileRoleFormatMatch? format)
    {
        return format is null
            || (IsValidRange(format.FirstLineIndentTwips)
                && IsValidRange(format.LeftIndentTwips)
                && IsValidRange(format.RightIndentTwips));
    }

    internal static bool IsValidRange(IntRangeMatch? range)
    {
        if (range is null)
        {
            return true;
        }

        if (range.Exact is null && range.Min is null && range.Max is null)
        {
            return false;
        }

        if (range.Exact is not null && (range.Min is not null || range.Max is not null))
        {
            return false;
        }

        return range.Min is null || range.Max is null || range.Min <= range.Max;
    }

    internal static CliResult ProfileInvalid(string path, string message)
    {
        return new CliResult
        {
            Status = "error",
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "error",
                    Code = "profile_invalid",
                    Message = message,
                    Path = path
                }
            ]
        };
    }

    internal static bool TryReadRequest(string path, out OperationRequest request, out CliResult? error)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            request = ThesisJson.Deserialize<OperationRequest>(File.ReadAllText(fullPath));
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            request = new OperationRequest();
            error = new CliResult
            {
                Status = "error",
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "request_invalid",
                        Message = $"Request JSON could not be read: {ex.Message}",
                        Path = fullPath
                    }
                ]
            };
            return false;
        }
    }

    internal static bool TryReadProjectRules(string path, out ProjectRules? rules, out CliResult? error)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            rules = ThesisJson.Deserialize<ProjectRules>(File.ReadAllText(fullPath));
            if (!string.Equals(rules.RulesKind, "projectRules", StringComparison.OrdinalIgnoreCase))
            {
                error = new CliResult
                {
                    Status = "error",
                    Diagnostics =
                    [
                        new Diagnostic
                        {
                            Severity = "error",
                            Code = "project_rules_invalid",
                            Message = "Project rules JSON must have rulesKind 'projectRules'.",
                            Path = fullPath
                        }
                    ]
                };
                return false;
            }

            NormalizeProjectRules(rules);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            rules = null;
            error = new CliResult
            {
                Status = "error",
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "project_rules_invalid",
                        Message = $"Project rules JSON could not be read: {ex.Message}",
                        Path = fullPath
                    }
                ]
            };
            return false;
        }
    }

    internal static bool TryReadContent(string path, out ThesisContent? content, out CliResult? error)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            content = ThesisJson.Deserialize<ThesisContent>(File.ReadAllText(fullPath));
            ContentNormalizer.Normalize(content);
            ContentPaths.Resolve(content, fullPath);
            if (!string.Equals(content.DocumentKind, "thesisContent", StringComparison.OrdinalIgnoreCase))
            {
                error = new CliResult
                {
                    Status = "error",
                    Diagnostics =
                    [
                        new Diagnostic
                        {
                            Severity = "error",
                            Code = "content_invalid",
                            Message = "Content JSON must have documentKind 'thesisContent'.",
                            Path = fullPath
                        }
                    ]
                };
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            content = null;
            error = new CliResult
            {
                Status = "error",
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "content_invalid",
                        Message = $"Content JSON could not be read: {ex.Message}",
                        Path = fullPath
                    }
                ]
            };
            return false;
        }
    }

    internal static void NormalizeProfile(TemplateProfile profile)
    {
        profile.StyleRoles ??= [];
        profile.RoleAliases ??= [];
        profile.Diagnostics ??= [];
        profile.TableArchetypes ??= [];
        profile.TablePolicy ??= new ProfileTablePolicy();
        profile.PageSetup ??= new ProfilePageSetup();
        profile.StructurePolicy ??= new ProfileStructurePolicy();
        profile.StructurePolicy.Sections ??= [];
        profile.StylePolicy ??= new ProfileStylePolicy();
        profile.StylePolicy.NumericStyleIds ??= [];
        profile.StylePolicy.DisallowedGeneratedStyleIds ??= [];
        profile.PackagePolicy ??= new ProfilePackagePolicy();
        profile.FieldPolicy ??= new ProfileFieldPolicy();
        profile.ZonePolicy ??= new ProfileZonePolicy();
        profile.ZonePolicy.Landmarks ??= [];
        profile.ZonePolicy.ForbiddenFrontMatterHeadings ??= [];
        profile.SourceEvidence ??= new ProfileSourceEvidence();
    }

    internal static void NormalizeProjectRules(ProjectRules rules)
    {
        rules.RoleAliases ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        rules.RoleFormats ??= new Dictionary<string, ProjectParagraphFormatRule>(StringComparer.OrdinalIgnoreCase);
        rules.RolePolicies ??= [];
        if (rules.StructurePolicy is not null)
        {
            rules.StructurePolicy.Sections ??= [];
        }

        if (rules.StylePolicy is not null)
        {
            rules.StylePolicy.NumericStyleIds ??= [];
            rules.StylePolicy.DisallowedGeneratedStyleIds ??= [];
        }

        if (rules.ZonePolicy is not null)
        {
            rules.ZonePolicy.Landmarks ??= [];
            rules.ZonePolicy.ForbiddenFrontMatterHeadings ??= [];
        }

        rules.TableArchetypes ??= [];
        rules.Diagnostics ??= [];
    }


}
