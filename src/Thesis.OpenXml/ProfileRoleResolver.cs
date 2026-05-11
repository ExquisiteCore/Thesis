using System.Text.Json.Nodes;
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
            .Where(candidate => string.Equals(candidate.Role, role, StringComparison.OrdinalIgnoreCase))
            .ToList()
            ?? [];
        if (matches.Count == 0)
        {
            error = "role_not_found";
            return [];
        }

        return matches;
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
