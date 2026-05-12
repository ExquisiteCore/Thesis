using Thesis.Schema;

namespace Thesis.Cli;

internal static class ProfileDiffBuilder
{
    public static ProfileDiff Build(TemplateProfile left, string leftPath, TemplateProfile right, string rightPath)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var diff = new ProfileDiff
        {
            LeftProfilePath = Path.GetFullPath(leftPath),
            RightProfilePath = Path.GetFullPath(rightPath)
        };

        AddScalarChange(diff, "sourceType", left.SourceType, right.SourceType);
        AddScalarChange(diff, "sourceDocument", left.SourceDocument, right.SourceDocument);
        AddScalarChange(diff, "requiresFinalization", left.RequiresFinalization, right.RequiresFinalization);
        AddPageSetupChanges(diff, left.PageSetup, right.PageSetup);
        AddRoleChanges(diff, left.StyleRoles, right.StyleRoles);
        AddTableChanges(diff, left.TablePolicy, right.TablePolicy, left.TableArchetypes.Count, right.TableArchetypes.Count);
        AddDiagnosticChanges(diff, left.Diagnostics, right.Diagnostics);

        diff.HasChanges = diff.Changes.Count > 0;
        return diff;
    }

    private static void AddPageSetupChanges(ProfileDiff diff, ProfilePageSetup left, ProfilePageSetup right)
    {
        AddScalarChange(diff, "pageSetup.pageSize.widthTwips", left.PageSize?.WidthTwips, right.PageSize?.WidthTwips);
        AddScalarChange(diff, "pageSetup.pageSize.heightTwips", left.PageSize?.HeightTwips, right.PageSize?.HeightTwips);
        AddScalarChange(diff, "pageSetup.pageSize.orientation", left.PageSize?.Orientation, right.PageSize?.Orientation);
        AddScalarChange(diff, "pageSetup.margins.topTwips", left.Margins?.TopTwips, right.Margins?.TopTwips);
        AddScalarChange(diff, "pageSetup.margins.rightTwips", left.Margins?.RightTwips, right.Margins?.RightTwips);
        AddScalarChange(diff, "pageSetup.margins.bottomTwips", left.Margins?.BottomTwips, right.Margins?.BottomTwips);
        AddScalarChange(diff, "pageSetup.margins.leftTwips", left.Margins?.LeftTwips, right.Margins?.LeftTwips);
        AddScalarChange(diff, "pageSetup.margins.headerTwips", left.Margins?.HeaderTwips, right.Margins?.HeaderTwips);
        AddScalarChange(diff, "pageSetup.margins.footerTwips", left.Margins?.FooterTwips, right.Margins?.FooterTwips);
        AddScalarChange(diff, "pageSetup.headers.count", left.Headers.Count, right.Headers.Count);
        AddScalarChange(diff, "pageSetup.footers.count", left.Footers.Count, right.Footers.Count);
    }

    private static void AddRoleChanges(ProfileDiff diff, List<ProfileStyleRole> leftRoles, List<ProfileStyleRole> rightRoles)
    {
        var leftByRole = leftRoles.ToDictionary(role => role.Role, StringComparer.Ordinal);
        var rightByRole = rightRoles.ToDictionary(role => role.Role, StringComparer.Ordinal);
        foreach (var role in leftByRole.Keys.Union(rightByRole.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var leftExists = leftByRole.TryGetValue(role, out var left);
            var rightExists = rightByRole.TryGetValue(role, out var right);
            if (!leftExists)
            {
                AddChange(diff, "added", $"styleRoles.{role}", null, right!.StyleId, $"Role '{role}' was added.");
                continue;
            }

            if (!rightExists)
            {
                AddChange(diff, "removed", $"styleRoles.{role}", left!.StyleId, null, $"Role '{role}' was removed.");
                continue;
            }

            AddScalarChange(diff, $"styleRoles.{role}.styleId", left!.StyleId, right!.StyleId);
            AddScalarChange(diff, $"styleRoles.{role}.confidence", left.Confidence, right.Confidence);
            AddScalarChange(diff, $"styleRoles.{role}.evidenceCount", left.Evidence.Count, right.Evidence.Count);
        }
    }

    private static void AddTableChanges(
        ProfileDiff diff,
        ProfileTablePolicy left,
        ProfileTablePolicy right,
        int leftArchetypeCount,
        int rightArchetypeCount)
    {
        AddScalarChange(diff, "tablePolicy.detected", left.Detected, right.Detected);
        AddScalarChange(diff, "tablePolicy.tableCount", left.TableCount, right.TableCount);
        AddScalarChange(diff, "tablePolicy.observedColumnCounts", Join(left.ObservedColumnCounts), Join(right.ObservedColumnCounts));
        AddScalarChange(diff, "tablePolicy.default.hasFormat", left.Default?.Format is not null, right.Default?.Format is not null);
        AddScalarChange(diff, "tableArchetypes.count", leftArchetypeCount, rightArchetypeCount);
    }

    private static void AddDiagnosticChanges(ProfileDiff diff, List<ProfileDiagnostic> leftDiagnostics, List<ProfileDiagnostic> rightDiagnostics)
    {
        var leftCodes = leftDiagnostics.Select(diagnostic => diagnostic.Code).ToHashSet(StringComparer.Ordinal);
        var rightCodes = rightDiagnostics.Select(diagnostic => diagnostic.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var code in leftCodes.Union(rightCodes, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var leftExists = leftCodes.Contains(code);
            var rightExists = rightCodes.Contains(code);
            if (!leftExists)
            {
                AddChange(diff, "added", $"diagnostics.{code}", null, code, $"Diagnostic '{code}' was added.");
            }
            else if (!rightExists)
            {
                AddChange(diff, "removed", $"diagnostics.{code}", code, null, $"Diagnostic '{code}' was removed.");
            }
        }
    }

    private static void AddScalarChange(ProfileDiff diff, string path, object? left, object? right)
    {
        var leftValue = Normalize(left);
        var rightValue = Normalize(right);
        if (string.Equals(leftValue, rightValue, StringComparison.Ordinal))
        {
            return;
        }

        AddChange(diff, "modified", path, leftValue, rightValue, $"{path} changed.");
    }

    private static void AddChange(ProfileDiff diff, string kind, string path, string? left, string? right, string message)
    {
        diff.Changes.Add(new ProfileDiffChange
        {
            Kind = kind,
            Path = path,
            Left = left,
            Right = right,
            Message = message
        });
    }

    private static string? Normalize(object? value)
    {
        return value switch
        {
            null => null,
            bool boolean => boolean ? "true" : "false",
            double number => number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            float number => number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static string Join(IEnumerable<int> values)
    {
        return string.Join(",", values);
    }
}
