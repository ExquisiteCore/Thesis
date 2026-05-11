using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal sealed class OpenXmlTargetResolver
{
    private readonly TemplateProfile? _profile;
    private readonly JsonObject? _profileOverrides;
    private readonly List<Table> _tables;

    public OpenXmlTargetResolver(Body body, TemplateProfile? profile, JsonObject? profileOverrides)
    {
        ArgumentNullException.ThrowIfNull(body);

        _profile = profile;
        _profileOverrides = profileOverrides;
        _tables = body.Descendants<Table>().ToList();
        Paragraphs = SelectIndexedParagraphs(body);
    }

    public IReadOnlyList<Paragraph> Paragraphs { get; }

    internal TargetResolutionResult Resolve(JsonNode? target, RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var targetObject = GetTargetObject(target, out var objectError);
        if (objectError is not null)
        {
            return TargetResolutionResult.Error(objectError);
        }

        if (targetObject is null)
        {
            return TargetResolutionResult.Error("target_type_missing");
        }

        var type = GetString(targetObject, "type", out var typeError);
        if (typeError is not null)
        {
            return TargetResolutionResult.Error(typeError);
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            return TargetResolutionResult.Error("target_type_missing");
        }

        return type switch
        {
            "paragraphIndex" => ResolveParagraphIndex(targetObject),
            "paragraphText" => ResolveParagraphText(targetObject, options),
            "runIndex" => ResolveRunIndex(targetObject),
            "styleId" => ResolveStyleId(targetObject, options),
            "tableIndex" => ResolveTableIndex(targetObject),
            "tableCell" => ResolveTableCell(targetObject),
            "role" => ResolveRole(targetObject, options),
            "sectionRange" => ResolveSectionRange(targetObject, options),
            _ => TargetResolutionResult.Error("target_type_unsupported")
        };
    }

    private TargetResolutionResult ResolveParagraphIndex(JsonObject target)
    {
        var index = GetInt(target, "index", out var indexError);
        if (indexError is not null || index is null)
        {
            return TargetResolutionResult.Error(indexError ?? "target_value_invalid");
        }

        if (index < 0 || index >= Paragraphs.Count)
        {
            return TargetResolutionResult.Error("target_not_found");
        }

        return TargetResolutionResult.FromMatches(
            [new ResolvedParagraphTarget(Paragraphs[index.Value], index.Value)]);
    }

    private TargetResolutionResult ResolveParagraphText(JsonObject target, RunOptions options)
    {
        var text = GetString(target, "text", out var textError);
        if (textError is not null || text is null)
        {
            return TargetResolutionResult.Error(textError ?? "target_value_invalid");
        }

        var match = GetString(target, "match", out var matchError) ?? "exact";
        if (matchError is not null)
        {
            return TargetResolutionResult.Error(matchError);
        }

        if (match is not "exact" and not "contains" and not "regex")
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        List<ResolvedTarget> matches;
        try
        {
            matches = Paragraphs
                .Select((paragraph, index) => (Paragraph: paragraph, Index: index))
                .Where(candidate => ParagraphTextMatches(candidate.Paragraph.InnerText, text, match))
                .Select(candidate => (ResolvedTarget)new ResolvedParagraphTarget(candidate.Paragraph, candidate.Index))
                .ToList();
        }
        catch (ArgumentException)
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        return ValidateMatchCount(matches, options);
    }

    private TargetResolutionResult ResolveRunIndex(JsonObject target)
    {
        var paragraphIndex = GetInt(target, "paragraphIndex", out var paragraphIndexError);
        var runIndex = GetInt(target, "runIndex", out var runIndexError);
        if (paragraphIndexError is not null || runIndexError is not null || paragraphIndex is null || runIndex is null)
        {
            return TargetResolutionResult.Error(paragraphIndexError ?? runIndexError ?? "target_value_invalid");
        }

        if (paragraphIndex < 0 || paragraphIndex >= Paragraphs.Count)
        {
            return TargetResolutionResult.Error("target_not_found");
        }

        var runs = Paragraphs[paragraphIndex.Value].Descendants<Run>().ToList();
        if (runIndex < 0 || runIndex >= runs.Count)
        {
            return TargetResolutionResult.Error("target_not_found");
        }

        return TargetResolutionResult.FromMatches(
            [new ResolvedRunTarget(runs[runIndex.Value], paragraphIndex.Value, runIndex.Value)]);
    }

    private TargetResolutionResult ResolveStyleId(JsonObject target, RunOptions options)
    {
        var styleId = GetString(target, "styleId", out var styleIdError);
        if (styleIdError is not null || string.IsNullOrWhiteSpace(styleId))
        {
            return TargetResolutionResult.Error(styleIdError ?? "target_value_invalid");
        }

        var matches = Paragraphs
            .Select((paragraph, index) => (Paragraph: paragraph, Index: index))
            .Where(candidate => string.Equals(GetParagraphStyleId(candidate.Paragraph), styleId, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => (ResolvedTarget)new ResolvedParagraphTarget(candidate.Paragraph, candidate.Index))
            .ToList();

        return ValidateMatchCount(matches, options);
    }

    private TargetResolutionResult ResolveTableIndex(JsonObject target)
    {
        var index = GetInt(target, "index", out var indexError);
        if (indexError is not null || index is null)
        {
            return TargetResolutionResult.Error(indexError ?? "target_value_invalid");
        }

        if (index < 0 || index >= _tables.Count)
        {
            return TargetResolutionResult.Error("target_not_found");
        }

        return TargetResolutionResult.FromMatches([CreateTableTarget(_tables[index.Value], index.Value)]);
    }

    private TargetResolutionResult ResolveTableCell(JsonObject target)
    {
        var tableIndex = GetInt(target, "tableIndex", out var tableIndexError);
        var rowIndex = GetInt(target, "rowIndex", out var rowIndexError);
        var cellIndex = GetInt(target, "cellIndex", out var cellIndexError);
        if (tableIndexError is not null || rowIndexError is not null || cellIndexError is not null
            || tableIndex is null || rowIndex is null || cellIndex is null)
        {
            return TargetResolutionResult.Error(tableIndexError ?? rowIndexError ?? cellIndexError ?? "target_value_invalid");
        }

        if (tableIndex < 0 || tableIndex >= _tables.Count)
        {
            return TargetResolutionResult.Error("target_not_found");
        }

        var rows = _tables[tableIndex.Value].Elements<TableRow>().ToList();
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            return TargetResolutionResult.Error("target_not_found");
        }

        var cells = rows[rowIndex.Value].Elements<TableCell>().ToList();
        if (cellIndex < 0 || cellIndex >= cells.Count)
        {
            return TargetResolutionResult.Error("target_not_found");
        }

        return TargetResolutionResult.FromMatches(
            [new ResolvedTableCellTarget(cells[cellIndex.Value], tableIndex.Value, rowIndex.Value, cellIndex.Value)]);
    }

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

        var profileRoles = _profile?.StyleRoles
            .Where(candidate => string.Equals(candidate.Role, resolvedRole, StringComparison.Ordinal))
            .ToList();
        if (profileRoles is null || profileRoles.Count == 0)
        {
            return ResolveRolePolicyOrError(resolvedRole, options, "role_not_found");
        }

        var anchorIndices = GetRoleAnchorIndices(profileRoles);
        if (anchorIndices.Count == 0)
        {
            return ResolveRolePolicyOrError(resolvedRole, options, "target_not_found");
        }

        var matches = anchorIndices
            .Select(index => ApplyRolePosition(index, position, offset.Value))
            .Where(index => index >= 0 && index < Paragraphs.Count)
            .Distinct()
            .Select(index => (ResolvedTarget)new ResolvedParagraphTarget(Paragraphs[index], index))
            .ToList();

        return ValidateMatchCount(matches, options);
    }

    private TargetResolutionResult ResolveRolePolicyOrError(string role, RunOptions options, string fallbackError)
    {
        var policyMatches = ResolveRolePolicy(role, out var policyError);
        if (policyError is not null)
        {
            return TargetResolutionResult.Error(policyError);
        }

        return policyMatches is null
            ? TargetResolutionResult.Error(fallbackError)
            : ValidateMatchCount(policyMatches, options);
    }

    private List<ResolvedTarget>? ResolveRolePolicy(string role, out string? error)
    {
        error = null;
        var policies = _profile?.RolePolicies
            .Where(policy =>
                string.Equals(policy.Role, role, StringComparison.Ordinal)
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
                .Select(candidate => (ResolvedTarget)new ResolvedParagraphTarget(candidate.Paragraph, candidate.Index))
                .ToList();
        }
        catch (ArgumentException)
        {
            error = "target_value_invalid";
            return [];
        }
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

        if (!TryResolveRangeAnchor(target["end"], out var endIndex, out var endError))
        {
            return TargetResolutionResult.Error(endError);
        }

        if (startIndex > endIndex)
        {
            return TargetResolutionResult.Error("range_invalid");
        }

        var firstIndex = includeStart ? startIndex : startIndex + 1;
        var lastIndex = includeEnd ? endIndex : endIndex - 1;
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

    private List<int> GetRoleAnchorIndices(List<ProfileStyleRole> profileRoles)
    {
        var evidenceIndices = profileRoles
            .SelectMany(role => role.Evidence)
            .Select(evidence => evidence.ParagraphIndex)
            .Distinct()
            .ToList();
        if (evidenceIndices.Count > 0)
        {
            return evidenceIndices
                .Where(index => index >= 0 && index < Paragraphs.Count)
                .ToList();
        }

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

    private TargetResolutionResult ValidateMatchCount(List<ResolvedTarget> matches, RunOptions options)
    {
        if (matches.Count == 0)
        {
            return TargetResolutionResult.Error("target_not_found");
        }

        if (matches.Count > 1 && options.RequireSingleMatch)
        {
            return TargetResolutionResult.Error("target_ambiguous");
        }

        return TargetResolutionResult.FromMatches(matches);
    }

    private ResolvedTableTarget CreateTableTarget(Table table, int tableIndex)
    {
        var rows = table.Elements<TableRow>().ToList();
        return new ResolvedTableTarget(
            table,
            tableIndex,
            rows.Count,
            rows.Select(row => row.Elements<TableCell>().Count()).ToList());
    }

    private static int ApplyRolePosition(int anchorIndex, string position, int offset)
    {
        return position switch
        {
            "beforeHeading" => anchorIndex - offset,
            _ => anchorIndex + offset
        };
    }

    private static bool ParagraphTextMatches(string candidate, string text, string match)
    {
        return match switch
        {
            "contains" => candidate.Contains(text, StringComparison.Ordinal),
            "regex" => Regex.IsMatch(candidate, text, RegexOptions.CultureInvariant),
            _ => string.Equals(candidate, text, StringComparison.Ordinal)
        };
    }

    private static List<Paragraph> SelectIndexedParagraphs(Body body)
    {
        return body
            .Descendants<Paragraph>()
            .Where(paragraph => !paragraph.Ancestors<Table>().Any())
            .Where(paragraph => !IsFieldOnlyParagraph(paragraph))
            .ToList();
    }

    private static bool IsFieldOnlyParagraph(Paragraph paragraph)
    {
        var hasFields = paragraph.Descendants<FieldChar>().Any()
            || paragraph.Descendants<FieldCode>().Any()
            || paragraph.Descendants<SimpleField>().Any();
        return hasFields && !paragraph.Descendants<Text>().Any(text => !string.IsNullOrWhiteSpace(text.Text));
    }

    private static string? GetParagraphStyleId(Paragraph paragraph)
    {
        return paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
    }

    private static bool RolePolicyMatches(Paragraph paragraph, ProfileRolePolicy policy)
    {
        var match = policy.Match;
        return StyleMatches(paragraph, match.StyleIds)
            && TextPatternMatches(paragraph, match.TextPatterns)
            && OutlineLevelMatches(paragraph, match.OutlineLevels);
    }

    private static bool StyleMatches(Paragraph paragraph, List<string> styleIds)
    {
        if (styleIds.Count == 0)
        {
            return true;
        }

        var paragraphStyleId = GetParagraphStyleId(paragraph);
        return paragraphStyleId is not null
            && styleIds.Any(styleId => string.Equals(styleId, paragraphStyleId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TextPatternMatches(Paragraph paragraph, List<string> textPatterns)
    {
        if (textPatterns.Count == 0)
        {
            return true;
        }

        return textPatterns.Any(pattern => Regex.IsMatch(paragraph.InnerText, pattern, RegexOptions.CultureInvariant));
    }

    private static bool OutlineLevelMatches(Paragraph paragraph, List<int> outlineLevels)
    {
        if (outlineLevels.Count == 0)
        {
            return true;
        }

        var outlineLevel = ReadOutlineLevel(paragraph);
        return outlineLevel is not null && outlineLevels.Contains(outlineLevel.Value);
    }

    private static int? ReadOutlineLevel(Paragraph paragraph)
    {
        return paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
    }

    private static JsonObject? GetTargetObject(JsonNode? node, out string? error)
    {
        error = null;
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject targetObject)
        {
            return targetObject;
        }

        error = "target_value_invalid";
        return null;
    }

    private static string? GetString(JsonObject node, string propertyName, out string? error)
    {
        error = null;
        if (!node.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        return GetStringValue(value, out error);
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

    private static int? GetInt(JsonObject node, string propertyName, out string? error)
    {
        error = null;
        if (!node.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<int>();
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

    private static bool? GetBool(JsonObject node, string propertyName, out string? error)
    {
        error = null;
        if (!node.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<bool>();
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
