using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal sealed partial class OpenXmlTargetResolver
{
    private TargetResolutionResult ResolveRole(JsonObject target, RunOptions options)
    {
        var role = GetString(target, "role", out var roleError);
        if (roleError is not null || string.IsNullOrWhiteSpace(role))
        {
            return TargetResolutionResult.Error(roleError ?? "target_value_invalid");
        }

        var position = GetString(target, "position", out var positionError) ?? "self";
        if (positionError is not null)
        {
            return TargetResolutionResult.Error(positionError);
        }

        if (position is not "self" and not "afterHeading" and not "beforeHeading")
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        var offset = GetInt(target, "offset", out var offsetError);
        if (offsetError is not null)
        {
            return TargetResolutionResult.Error(offsetError);
        }

        offset ??= position == "self" ? 0 : 1;
        if (offset < 0)
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        var resolvedRole = ProfileRoleResolver.ResolveAlias(role, _profileOverrides, out var aliasError);
        if (aliasError is not null)
        {
            return TargetResolutionResult.Error(aliasError);
        }

        var policyResolution = ResolveRolePolicyAnchors(resolvedRole, out var policyError);
        if (policyError is not null)
        {
            return TargetResolutionResult.Error(policyError);
        }

        var anchorIndices = policyResolution is { Count: > 0 } ? policyResolution : null;
        var profileRoles = _profile?.StyleRoles
            .Where(candidate => ProfileRoleResolver.RoleNameMatches(candidate.Role, resolvedRole))
            .ToList()
            ?? [];
        if (anchorIndices is null)
        {
            var evidenceAnchors = GetTrustedRoleEvidenceIndices(profileRoles);
            if (evidenceAnchors.Count > 0)
            {
                anchorIndices = evidenceAnchors;
            }
        }

        if (anchorIndices is null)
        {
            anchorIndices = ResolveSemanticRoleAnchorIndices(resolvedRole);
        }

        if (anchorIndices is null && profileRoles.Count > 0 && ThesisTextHeuristics.SemanticRolePredicate(resolvedRole) is null)
        {
            var styleAnchors = GetRoleStyleAnchorIndices(profileRoles);
            if (styleAnchors.Count > 0)
            {
                anchorIndices = styleAnchors;
            }
        }

        if (anchorIndices is null)
        {
            if (profileRoles.Count == 0)
            {
                return ResolveFormatClusterOrError(resolvedRole, position, offset.Value, options, "role_not_found");
            }

            return ResolveFormatClusterOrError(resolvedRole, position, offset.Value, options, "target_not_found");
        }

        var matches = anchorIndices
            .Select(index => ApplyRolePosition(index, position, offset.Value))
            .Where(index => index >= 0 && index < Paragraphs.Count)
            .Distinct()
            .Select(index => (ResolvedTarget)new ResolvedParagraphTarget(Paragraphs[index], index))
            .ToList();

        return ValidateMatchCount(matches, options);
    }

    private List<int>? ResolveSemanticRoleAnchorIndices(string role)
    {
        var predicate = ThesisTextHeuristics.SemanticRolePredicate(role);
        if (predicate is null)
        {
            return null;
        }

        var matches = Paragraphs
            .Select((paragraph, index) => (Paragraph: paragraph, Index: index))
            .Where(candidate => !ThesisTextHeuristics.IsLikelyTocLine(candidate.Paragraph.InnerText))
            .Where(candidate => predicate(candidate.Paragraph.InnerText))
            .Select(candidate => candidate.Index)
            .ToList();
        return matches.Count == 0 ? null : matches;
    }

    private TargetResolutionResult ResolveFormatClusterOrError(
        string role,
        string position,
        int offset,
        RunOptions options,
        string fallbackError)
    {
        var clusterAnchorIndices = ResolveFormatClusterAnchorIndices(role, out var clusterError);
        if (clusterError is not null)
        {
            return TargetResolutionResult.Error(clusterError);
        }

        if (clusterAnchorIndices is null)
        {
            return TargetResolutionResult.Error(fallbackError);
        }

        var matches = clusterAnchorIndices
            .Select(index => ApplyRolePosition(index, position, offset))
            .Where(index => index >= 0 && index < Paragraphs.Count)
            .Distinct()
            .Select(index => (ResolvedTarget)new ResolvedParagraphTarget(Paragraphs[index], index))
            .ToList();

        return ValidateMatchCount(matches, options);
    }

    private List<int>? ResolveRolePolicyAnchors(string role, out string? error)
    {
        error = null;
        var policies = _profile?.RolePolicies
            .Where(policy =>
                ProfileRoleResolver.RoleNameMatches(policy.Role, role)
                && string.Equals(policy.AppliesTo, "paragraph", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(policy => policy.Priority)
            .ToList();
        if (policies is null || policies.Count == 0)
        {
            return null;
        }

        try
        {
            return Paragraphs
                .Select((paragraph, index) => (Paragraph: paragraph, Index: index))
                .Where(candidate => policies.Any(policy => RolePolicyMatches(candidate.Paragraph, policy)))
                .Select(candidate => candidate.Index)
                .ToList();
        }
        catch (ArgumentException)
        {
            error = "target_value_invalid";
            return [];
        }
    }

    private List<int>? ResolveFormatClusterAnchorIndices(string role, out string? error)
    {
        error = null;
        var clusters = _profile?.FormatClusters
            .Where(cluster =>
                ProfileRoleResolver.RoleNameMatches(cluster.RoleHint, role)
                && !string.Equals(cluster.RoleHint, "unknown", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cluster.AppliesTo, "paragraph", StringComparison.OrdinalIgnoreCase)
                && cluster.Match.Format is not null)
            .OrderByDescending(cluster => cluster.Confidence)
            .ThenByDescending(cluster => cluster.Count)
            .ToList();
        if (clusters is null || clusters.Count == 0)
        {
            return null;
        }

        return Paragraphs
            .Select((paragraph, index) => (Paragraph: paragraph, Index: index))
            .Where(candidate => clusters.Any(cluster => FormatClusterMatches(candidate.Paragraph, cluster)))
            .Select(candidate => candidate.Index)
            .ToList();
    }

    private static bool FormatClusterMatches(Paragraph paragraph, ProfileFormatCluster cluster)
    {
        return StyleMatches(paragraph, cluster.Match.StyleIds)
            && TextPatternMatches(paragraph, cluster.Match.TextPatterns)
            && FormatMatches(paragraph, cluster.Match.Format);
    }

    private TargetResolutionResult ResolveSectionRange(JsonObject target, RunOptions options)
    {
        var includeStart = GetBool(target, "includeStart", out var includeStartError) ?? false;
        var includeEnd = GetBool(target, "includeEnd", out var includeEndError) ?? false;
        if (includeStartError is not null || includeEndError is not null)
        {
            return TargetResolutionResult.Error(includeStartError ?? includeEndError!);
        }

        if (!TryResolveRangeAnchor(target["start"], out var startIndex, out var startError))
        {
            return TargetResolutionResult.Error(startError);
        }

        var endNode = target["end"];
        var endIndex = Paragraphs.Count - 1;
        if (endNode is not null && !TryResolveRangeAnchor(endNode, out endIndex, out var endError))
        {
            return TargetResolutionResult.Error(endError);
        }

        if (startIndex > endIndex)
        {
            return TargetResolutionResult.Error("range_invalid");
        }

        var firstIndex = includeStart ? startIndex : startIndex + 1;
        var lastIndex = endNode is null || includeEnd ? endIndex : endIndex - 1;
        var matches = firstIndex > lastIndex
            ? []
            : Enumerable.Range(firstIndex, lastIndex - firstIndex + 1)
                .Select(index => (ResolvedTarget)new ResolvedParagraphTarget(Paragraphs[index], index))
                .ToList();

        return ValidateMatchCount(matches, options);
    }

    private bool TryResolveRangeAnchor(JsonNode? anchor, out int paragraphIndex, out string error)
    {
        paragraphIndex = -1;
        error = "";

        if (anchor is null)
        {
            error = "range_anchor_missing";
            return false;
        }

        var result = Resolve(anchor, new RunOptions
        {
            CreateSnapshot = false,
            StopOnError = true,
            RequireSingleMatch = false,
            TrackChanges = false
        });

        if (!result.Success)
        {
            error = result.ErrorCode switch
            {
                "target_ambiguous" or "range_anchor_ambiguous" => "range_anchor_ambiguous",
                "target_value_invalid" => "target_value_invalid",
                _ => "range_anchor_missing"
            };
            return false;
        }

        if (result.Matches.Count != 1 || result.Matches[0] is not ResolvedParagraphTarget paragraphTarget)
        {
            error = "range_anchor_ambiguous";
            return false;
        }

        paragraphIndex = paragraphTarget.ParagraphIndex;
        return true;
    }

    private List<int> GetTrustedRoleEvidenceIndices(List<ProfileStyleRole> profileRoles)
    {
        return [.. profileRoles
            .SelectMany(role => role.Evidence)
            .Where(evidence => evidence.ParagraphIndex >= 0 && evidence.ParagraphIndex < Paragraphs.Count)
            .Where(evidence => EvidenceMatches(Paragraphs[evidence.ParagraphIndex], evidence))
            .Select(evidence => evidence.ParagraphIndex)
            .Distinct()];
    }

    private List<int> GetRoleStyleAnchorIndices(List<ProfileStyleRole> profileRoles)
    {
        var styleIds = profileRoles
            .Select(role => role.StyleId)
            .Where(styleId => !string.IsNullOrWhiteSpace(styleId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (styleIds.Count == 0)
        {
            return [];
        }

        return Paragraphs
            .Select((paragraph, index) => (Paragraph: paragraph, Index: index))
            .Where(candidate =>
            {
                var styleId = GetParagraphStyleId(candidate.Paragraph);
                return styleId is not null && styleIds.Contains(styleId);
            })
            .Select(candidate => candidate.Index)
            .ToList();
    }

    private static bool EvidenceMatches(Paragraph paragraph, ProfileParagraphEvidence evidence)
    {
        if (!string.IsNullOrWhiteSpace(evidence.StyleId)
            && !string.Equals(GetParagraphStyleId(paragraph), evidence.StyleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(evidence.TextPreview)
            || paragraph.InnerText.StartsWith(evidence.TextPreview, StringComparison.Ordinal);
    }

    private static int ApplyRolePosition(int anchorIndex, string position, int offset)
    {
        return position switch
        {
            "beforeHeading" => anchorIndex - offset,
            _ => anchorIndex + offset
        };
    }
}
