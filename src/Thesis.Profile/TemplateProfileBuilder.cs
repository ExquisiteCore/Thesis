using System.Text.Json.Nodes;
using Thesis.Schema;

namespace Thesis.Profile;

public static class TemplateProfileBuilder
{
    public static JsonObject Build(DocumentMap map, string sourceType)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);

        return new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["profileKind"] = "templateProfile",
            ["sourceType"] = sourceType,
            ["sourceDocument"] = map.Path,
            ["requiresFinalization"] = map.RequiresFinalization,
            ["finalizationReasons"] = ToJsonArray(map.FinalizationReasons),
            ["pageSetup"] = BuildPageSetup(map),
            ["styleRoles"] = BuildStyleRoles(map),
            ["numberingPolicy"] = BuildNumberingPolicy(map),
            ["tablePolicy"] = BuildTablePolicy(map),
            ["sourceEvidence"] = BuildSourceEvidence(map)
        };
    }

    private static JsonObject BuildPageSetup(DocumentMap map)
    {
        var section = map.Sections.FirstOrDefault();
        return new JsonObject
        {
            ["pageSize"] = section?.PageSize is null
                ? null
                : new JsonObject
                {
                    ["widthTwips"] = section.PageSize.WidthTwips,
                    ["heightTwips"] = section.PageSize.HeightTwips,
                    ["orientation"] = section.PageSize.Orientation
                },
            ["margins"] = section?.PageMargin is null
                ? null
                : new JsonObject
                {
                    ["topTwips"] = section.PageMargin.TopTwips,
                    ["rightTwips"] = section.PageMargin.RightTwips,
                    ["bottomTwips"] = section.PageMargin.BottomTwips,
                    ["leftTwips"] = section.PageMargin.LeftTwips,
                    ["headerTwips"] = section.PageMargin.HeaderTwips,
                    ["footerTwips"] = section.PageMargin.FooterTwips,
                    ["gutterTwips"] = section.PageMargin.GutterTwips
                },
            ["headers"] = ToJsonArray(section?.Headers.Select(header => new JsonObject
            {
                ["type"] = header.Type,
                ["relationshipId"] = header.RelationshipId
            }) ?? []),
            ["footers"] = ToJsonArray(section?.Footers.Select(footer => new JsonObject
            {
                ["type"] = footer.Type,
                ["relationshipId"] = footer.RelationshipId
            }) ?? [])
        };
    }

    private static JsonArray BuildStyleRoles(DocumentMap map)
    {
        var roles = new List<JsonObject>();
        AddRole(roles, map, "title", "Title");
        AddRole(roles, map, "heading1", "Heading1");
        AddRole(roles, map, "normal", "Normal");
        return ToJsonArray(roles);
    }

    private static void AddRole(List<JsonObject> roles, DocumentMap map, string role, string styleId)
    {
        var style = map.Styles.FirstOrDefault(candidate =>
            string.Equals(candidate.StyleId, styleId, StringComparison.OrdinalIgnoreCase));
        if (style is null)
        {
            return;
        }

        var evidence = map.Paragraphs
            .Where(paragraph => string.Equals(paragraph.StyleId, style.StyleId, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(paragraph => new JsonObject
            {
                ["paragraphIndex"] = paragraph.Index,
                ["textPreview"] = Preview(paragraph.Text)
            });

        roles.Add(new JsonObject
        {
            ["role"] = role,
            ["styleId"] = style.StyleId,
            ["name"] = style.Name,
            ["type"] = style.Type,
            ["basedOn"] = style.BasedOn,
            ["confidence"] = map.Paragraphs.Any(paragraph => string.Equals(paragraph.StyleId, style.StyleId, StringComparison.OrdinalIgnoreCase))
                ? 0.9
                : 0.55,
            ["evidence"] = ToJsonArray(evidence)
        });
    }

    private static JsonObject BuildTablePolicy(DocumentMap map)
    {
        var observedColumnCounts = map.Tables
            .SelectMany(table => table.CellCounts)
            .Where(count => count > 0)
            .Distinct()
            .OrderBy(count => count)
            .ToArray();

        return new JsonObject
        {
            ["detected"] = map.Tables.Count > 0,
            ["tableCount"] = map.Tables.Count,
            ["observedColumnCounts"] = ToJsonArray(observedColumnCounts),
            ["default"] = map.Tables.FirstOrDefault() is null
                ? null
                : new JsonObject
                {
                    ["rowCount"] = map.Tables[0].RowCount,
                    ["cellCounts"] = ToJsonArray(map.Tables[0].CellCounts),
                    ["textPreview"] = map.Tables[0].TextPreview
                }
        };
    }

    private static JsonObject BuildNumberingPolicy(DocumentMap map)
    {
        return new JsonObject
        {
            ["detected"] = map.Numbering.Count > 0 || map.Paragraphs.Any(paragraph => paragraph.Numbering is not null),
            ["instances"] = ToJsonArray(map.Numbering.Select(numbering => new JsonObject
            {
                ["numberingId"] = numbering.NumberingId,
                ["abstractNumberingId"] = numbering.AbstractNumberingId,
                ["levels"] = ToJsonArray(numbering.Levels.Select(level => new JsonObject
                {
                    ["level"] = level.Level,
                    ["format"] = level.Format,
                    ["text"] = level.Text
                }))
            })),
            ["paragraphUses"] = ToJsonArray(map.Paragraphs
                .Where(paragraph => paragraph.Numbering is not null)
                .Take(10)
                .Select(paragraph => new JsonObject
                {
                    ["paragraphIndex"] = paragraph.Index,
                    ["numberingId"] = paragraph.Numbering!.NumberingId,
                    ["level"] = paragraph.Numbering.Level,
                    ["textPreview"] = Preview(paragraph.Text)
                }))
        };
    }


    private static JsonObject BuildSourceEvidence(DocumentMap map)
    {
        return new JsonObject
        {
            ["paragraphCount"] = map.Paragraphs.Count,
            ["styleCount"] = map.Styles.Count,
            ["numberingCount"] = map.Numbering.Count,
            ["sectionCount"] = map.Sections.Count,
            ["tableCount"] = map.Tables.Count,
            ["paragraphSamples"] = ToJsonArray(map.Paragraphs.Take(5).Select(paragraph => new JsonObject
            {
                ["index"] = paragraph.Index,
                ["styleId"] = paragraph.StyleId,
                ["textPreview"] = Preview(paragraph.Text)
            }))
        };
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonArray ToJsonArray(IEnumerable<int> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonArray ToJsonArray(IEnumerable<JsonObject> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static string Preview(string text)
    {
        return text.Length <= 80 ? text : text[..80];
    }
}
