internal static partial class Program
{
    static void CliRunDryRunPreviewsApplyProfileTableWithoutChangingDocx()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithTableFormat(context);
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
                  "id": "apply-table",
                  "op": "applyProfileTable",
                  "target": { "type": "tableIndex", "index": 0 },
                  "format": {
                    "widthTwips": 7200
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("preview", result.Operations[0].Status);
        AssertEqual("t0", result.Operations[0].Matches[0].Id);
        AssertEqual("table", result.Operations[0].Matches[0].Type);
        AssertContains(result.Operations[0].Matches[0].PreviewAfter!, "\"widthTwips\":7200");
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunExecuteAppliesProfileTableFormatting()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithTableFormat(context);
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
                  "id": "apply-table",
                  "op": "applyProfileTable",
                  "target": { "type": "tableIndex", "index": 0 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);

        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        var format = map.Tables[0].Format;
        AssertEqual(8640, format.WidthTwips);
        AssertEqual("dxa", format.WidthType);
        AssertEqual("center", format.Alignment);
        AssertEqual(2, format.GridColumnWidthsTwips.Count);
        AssertEqual(4320, format.GridColumnWidthsTwips[0]);
        AssertEqual(4320, format.GridColumnWidthsTwips[1]);
        AssertEqual("single", format.Borders!.Top!.Value);
        AssertEqual("12", format.Borders.Top.Size);
        AssertEqual("single", format.Borders.Bottom!.Value);
        AssertEqual("single", format.Borders.InsideHorizontal!.Value);
        AssertEqual("4", format.Borders.InsideHorizontal.Size);
        AssertEqual("nil", format.Borders.InsideVertical!.Value);
        AssertEqual(60, format.CellMargins!.TopTwips);
        AssertEqual(120, format.CellMargins.LeftTwips);
        AssertEqual(60, format.CellMargins.BottomTwips);
        AssertEqual(120, format.CellMargins.RightTwips);
        AssertEqual(1, format.HeaderRowCount);
        AssertEqual("center", format.FirstCellParagraphFormat!.Alignment);
        AssertEqual(true, format.FirstCellParagraphFormat.RunFormat!.Bold);
        AssertEqual("21", format.FirstCellParagraphFormat.RunFormat.FontSizeHalfPoints);
        AssertEqual("宋体", format.FirstCellParagraphFormat.RunFormat.EastAsiaFont);
    }

    static void CliRunApplyProfileTableReturnsFormatMissing()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            SourceType = "test",
            SourceDocument = context.SourceDoc,
            TablePolicy = new ProfileTablePolicy
            {
                Detected = true,
                TableCount = 1,
                ObservedColumnCounts = [2],
                Default = new ProfileTableSample { RowCount = 2, CellCounts = [2, 2], TextPreview = "A1 B1" }
            }
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "missing-table-format",
                  "op": "applyProfileTable",
                  "target": { "type": "tableIndex", "index": 0 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("profile_table_format_missing", result.Operations[0].Reason);
        AssertEqual("profile_table_format_missing", result.Diagnostics[0].Code);
    }

