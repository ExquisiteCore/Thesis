using System.Text.Json.Nodes;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal static class ProfileRoleResolver
{
    public static string ResolveAlias(string role, JsonObject? profileOverrides, out string? error)
    {
        error = null;
        if (profileOverrides is null || !profileOverrides.TryGetPropertyValue("roleAliases", out var roleAliases))
        {
            return role;
        }

        if (roleAliases is null)
        {
            return role;
        }

        if (roleAliases is not JsonObject aliases)
        {
            error = "target_value_invalid";
            return role;
        }

        if (!aliases.TryGetPropertyValue(role, out var resolvedRoleNode) || resolvedRoleNode is null)
        {
            return role;
        }

        var resolvedRole = GetStringValue(resolvedRoleNode, out var valueError);
        if (valueError is not null || string.IsNullOrWhiteSpace(resolvedRole))
        {
            error = valueError ?? "target_value_invalid";
            return role;
        }

        return resolvedRole;
    }

    public static List<ProfileStyleRole> FindRoles(
        TemplateProfile? profile,
        JsonObject? profileOverrides,
        string? requestedRole,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(requestedRole))
        {
            error = "role_missing";
            return [];
        }

        var role = ResolveAlias(requestedRole, profileOverrides, out var aliasError);
        if (aliasError is not null)
        {
            error = aliasError;
            return [];
        }

        var matches = profile?.StyleRoles
            .Where(candidate => RoleNameMatches(candidate.Role, role))
            .ToList()
            ?? [];
        if (matches.Count == 0)
        {
            error = "role_not_found";
            return [];
        }

        return matches;
    }

    public static ParagraphFormatSample? FindRoleFormat(
        TemplateProfile? profile,
        JsonObject? profileOverrides,
        string? requestedRole,
        out string? error)
    {
        var roles = FindRoles(profile, profileOverrides, requestedRole, out error);
        if (error is null)
        {
            var roleFormat = roles.Select(role => role.Format).FirstOrDefault(candidate => candidate is not null);
            if (roleFormat is not null)
            {
                return roleFormat;
            }
        }

        if ((error is not null && error != "role_not_found") || string.IsNullOrWhiteSpace(requestedRole))
        {
            return null;
        }

        var role = ResolveAlias(requestedRole, profileOverrides, out var aliasError);
        if (aliasError is not null)
        {
            error = aliasError;
            return null;
        }

        var policyFormat = profile?.RolePolicies
            .Where(policy =>
                RoleNameMatches(policy.Role, role)
                && string.Equals(policy.AppliesTo, "paragraph", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(policy => policy.Priority)
            .Select(policy => policy.Format)
            .FirstOrDefault(candidate => candidate is not null);
        if (policyFormat is null)
        {
            var clusterFormat = profile?.FormatClusters
                .Where(cluster =>
                    RoleNameMatches(cluster.RoleHint, role)
                    && !string.Equals(cluster.RoleHint, "unknown", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(cluster.AppliesTo, "paragraph", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(cluster => cluster.Confidence)
                .ThenByDescending(cluster => cluster.Count)
                .Select(cluster => cluster.Format)
                .FirstOrDefault(candidate => candidate is not null);
            if (clusterFormat is null)
            {
                return null;
            }

            error = null;
            return clusterFormat;
        }

        error = null;
        return policyFormat;
    }

    internal static bool RoleNameMatches(string? candidate, string? requested)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(requested))
        {
            return false;
        }

        return string.Equals(candidate, requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeBuiltInRoleAlias(candidate), NormalizeBuiltInRoleAlias(requested), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBuiltInRoleAlias(string role)
    {
        return ThesisTextHeuristics.NormalizeTocRole(role);
    }

    private static string? GetStringValue(JsonNode value, out string? error)
    {
        error = null;
        try
        {
            return value.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            error = "target_value_invalid";
            return null;
        }
        catch (FormatException)
        {
            error = "target_value_invalid";
            return null;
        }
    }
}
