using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal sealed partial class OpenXmlTargetResolver
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
            "runText" => ResolveRunText(targetObject, options),
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

    private TargetResolutionResult ResolveRunText(JsonObject target, RunOptions options)
    {
        var text = GetString(target, "text", out var textError);
        if (textError is not null || text is null)
        {
            return TargetResolutionResult.Error(textError ?? "target_value_invalid");
        }

        var match = GetString(target, "match", out var matchError) ?? "contains";
        if (matchError is not null)
        {
            return TargetResolutionResult.Error(matchError);
        }

        if (match is not "exact" and not "contains" and not "regex")
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        var paragraphIndex = GetInt(target, "paragraphIndex", out var paragraphIndexError);
        if (paragraphIndexError is not null)
        {
            return TargetResolutionResult.Error(paragraphIndexError);
        }

        if (paragraphIndex is not null && (paragraphIndex < 0 || paragraphIndex >= Paragraphs.Count))
        {
            return TargetResolutionResult.Error("target_not_found");
        }

        var paragraphCandidates = paragraphIndex is null
            ? Paragraphs.Select((paragraph, index) => (Paragraph: paragraph, Index: index))
            : [(Paragraph: Paragraphs[paragraphIndex.Value], Index: paragraphIndex.Value)];

        List<ResolvedTarget> matches;
        try
        {
            matches = paragraphCandidates
                .SelectMany(candidate => candidate.Paragraph.Descendants<Run>()
                    .Select((run, runIndex) => (candidate.Index, Run: run, RunIndex: runIndex)))
                .Where(candidate => ParagraphTextMatches(candidate.Run.InnerText, text, match))
                .Select(candidate => (ResolvedTarget)new ResolvedRunTarget(candidate.Run, candidate.Index, candidate.RunIndex))
                .ToList();
        }
        catch (ArgumentException)
        {
            return TargetResolutionResult.Error("target_value_invalid");
        }

        return ValidateMatchCount(matches, options);
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

}
