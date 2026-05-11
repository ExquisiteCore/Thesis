internal static partial class Program
{
    static void CliRunExecuteInsertsDeletesAndMovesParagraphs()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "insert-after-title",
                  "op": "insertParagraph",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "text": "新增说明",
                  "format": {
                    "position": "after",
                    "styleId": "Normal",
                    "alignment": "center",
                    "bold": true,
                    "fontSizeHalfPoints": "24",
                    "eastAsiaFont": "黑体"
                  }
                },
                {
                  "id": "move-intro-after-insert",
                  "op": "moveParagraph",
                  "target": { "type": "paragraphText", "text": "第一章 绪论", "match": "exact" },
                  "format": {
                    "anchor": { "type": "paragraphText", "text": "新增说明", "match": "exact" },
                    "position": "after"
                  }
                },
                {
                  "id": "delete-list-item",
                  "op": "deleteParagraph",
                  "target": { "type": "paragraphText", "text": "列表项", "match": "exact" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(3, result.Operations.Count);
        foreach (var operation in result.Operations)
        {
            AssertEqual("applied", operation.Status);
        }

        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("中文摘要", map.Paragraphs[0].Text);
        AssertEqual("新增说明", map.Paragraphs[1].Text);
        AssertEqual("Normal", map.Paragraphs[1].StyleId);
        AssertEqual("center", map.Paragraphs[1].Format.Alignment);
        AssertEqual(true, map.Paragraphs[1].Runs[0].Bold);
        AssertEqual("24", map.Paragraphs[1].Runs[0].FontSizeHalfPoints);
        AssertEqual("黑体", map.Paragraphs[1].Runs[0].EastAsiaFont);
        AssertEqual("第一章 绪论", map.Paragraphs[2].Text);
        AssertEqual(false, map.Paragraphs.Any(paragraph => paragraph.Text == "列表项"));
    }

    static void CliRunDryRunPreviewsInsertParagraphWithoutChangingDocx()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "insert-before-title",
                  "op": "insertParagraph",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "text": "封面标题",
                  "format": {
                    "position": "before",
                    "styleId": "Title"
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("preview", result.Operations[0].Status);
        AssertEqual("中文摘要", result.Operations[0].Matches[0].PreviewBefore);
        AssertEqual("封面标题", result.Operations[0].Matches[0].PreviewAfter);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunExecuteInsertsAndDeletesTableRows()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "insert-row",
                  "op": "insertTableRow",
                  "target": { "type": "tableIndex", "index": 0 },
                  "format": {
                    "rowIndex": 1,
                    "position": "after",
                    "cells": ["A-new", "B-new"]
                  }
                },
                {
                  "id": "delete-first-row",
                  "op": "deleteTableRow",
                  "target": { "type": "tableIndex", "index": 0 },
                  "format": {
                    "rowIndex": 0
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        AssertEqual("applied", result.Operations[1].Status);

        var table = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument).Tables[0];
        AssertEqual(2, table.RowCount);
        AssertEqual(2, table.CellCounts[0]);
        AssertContains(table.TextPreview, "A2");
        AssertContains(table.TextPreview, "A-new");
        AssertEqual(false, table.TextPreview.Contains("A1", StringComparison.Ordinal));
    }

    static void CliRunExecuteInsertsImageParagraph()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var imagePath = Path.Combine(temp.Path, "pixel.png");
        File.WriteAllBytes(
            imagePath,
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            $$"""
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "insert-image",
                  "op": "insertImage",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "format": {
                    "position": "after",
                    "imagePath": "{{imagePath.Replace("\\", "\\\\")}}",
                    "widthEmu": 914400,
                    "heightEmu": 914400,
                    "altText": "单像素图片",
                    "alignment": "center"
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);

        using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(context.Paths.WorkingDocument, false);
        AssertEqual(true, document.MainDocumentPart!.ImageParts.Any());
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("中文摘要", map.Paragraphs[0].Text);
        AssertEqual("", map.Paragraphs[1].Text);
        AssertEqual("center", map.Paragraphs[1].Format.Alignment);
    }

    static void CliRunRejectsInvalidStructureOperations()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false,
                "stopOnError": false
              },
              "operations": [
                {
                  "id": "move-self",
                  "op": "moveParagraph",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "format": {
                    "anchor": { "type": "paragraphIndex", "index": 0 },
                    "position": "after"
                  }
                },
                {
                  "id": "too-many-cells",
                  "op": "insertTableRow",
                  "target": { "type": "tableIndex", "index": 0 },
                  "format": {
                    "rowIndex": 0,
                    "cells": ["A", "B", "C"]
                  }
                },
                {
                  "id": "missing-image",
                  "op": "insertImage",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "format": {
                    "imagePath": "missing.png",
                    "widthEmu": 914400,
                    "heightEmu": 914400
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("target_value_invalid", result.Operations[0].Reason);
        AssertEqual("table_cell_count_invalid", result.Operations[1].Reason);
        AssertEqual("image_not_found", result.Operations[2].Reason);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunRejectsDeletingOnlyTableRow()
    {
        using var temp = new TempDirectory();
        var sourceDoc = Path.Combine(temp.Path, "source.docx");
        WriteSimpleDocx(
            sourceDoc,
            """
                <w:tbl>
                  <w:tr><w:tc><w:p><w:r><w:t>only row</w:t></w:r></w:p></w:tc></w:tr>
                </w:tbl>
            """);
        var profile = Path.Combine(temp.Path, "input-profile.json");
        File.WriteAllText(profile, "{}");
        var workspace = Path.Combine(temp.Path, ".thesis");
        AssertEqual("success", SessionInitializer.Initialize(sourceDoc, profile, workspace).Status);
        var context = new WorkspaceContext(
            sourceDoc,
            profile,
            Path.GetFullPath(workspace),
            SessionPaths.FromWorkspace(workspace),
            File.ReadAllBytes(sourceDoc));
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "delete-only-row",
                  "op": "deleteTableRow",
                  "target": { "type": "tableIndex", "index": 0 },
                  "format": { "rowIndex": 0 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("table_row_count_invalid", result.Operations[0].Reason);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }
}
