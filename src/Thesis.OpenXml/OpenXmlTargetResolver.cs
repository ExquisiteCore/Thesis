using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal sealed class OpenXmlTargetResolver
{
    private readonly TemplateProfile? _profile;
    private readonly JsonObject? _profileOverrides;
    private readonly IReadOnlyDictionary<string, int> _styleOutlineLevels;
    private readonly List<Table> _tables;

    public OpenXmlTargetResolver(
        Body body,
        TemplateProfile? profile,
        JsonObject? profileOverrides,
        IReadOnlyDictionary<string, int>? styleOutlineLevels = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        _profile = profile;
        _profileOverrides = profileOverrides;
        _styleOutlineLevels = styleOutlineLevels ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
            "paragraphId" => ResolveParagraphId(targetObject),
            "headingPath" => ResolveHeadingPath(targetObject, options),
            "within" => ResolveWithin(targetObject, options),
            "format" => ResolveFormat(targetObject, options),
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

    private TargetResolutionResult ResolveParagraphId(JsonObject target)
    {
        var id = GetString(target, "id", out var idError);
        if (idError is not null || string.IsNullOrWhiteSpace(id) || !id.StartsWith('p'))
        {
            return TargetResolutionResult.Error(idError ?? "target_value_invalid");
        }

        return int.TryParse(id[1..], out var index)
            ? ResolveParagraphIndex(new JsonObject { ["index"] = index })
            : TargetResolutionResult.Error("target_value_invalid");
    }

    private TargetResolutionResult ResolveHeadingPath(JsonObject target, RunOptions options)
    {
        var pathNode = target["path"];
        if (pathNode is not JsonArray path || path.Count == 0)
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        var last = path.Last();
        if (last is null)
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        string text;
        try
        {
            text = last.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }
        catch (FormatException)
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        var matches = Paragraphs
            .Select((paragraph, index) => (Paragraph: paragraph, Index: index))
            .Where(candidate => string.Equals(candidate.Paragraph.InnerText, text, StringComparison.Ordinal))
            .Select(candidate => (ResolvedTarget)new ResolvedParagraphTarget(candidate.Paragraph, candidate.Index))
            .ToList();
        return ValidateMatchCount(matches, options);
    }

    private TargetResolutionResult ResolveWithin(JsonObject target, RunOptions options)
    {
        var scopeNode = target["scope"];
        var targetNode = target["target"];
        if (scopeNode is null || targetNode is null)
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        var scope = Resolve(scopeNode, new RunOptions { RequireSingleMatch = false, CreateSnapshot = false, StopOnError = true });
        if (!scope.Success)
        {
            return TargetResolutionResult.Error(scope.ErrorCode!);
        }

        var allowed = scope.Matches
            .OfType<ResolvedParagraphTarget>()
            .Select(match => match.ParagraphIndex)
            .ToHashSet();
        if (allowed.Count == 0)
        {
            return TargetResolutionResult.Error("target_not_found");
        }

        var inner = Resolve(targetNode, new RunOptions { RequireSingleMatch = false, CreateSnapshot = false, StopOnError = true });
        if (!inner.Success)
        {
            return TargetResolutionResult.Error(inner.ErrorCode!);
        }

        var matches = inner.Matches
            .OfType<ResolvedParagraphTarget>()
            .Where(match => allowed.Contains(match.ParagraphIndex))
            .Cast<ResolvedTarget>()
            .ToList();
        return ValidateMatchCount(matches, options);
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

    private TargetResolutionResult ResolveFormat(JsonObject target, RunOptions options)
    {
        var formatNode = target["format"];
        if (formatNode is not JsonObject format)
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        var match = CreateFormatMatch(format, out var error);
        if (error is not null)
        {
            return TargetResolutionResult.Error(error);
        }

        var matches = Paragraphs
            .Select((paragraph, index) => (Paragraph: paragraph, Index: index))
            .Where(candidate => FormatMatches(candidate.Paragraph, match))
            .Select(candidate => (ResolvedTarget)new ResolvedParagraphTarget(candidate.Paragraph, candidate.Index))
            .ToList();
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
            return ResolveRolePolicyOrError(resolvedRole, position, offset.Value, options, "role_not_found");
        }

        var anchorIndices = GetRoleAnchorIndices(profileRoles);
        if (anchorIndices.Count == 0)
        {
            return ResolveRolePolicyOrError(resolvedRole, position, offset.Value, options, "target_not_found");
        }

        var matches = anchorIndices
            .Select(index => ApplyRolePosition(index, position, offset.Value))
            .Where(index => index >= 0 && index < Paragraphs.Count)
            .Distinct()
            .Select(index => (ResolvedTarget)new ResolvedParagraphTarget(Paragraphs[index], index))
            .ToList();

        return ValidateMatchCount(matches, options);
    }

    private TargetResolutionResult ResolveRolePolicyOrError(
        string role,
        string position,
        int offset,
        RunOptions options,
        string fallbackError)
    {
        var policyAnchorIndices = ResolveRolePolicyAnchorIndices(role, out var policyError);
        if (policyError is not null)
        {
            return TargetResolutionResult.Error(policyError);
        }

        if (policyAnchorIndices is null)
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

            policyAnchorIndices = clusterAnchorIndices;
        }

        var matches = policyAnchorIndices
            .Select(index => ApplyRolePosition(index, position, offset))
            .Where(index => index >= 0 && index < Paragraphs.Count)
            .Distinct()
            .Select(index => (ResolvedTarget)new ResolvedParagraphTarget(Paragraphs[index], index))
            .ToList();

        return ValidateMatchCount(matches, options);
    }

    private List<int>? ResolveRolePolicyAnchorIndices(string role, out string? error)
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
                string.Equals(cluster.RoleHint, role, StringComparison.OrdinalIgnoreCase)
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

    private bool RolePolicyMatches(Paragraph paragraph, ProfileRolePolicy policy)
    {
        var match = policy.Match;
        return StyleMatches(paragraph, match.StyleIds)
            && TextPatternMatches(paragraph, match.TextPatterns)
            && OutlineLevelMatches(paragraph, match.OutlineLevels)
            && FormatMatches(paragraph, match.Format);
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

    private bool OutlineLevelMatches(Paragraph paragraph, List<int> outlineLevels)
    {
        if (outlineLevels.Count == 0)
        {
            return true;
        }

        var outlineLevel = ReadOutlineLevel(paragraph);
        return outlineLevel is not null && outlineLevels.Contains(outlineLevel.Value);
    }

    private int? ReadOutlineLevel(Paragraph paragraph)
    {
        var directOutlineLevel = paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (directOutlineLevel is not null)
        {
            return directOutlineLevel;
        }

        var styleId = GetParagraphStyleId(paragraph);
        return styleId is not null && _styleOutlineLevels.TryGetValue(styleId, out var styleOutlineLevel)
            ? styleOutlineLevel
            : null;
    }

    private static bool FormatMatches(Paragraph paragraph, ProfileRoleFormatMatch? match)
    {
        if (match is null)
        {
            return true;
        }

        var properties = paragraph.ParagraphProperties;
        var spacing = properties?.SpacingBetweenLines;
        var indentation = properties?.Indentation;
        var runFormat = ReadFirstTextRunFormat(paragraph);

        return StringMatches(GetParagraphStyleId(paragraph), match.StyleId)
            && StringMatches(LowerInnerText(properties?.Justification?.Val), match.Alignment)
            && StringMatches(runFormat.FontSizeHalfPoints, match.FontSizeHalfPoints)
            && BoolMatches(runFormat.Bold, match.Bold)
            && BoolMatches(runFormat.Italic, match.Italic)
            && StringMatches(spacing?.Line?.Value, match.LineSpacing)
            && StringMatches(LowerInnerText(spacing?.LineRule), match.LineSpacingRule)
            && RangeMatches(ReadIndentTwips(indentation?.FirstLine), match.FirstLineIndentTwips)
            && RangeMatches(ReadIndentTwips(indentation?.Left), match.LeftIndentTwips)
            && RangeMatches(ReadIndentTwips(indentation?.Right), match.RightIndentTwips);
    }

    private static ProfileRoleFormatMatch? CreateFormatMatch(JsonObject format, out string? error)
    {
        error = null;
        return new ProfileRoleFormatMatch
        {
            StyleId = GetString(format, "styleId", out error),
            Alignment = error is null ? GetString(format, "alignment", out error) : null,
            FontSizeHalfPoints = error is null ? GetString(format, "fontSizeHalfPoints", out error) : null,
            Bold = error is null ? GetBool(format, "bold", out error) : null,
            Italic = error is null ? GetBool(format, "italic", out error) : null,
            LineSpacing = error is null ? GetString(format, "lineSpacing", out error) : null,
            LineSpacingRule = error is null ? GetString(format, "lineSpacingRule", out error) : null,
            FirstLineIndentTwips = error is null ? CreateRange(format["firstLineIndentTwips"], out error) : null,
            LeftIndentTwips = error is null ? CreateRange(format["leftIndentTwips"], out error) : null,
            RightIndentTwips = error is null ? CreateRange(format["rightIndentTwips"], out error) : null
        };
    }

    private static IntRangeMatch? CreateRange(JsonNode? node, out string? error)
    {
        error = null;
        if (node is null)
        {
            return null;
        }

        try
        {
            if (node is JsonValue)
            {
                return new IntRangeMatch { Exact = node.GetValue<int>() };
            }

            if (node is not JsonObject obj)
            {
                error = "target_value_invalid";
                return null;
            }

            return new IntRangeMatch
            {
                Exact = GetInt(obj, "exact", out error),
                Min = error is null ? GetInt(obj, "min", out error) : null,
                Max = error is null ? GetInt(obj, "max", out error) : null
            };
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

    private static RunFormatFacts ReadFirstTextRunFormat(Paragraph paragraph)
    {
        var properties = paragraph
            .Descendants<Run>()
            .FirstOrDefault(run => !string.IsNullOrWhiteSpace(run.InnerText))
            ?.RunProperties;

        return new RunFormatFacts(
            ReadOnOffValue(properties?.Bold) == true,
            ReadOnOffValue(properties?.Italic) == true,
            properties?.FontSize?.Val?.Value ?? properties?.FontSizeComplexScript?.Val?.Value);
    }

    private static bool StringMatches(string? actual, string? expected)
    {
        return expected is null
            || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool BoolMatches(bool actual, bool? expected)
    {
        return expected is null || actual == expected.Value;
    }

    private static bool RangeMatches(int? actual, IntRangeMatch? expected)
    {
        if (expected is null)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        if (expected.Exact is not null)
        {
            return actual.Value == expected.Exact.Value;
        }

        if (expected.Min is not null && actual.Value < expected.Min.Value)
        {
            return false;
        }

        return expected.Max is null || actual.Value <= expected.Max.Value;
    }

    private static bool? ReadOnOffValue(OnOffType? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Val?.Value ?? true;
    }

    private static string? LowerInnerText(OpenXmlSimpleType? value)
    {
        return string.IsNullOrWhiteSpace(value?.InnerText)
            ? null
            : value.InnerText.ToLowerInvariant();
    }

    private static int? ToInt(StringValue? value)
    {
        return int.TryParse(value?.Value, out var result) ? result : null;
    }

    private static int ReadIndentTwips(StringValue? value)
    {
        return ToInt(value) ?? 0;
    }

    private readonly record struct RunFormatFacts(bool Bold, bool Italic, string? FontSizeHalfPoints);

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
