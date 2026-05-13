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
        Item("setParagraphFormat", "Apply paragraph and run formatting to matched paragraphs.", ["paragraphIndex", "paragraphId", "paragraphText", "headingPath", "within", "format", "styleId", "role", "sectionRange"], ["target"], optionalFormat: ["styleId", "alignment", "spacingBeforeTwips", "spacingAfterTwips", "lineSpacing", "lineSpacingRule", "firstLineIndentTwips", "bold", "italic", "fontSizeHalfPoints", "eastAsiaFont"]),
        Item("copyParagraphFormat", "Copy paragraph formatting from a source paragraph target.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], requiredFormat: ["source"]),
        Item("clearDirectFormatting", "Clear direct paragraph and/or run formatting from matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target"], optionalFormat: ["scope"]),
        Item("setPageBreakBefore", "Set or clear paragraph page-break-before.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], optionalFormat: ["value"]),
        Item("setRunFormat", "Apply run-level formatting to one matched run.", ["runIndex", "runText"], ["target"], optionalFormat: ["bold", "italic", "fontSizeHalfPoints"]),
        Item("replaceText", "Replace text inside matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target", "text"], requiredFormat: ["find"]),
        Item("replaceRegex", "Replace regex matches inside matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target", "text"], requiredFormat: ["pattern"]),
        Item("insertTextBefore", "Insert text before a matched substring inside matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target", "text"], requiredFormat: ["find"]),
        Item("insertTextAfter", "Insert text after a matched substring inside matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target", "text"], requiredFormat: ["find"]),
        Item("deleteText", "Delete text inside matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target"], requiredFormat: ["find"]),
        Item("writeBlock", "Insert or replace text as a semantic thesis block using profile role formatting; operation format overrides profile defaults.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target", "role", "text"], optionalFormat: ["position(before|after|replace)", "styleId", "alignment", "spacingBeforeTwips", "spacingAfterTwips", "lineSpacing", "lineSpacingRule", "firstLineIndentTwips", "leftIndentTwips", "rightIndentTwips", "bold", "italic", "fontSizeHalfPoints", "asciiFont", "highAnsiFont", "eastAsiaFont", "complexScriptFont"], profileRequired: true),
        Item("insertParagraph", "Insert a paragraph before or after a matched paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target", "text"], optionalFormat: ["position", "styleId", "alignment", "runFormat", "spacing"]),
        Item("deleteParagraph", "Delete matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target"]),
        Item("moveParagraph", "Move matched paragraphs before or after an anchor paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], requiredFormat: ["anchor"], optionalFormat: ["position"]),
        Item("insertPageBreak", "Insert an explicit page break paragraph before or after a matched paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], optionalFormat: ["position"]),
        Item("addBookmark", "Add a named bookmark around a matched paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], requiredFormat: ["name"]),
        Item("addFootnote", "Append a footnote reference to a matched paragraph and create the footnote body.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target", "text"]),
        Item("applyProfilePageSetup", "Apply page size and margins from the active template profile.", [], [], optionalFormat: ["widthTwips", "heightTwips", "orientation", "topTwips", "rightTwips", "bottomTwips", "leftTwips"], profileRequired: true),
        Item("applyProfileRole", "Apply formatting from a template profile role to matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target", "role"], optionalFormat: ["styleId", "alignment", "runFormat", "spacing"], profileRequired: true),
        Item("applyProfileTable", "Apply table formatting from the template profile default or archetype.", ["tableIndex"], ["target"], optionalFormat: ["archetype"], profileRequired: true),
        Item("setTableBorders", "Set table border lines.", ["tableIndex"], ["target"], optionalFormat: ["borders"]),
        Item("setTableCellText", "Replace text in one table cell.", ["tableCell"], ["target", "text"]),
        Item("setTableCellFormat", "Apply paragraph formatting to one table cell.", ["tableCell"], ["target"], optionalFormat: ["styleId", "alignment", "spacingBeforeTwips", "spacingAfterTwips", "lineSpacing", "lineSpacingRule", "firstLineIndentTwips", "bold", "italic", "fontSizeHalfPoints", "eastAsiaFont"]),
        Item("setTableColumnWidth", "Set a table grid column width.", ["tableIndex"], ["target"], requiredFormat: ["columnIndex", "widthTwips"]),
        Item("setTableRowHeader", "Mark or unmark a table row as a repeating header.", ["tableIndex"], ["target"], requiredFormat: ["rowIndex"], optionalFormat: ["header"]),
        Item("insertTableRow", "Insert a table row before or after an existing row.", ["tableIndex"], ["target"], requiredFormat: ["rowIndex"], optionalFormat: ["position", "cells"]),
        Item("deleteTableRow", "Delete a table row.", ["tableIndex"], ["target"], requiredFormat: ["rowIndex"]),
        Item("insertTableColumn", "Insert a table column before or after an existing column.", ["tableIndex"], ["target"], requiredFormat: ["columnIndex"], optionalFormat: ["position", "widthTwips", "cells"]),
        Item("deleteTableColumn", "Delete a table column.", ["tableIndex"], ["target"], requiredFormat: ["columnIndex"]),
        Item("applyThreeLineTable", "Apply academic three-line table borders.", ["tableIndex"], ["target"]),
        Item("insertTable", "Insert a simple table before or after a paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], requiredFormat: ["rows"], optionalFormat: ["position"]),
        Item("deleteTable", "Delete matched tables.", ["tableIndex"], ["target"]),
        Item("mergeCells", "Merge a contiguous cell range in one table row.", ["tableIndex"], ["target"], requiredFormat: ["rowIndex", "startCellIndex", "endCellIndex"]),
        Item("splitCell", "Split one table cell into multiple cells.", ["tableCell"], ["target"], requiredFormat: ["cellCount"], optionalFormat: ["texts"]),
        Item("insertImage", "Insert an inline image paragraph before or after a matched paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], requiredFormat: ["imagePath", "widthEmu", "heightEmu"], optionalFormat: ["position", "altText", "alignment"]),
        Item("insertCaption", "Insert a thesis figure or table caption near a paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target", "text"], optionalFormat: ["position", "styleId", "alignment"]),
        Item("insertTocField", "Insert a TOC field near a matched paragraph.", ["paragraphIndex", "paragraphText", "styleId", "role"], ["target"], optionalFormat: ["position", "levels"]),
        Item("markTocNeedsUpdate", "Mark fields and TOC as needing host update.", [], []),
        Item("updateSimpleFields", "Clear dirty flags on simple fields where OpenXML can safely do so.", [], []),
        Item("normalizeRuns", "Collapse simple paragraph text runs into one run.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target"]),
        Item("removeExtraSpaces", "Collapse repeated spaces inside matched paragraphs.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target"]),
        Item("normalizeChinesePunctuationSpacing", "Remove extra spaces around Chinese punctuation.", ["paragraphIndex", "paragraphText", "styleId", "role", "sectionRange"], ["target"]),
        Item("removeDuplicatePageBreaks", "Remove adjacent duplicate page-break paragraphs.", [], []),
        Item("ensureRoleOrder", "Move exact-match paragraphs into a requested order.", [], [], requiredFormat: ["order"]),
        Item("setHeaderFooterText", "Set section header or footer text.", [], ["text"], optionalFormat: ["kind", "type"]),
        Item("insertPageNumber", "Insert a PAGE field in a header or footer.", [], [], optionalFormat: ["kind", "type", "alignment"]),
        Item("replaceReferences", "Replace reference items after a references heading.", ["paragraphIndex", "paragraphText", "role"], ["target"], requiredFormat: ["items"]),
        Item("insertReferenceItem", "Insert one numbered reference item near a matched reference paragraph.", ["paragraphIndex", "paragraphText", "role"], ["target", "text"], optionalFormat: ["position"]),
        Item("applyReferenceFormat", "Apply basic thesis reference paragraph formatting.", ["paragraphIndex", "paragraphText", "sectionRange"], ["target"]),
        Item("normalizeReferences", "Insert a basic numbered reference placeholder after a references heading when missing.", ["paragraphIndex", "paragraphText", "role"], ["target"], optionalFormat: ["position"])
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
            "setParagraphFormat" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("alignment", "center"), ("fontSizeHalfPoints", "28"), ("bold", true)))),
            "copyParagraphFormat" => Request(op, Operation(op, target: ParagraphIndexTarget(1), format: Obj(("source", ParagraphIndexTarget(0))))),
            "clearDirectFormatting" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("scope", "paragraphAndRuns")))),
            "setPageBreakBefore" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("value", true)))),
            "setRunFormat" => Request(op, Operation(op, target: Obj(("type", "runText"), ("text", "关键词")), format: Obj(("bold", true), ("fontSizeHalfPoints", "24")))),
            "replaceText" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "新文本", format: Obj(("find", "旧文本")))),
            "replaceRegex" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "第一章", format: Obj(("pattern", "^第1章")))),
            "insertTextBefore" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "前缀", format: Obj(("find", "标题")))),
            "insertTextAfter" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "后缀", format: Obj(("find", "标题")))),
            "deleteText" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("find", "多余文本")))),
            "writeBlock" => Request(
                op,
                Operation(
                    op,
                    target: Obj(("type", "paragraphText"), ("text", "第一章 绪论"), ("match", "exact")),
                    text: "工业控制系统是关键基础设施中的重要组成部分。",
                    format: Obj(("position", "after"), ("fontSizeHalfPoints", "24"), ("eastAsiaFont", "宋体")),
                    role: "body")),
            "insertParagraph" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "新增段落", format: Obj(("position", "after")))),
            "deleteParagraph" => Request(op, Operation(op, target: ParagraphIndexTarget(1))),
            "moveParagraph" => Request(op, Operation(op, target: ParagraphIndexTarget(2), format: Obj(("position", "after"), ("anchor", ParagraphIndexTarget(0))))),
            "insertPageBreak" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("position", "after")))),
            "addBookmark" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("name", "abstract_anchor")))),
            "addFootnote" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "脚注内容")),
            "applyProfilePageSetup" => Request(op, Operation(op, format: Obj(("topTwips", 1440), ("bottomTwips", 1440)))),
            "applyProfileRole" => Request(op, Operation(op, role: "body", target: ParagraphIndexTarget(1))),
            "applyProfileTable" => Request(op, Operation(op, target: TableIndexTarget(0))),
            "setTableBorders" => Request(
                op,
                Operation(
                    op,
                    target: TableIndexTarget(0),
                    format: Obj(("borders", Obj(("top", Obj(("value", "single"), ("size", "12"), ("color", "000000")))))))),
            "setTableCellText" => Request(op, Operation(op, target: TableCellTarget(0, 0, 0), text: "单元格文本")),
            "setTableCellFormat" => Request(op, Operation(op, target: TableCellTarget(0, 0, 0), format: Obj(("alignment", "center"), ("bold", true)))),
            "setTableColumnWidth" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("columnIndex", 0), ("widthTwips", 2400)))),
            "setTableRowHeader" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("rowIndex", 0), ("header", true)))),
            "insertTableRow" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("rowIndex", 0), ("position", "after"), ("cells", Array("第一列", "第二列"))))),
            "deleteTableRow" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("rowIndex", 1)))),
            "insertTableColumn" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("columnIndex", 0), ("position", "after"), ("widthTwips", 2400), ("cells", Array("表头", "数据"))))),
            "deleteTableColumn" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("columnIndex", 1)))),
            "applyThreeLineTable" => Request(op, Operation(op, target: TableIndexTarget(0))),
            "insertTable" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("position", "after"), ("rows", Rows(("A1", "B1"), ("A2", "B2")))))),
            "deleteTable" => Request(op, Operation(op, target: TableIndexTarget(0))),
            "mergeCells" => Request(op, Operation(op, target: TableIndexTarget(0), format: Obj(("rowIndex", 0), ("startCellIndex", 0), ("endCellIndex", 1)))),
            "splitCell" => Request(op, Operation(op, target: TableCellTarget(0, 0, 0), format: Obj(("cellCount", 2), ("texts", Array("第一列", "第二列"))))),
            "insertImage" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("imagePath", "figure.png"), ("position", "after"), ("widthEmu", 3600000), ("heightEmu", 2400000)))),
            "insertCaption" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "图1-1 系统结构图", format: Obj(("position", "after"), ("alignment", "center")))),
            "insertTocField" => Request(op, Operation(op, target: ParagraphIndexTarget(0), format: Obj(("position", "after"), ("levels", "1-3")))),
            "markTocNeedsUpdate" => Request(op, Operation(op)),
            "updateSimpleFields" => Request(op, Operation(op)),
            "normalizeRuns" => Request(op, Operation(op, target: ParagraphIndexTarget(0))),
            "removeExtraSpaces" => Request(op, Operation(op, target: ParagraphIndexTarget(0))),
            "normalizeChinesePunctuationSpacing" => Request(op, Operation(op, target: ParagraphIndexTarget(0))),
            "removeDuplicatePageBreaks" => Request(op, Operation(op)),
            "ensureRoleOrder" => Request(op, Operation(op, format: Obj(("order", Array("摘要", "Abstract"))))),
            "setHeaderFooterText" => Request(op, Operation(op, text: "论文题目", format: Obj(("kind", "header"), ("type", "default")))),
            "insertPageNumber" => Request(op, Operation(op, format: Obj(("kind", "footer"), ("type", "default"), ("alignment", "center")))),
            "replaceReferences" => Request(op, Operation(op, target: Obj(("type", "paragraphText"), ("text", "参考文献"), ("match", "exact")), format: Obj(("items", Array("作者. 题名[J]. 期刊, 2024."))))),
            "insertReferenceItem" => Request(op, Operation(op, target: ParagraphIndexTarget(0), text: "作者. 题名[M]. 北京: 出版社, 2025.")),
            "applyReferenceFormat" => Request(op, Operation(op, target: ParagraphIndexTarget(0))),
            "normalizeReferences" => Request(op, Operation(op, target: Obj(("type", "paragraphText"), ("text", "参考文献"), ("match", "exact")), format: Obj(("position", "afterHeading")))),
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

    private static JsonArray Rows(params (string First, string Second)[] rows)
    {
        var array = new JsonArray();
        foreach (var row in rows)
        {
            array.Add(Array(row.First, row.Second));
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
