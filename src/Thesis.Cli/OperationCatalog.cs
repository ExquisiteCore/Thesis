using System.Text.Json.Nodes;
using Thesis.Schema;

namespace Thesis.Cli;

internal static class OperationCatalog
{
    private static readonly OperationCatalogItem[] Items =
    [
        Item("resolveTarget", "Preview target resolution without editing the document.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange", "tableIndex", "tableCell"], ["target"]),
        Item("replaceParagraphText", "Replace the complete text of matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target", "text"]),
        Item("setParagraphStyle", "Apply an existing paragraph style to matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target"], requiredFormat: ["styleId"]),
        Item("setRunFormat", "Apply run-level formatting to one matched run.", ["runText"], ["target"], optionalFormat: ["bold", "italic", "fontSizeHalfPoints"]),
        Item("insertParagraph", "Insert a paragraph before or after a matched paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target", "text"], optionalFormat: ["position", "styleId", "alignment", "runFormat", "spacing"]),
        Item("deleteParagraph", "Delete matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target"]),
        Item("moveParagraph", "Move matched paragraphs before or after an anchor paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], requiredFormat: ["anchor"], optionalFormat: ["position"]),
        Item("insertPageBreak", "Insert an explicit page break paragraph before or after a matched paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], optionalFormat: ["position"]),
        Item("addBookmark", "Add a named bookmark around a matched paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], requiredFormat: ["name"]),
        Item("addFootnote", "Append a footnote reference to a matched paragraph and create the footnote body.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target", "text"]),
        Item("applyProfilePageSetup", "Apply page size and margins from the active template profile.", [], [], optionalFormat: ["widthTwips", "heightTwips", "orientation", "topTwips", "rightTwips", "bottomTwips", "leftTwips"], profileRequired: true),
        Item("applyProfileRole", "Apply formatting from a template profile role to matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target", "role"], optionalFormat: ["styleId", "alignment", "runFormat", "spacing"], profileRequired: true),
        Item("applyProfileTable", "Apply table formatting from the template profile default or archetype.", ["tableIndex"], ["target"], optionalFormat: ["archetype"], profileRequired: true),
        Item("setTableBorders", "Set table border lines.", ["tableIndex"], ["target"], optionalFormat: ["top", "bottom", "left", "right", "insideHorizontal", "insideVertical"]),
        Item("setTableCellText", "Replace text in one table cell.", ["tableCell"], ["target", "text"]),
        Item("setTableCellFormat", "Apply paragraph or cell formatting to one table cell.", ["tableCell"], ["target"], optionalFormat: ["paragraph", "shadingFill", "verticalAlignment"]),
        Item("setTableColumnWidth", "Set a table grid column width.", ["tableIndex"], ["target"], requiredFormat: ["columnIndex", "widthTwips"]),
        Item("setTableRowHeader", "Mark or unmark a table row as a repeating header.", ["tableIndex"], ["target"], requiredFormat: ["rowIndex"], optionalFormat: ["header"]),
        Item("insertTableRow", "Insert a table row before or after an existing row.", ["tableIndex"], ["target"], requiredFormat: ["rowIndex"], optionalFormat: ["position", "cells"]),
        Item("deleteTableRow", "Delete a table row.", ["tableIndex"], ["target"], requiredFormat: ["rowIndex"]),
        Item("insertTableColumn", "Insert a table column before or after an existing column.", ["tableIndex"], ["target"], requiredFormat: ["columnIndex"], optionalFormat: ["position", "widthTwips", "cells"]),
        Item("deleteTableColumn", "Delete a table column.", ["tableIndex"], ["target"], requiredFormat: ["columnIndex"]),
        Item("applyThreeLineTable", "Apply academic three-line table borders.", ["tableIndex"], ["target"]),
        Item("insertImage", "Insert an inline image paragraph before or after a matched paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], requiredFormat: ["imagePath"], optionalFormat: ["position", "widthEmu", "heightEmu", "altText", "alignment"])
    ];

    public static List<OperationCatalogItem> List()
    {
        return Items.Select(Clone).ToList();
    }

    public static OperationRequest? CreateSample(string op)
    {
        return string.IsNullOrWhiteSpace(op) ? null : op switch
        {
            "resolveTarget" => Request(op, Operation(op, target: ParagraphIndexTarget(0))),
            "replaceParagraphText" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "替换后的段落")),
            "setParagraphStyle" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("styleId", "BodyText")))),
            "setRunFormat" => Request(op, Operation(op, target: Obj(("type", "runText"), ("text", "关键词")), format: Obj(("bold", true), ("fontSizeHalfPoints", "24")))),
            "insertParagraph" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "新增段落", format: Obj(("position", "after")))),
            "deleteParagraph" => Request(op, Operation(op, target: ParagraphIndexTarget(1))),
            "moveParagraph" => Request(op, Operation(op, target: ParagraphIndexTarget(2), format: Obj(("position", "after"), ("anchor", ParagraphIndexTarget(0))))),
            "insertPageBreak" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("position", "after")))),
            "addBookmark" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("name", "abstract_anchor")))),
            "addFootnote" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "脚注内容")),
            "applyProfilePageSetup" => Request(op, Operation(op, format: Obj(("topTwips", 1440), ("bottomTwips", 1440)))),
            "applyProfileRole" => Request(op, Operation(op, role: "body", target: ParagraphIndexTarget(1))),
            "applyProfileTable" => Request(op, Operation(op, target: TableIndexTarget(0))),
            "setTableBorders" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("top", Obj(("value", "single"), ("size", "12"), ("color", "000000")))))),
            "setTableCellText" => Request(op, Operation(op, target: TableCellTarget(0, 0, 0), text: "单元格文本")),
            "setTableCellFormat" => Request(op, Operation(op, target: TableCellTarget(0, 0, 0), format: Obj(("shadingFill", "F2F2F2")))),
            "setTableColumnWidth" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("columnIndex", 0), ("widthTwips", 2400)))),
            "setTableRowHeader" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("rowIndex", 0), ("header", true)))),
            "insertTableRow" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("rowIndex", 0), ("position", "after"), ("cells", Array("第一列", "第二列"))))),
            "deleteTableRow" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("rowIndex", 1)))),
            "insertTableColumn" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("columnIndex", 0), ("position", "after"), ("widthTwips", 2400), ("cells", Array("表头", "数据"))))),
            "deleteTableColumn" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("columnIndex", 1)))),
            "applyThreeLineTable" => Request(op, Operation(op, target: TableIndexTarget(0))),
            "insertImage" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("imagePath", "figure.png"), ("position", "after"), ("widthEmu", 3600000), ("heightEmu", 2400000)))),
            _ => null
        };
    }

    private static OperationCatalogItem Item(
        string op,
        string description,
        string[] targetTypes,
        string[] requiredFields,
        string[]? optionalFields = null,
        string[]? requiredFormat = null,
        string[]? optionalFormat = null,
        bool profileRequired = false)
    {
        return new OperationCatalogItem
        {
            Op = op,
            Description = description,
            TargetTypes = [.. targetTypes],
            RequiredFields = [.. requiredFields],
            OptionalFields = optionalFields is null ? [] : [.. optionalFields],
            RequiredFormat = requiredFormat is null ? [] : [.. requiredFormat],
            OptionalFormat = optionalFormat is null ? [] : [.. optionalFormat],
            ProfileRequired = profileRequired
        };
    }

    private static OperationCatalogItem Clone(OperationCatalogItem item)
    {
        return new OperationCatalogItem
        {
            Op = item.Op,
            Description = item.Description,
            TargetTypes = [.. item.TargetTypes],
            RequiredFields = [.. item.RequiredFields],
            OptionalFields = [.. item.OptionalFields],
            RequiredFormat = [.. item.RequiredFormat],
            OptionalFormat = [.. item.OptionalFormat],
            ProfileRequired = item.ProfileRequired
        };
    }

    private static OperationRequest Request(string op, ThesisOperation operation)
    {
        return new OperationRequest
        {
            RequestId = $"example-{op}",
            Mode = RequestMode.DryRun,
            Options = new RunOptions
            {
                CreateSnapshot = false,
                StopOnError = true,
                RequireSingleMatch = false,
                TrackChanges = false
            },
            Operations = [operation]
        };
    }

    private static ThesisOperation Operation(
        string op,
        JsonNode? target = null,
        string? text = null,
        JsonNode? format = null,
        string? role = null)
    {
        return new ThesisOperation
        {
            Id = $"sample-{op}",
            Op = op,
            Role = role,
            Target = target,
            Text = text,
            Format = format
        };
    }

    private static JsonObject ParagraphIndexTarget(int index)
    {
        return Obj(("type", "paragraphIndex"), ("index", index));
    }

    private static JsonObject TableIndexTarget(int index)
    {
        return Obj(("type", "tableIndex"), ("index", index));
    }

    private static JsonObject TableCellTarget(int tableIndex, int rowIndex, int cellIndex)
    {
        return Obj(("type", "tableCell"), ("tableIndex", tableIndex), ("rowIndex", rowIndex), ("cellIndex", cellIndex));
    }

    private static JsonArray Array(params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonObject Obj(params (string Name, object? Value)[] properties)
    {
        var obj = new JsonObject();
        foreach (var (name, value) in properties)
        {
            obj[name] = value switch
            {
                null => null,
                JsonNode node => node.DeepClone(),
                string text => text,
                int number => number,
                bool boolean => boolean,
                _ => JsonValue.Create(value)
            };
        }

        return obj;
    }
}