    static void CliRunExecuteAppliesTableMicroOperations()
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
                  "id": "cell-text",
                  "op": "setTableCellText",
                  "target": { "type": "tableCell", "tableIndex": 0, "rowIndex": 1, "cellIndex": 1 },
                  "text": "结果"
                },
                {
                  "id": "cell-format",
                  "op": "setTableCellFormat",
                  "target": { "type": "tableCell", "tableIndex": 0, "rowIndex": 0, "cellIndex": 0 },
                  "format": {
                    "alignment": "center",
                    "bold": true,
                    "fontSizeHalfPoints": "24",
                    "eastAsiaFont": "黑体"
                  }
                },
                {
                  "id": "column-width",
                  "op": "setTableColumnWidth",
                  "target": { "type": "tableIndex", "index": 0 },
                  "format": { "columnIndex": 0, "widthTwips": 4800 }
                },
                {
                  "id": "header-row",
                  "op": "setTableRowHeader",
                  "target": { "type": "tableIndex", "index": 0 },
                  "format": { "rowIndex": 0, "header": true }
                },
                {
                  "id": "table-borders",
                  "op": "setTableBorders",
                  "target": { "type": "tableIndex", "index": 0 },
                  "format": {
                    "borders": {
                      "top": { "value": "single", "size": "8", "color": "000000" },
                      "left": { "value": "nil" },
                      "bottom": { "value": "single", "size": "8", "color": "000000" },
                      "right": { "value": "nil" },
                      "insideHorizontal": { "value": "single", "size": "4", "color": "000000" },
                      "insideVertical": { "value": "nil" }
                    }
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(5, result.Operations.Count);
        foreach (var operation in result.Operations)
        {
            AssertEqual("applied", operation.Status);
        }

        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        var table = map.Tables[0];
        AssertContains(table.TextPreview, "结果");
        AssertEqual(4800, table.Format.GridColumnWidthsTwips[0]);
        AssertEqual(1, table.Format.HeaderRowCount);
        AssertEqual("single", table.Format.Borders!.Top!.Value);
        AssertEqual("8", table.Format.Borders.Top.Size);
        AssertEqual("nil", table.Format.Borders.Left!.Value);
        AssertEqual("single", table.Format.Borders.InsideHorizontal!.Value);
        AssertEqual("nil", table.Format.Borders.InsideVertical!.Value);
        AssertEqual("center", table.Format.FirstCellParagraphFormat!.Alignment);
        AssertEqual(true, table.Format.FirstCellParagraphFormat.RunFormat!.Bold);
        AssertEqual("24", table.Format.FirstCellParagraphFormat.RunFormat.FontSizeHalfPoints);
        AssertEqual("黑体", table.Format.FirstCellParagraphFormat.RunFormat.EastAsiaFont);
    }

    static void CliRunDryRunPreviewsTableCellTextWithoutChangingDocx()
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
                  "id": "cell-text",
                  "op": "setTableCellText",
                  "target": { "type": "tableCell", "tableIndex": 0, "rowIndex": 1, "cellIndex": 1 },
                  "text": "结果"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("preview", result.Operations[0].Status);
        AssertEqual("B2", result.Operations[0].Matches[0].PreviewBefore);
        AssertEqual("结果", result.Operations[0].Matches[0].PreviewAfter);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunTableBorderUpdateCanSetOneSideOnBareTable()
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
                  "id": "bottom-only",
                  "op": "setTableBorders",
                  "target": { "type": "tableIndex", "index": 0 },
                  "format": {
                    "borders": {
                      "bottom": { "value": "single", "size": "8", "color": "000000" }
                    }
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        var borders = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument).Tables[0].Format.Borders!;
        AssertEqual(null, borders.Top);
        AssertEqual(null, borders.Left);
        AssertEqual("single", borders.Bottom!.Value);
        AssertEqual("8", borders.Bottom.Size);
        AssertEqual(null, borders.Right);
        AssertEqual(null, borders.InsideHorizontal);
        AssertEqual(null, borders.InsideVertical);
    }

    static void CliRunTableBorderUpdatePreservesExistingSides()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithTableFormat(context);
        var applyProfileRequest = Path.Combine(temp.Path, "apply-profile.json");
        File.WriteAllText(
            applyProfileRequest,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "apply-table",
                  "op": "applyProfileTable",
                  "target": { "type": "tableIndex", "index": 0 }
                }
              ]
            }
            """);
        AssertEqual(0, RunCli(["run", "--workspace", context.Workspace, "--request", applyProfileRequest]).ExitCode);

        var updateRequest = Path.Combine(temp.Path, "update-border.json");
        File.WriteAllText(
            updateRequest,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "bottom-only",
                  "op": "setTableBorders",
                  "target": { "type": "tableIndex", "index": 0 },
                  "format": {
                    "borders": {
                      "bottom": { "value": "double", "size": "16", "color": "FF0000" }
                    }
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", updateRequest]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        var borders = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument).Tables[0].Format.Borders!;
        AssertEqual("single", borders.Top!.Value);
        AssertEqual("nil", borders.Left!.Value);
        AssertEqual("double", borders.Bottom!.Value);
        AssertEqual("16", borders.Bottom.Size);
        AssertEqual("FF0000", borders.Bottom.Color);
        AssertEqual("nil", borders.Right!.Value);
        AssertEqual("single", borders.InsideHorizontal!.Value);
        AssertEqual("nil", borders.InsideVertical!.Value);
    }

    static void CliRunApplyThreeLineTableSetsAcademicBorders()
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
                  "id": "three-line",
                  "op": "applyThreeLineTable",
                  "target": { "type": "tableIndex", "index": 0 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        var format = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument).Tables[0].Format;
        AssertEqual("single", format.Borders!.Top!.Value);
        AssertEqual("12", format.Borders.Top.Size);
        AssertEqual("nil", format.Borders.Left!.Value);
        AssertEqual("single", format.Borders.Bottom!.Value);
        AssertEqual("12", format.Borders.Bottom.Size);
        AssertEqual("nil", format.Borders.Right!.Value);
        AssertEqual("single", format.Borders.InsideHorizontal!.Value);
        AssertEqual("4", format.Borders.InsideHorizontal.Size);
        AssertEqual("nil", format.Borders.InsideVertical!.Value);
    }

    static void CliRunTableCellOperationRejectsTableTarget()
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
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "bad-cell-text",
                  "op": "setTableCellText",
                  "target": { "type": "tableIndex", "index": 0 },
                  "text": "not allowed"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("target_type_unsupported", result.Operations[0].Reason);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

}
