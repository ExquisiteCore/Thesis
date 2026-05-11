internal static partial class Program
{
    static void CliRunReadsRequestJsonAndReturnsSuccessJson()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-123",
              "mode": "dryRun",
              "operations": []
            }
            """);

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = ThesisCli.Run(
            ["run", "--workspace", context.Workspace, "--request", requestPath],
            output,
            error);

        AssertEqual(0, exitCode);
        var result = ThesisJson.Deserialize<CliResult>(output.ToString());
        AssertEqual("success", result.Status);
        AssertEqual("req-123", result.RequestId);
        AssertEqual(context.Workspace, result.Workspace);
        AssertEqual(context.Paths.WorkingDocument, result.Document);
    }

    static void CliRunDryRunPreviewsMicroEditsWithoutChangingDocx()
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
              "requestId": "req-dry-run",
              "mode": "dryRun",
              "options": {
                "requireSingleMatch": true
              },
              "operations": [
                {
                  "id": "replace-title",
                  "op": "replaceParagraphText",
                  "target": { "type": "paragraphText", "text": "中文摘要", "match": "exact" },
                  "text": "中文摘要（修改后）"
                },
                {
                  "id": "style-title",
                  "op": "setParagraphStyle",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "format": { "styleId": "Heading1" }
                },
                {
                  "id": "format-title-run",
                  "op": "setRunFormat",
                  "target": { "type": "runIndex", "paragraphIndex": 0, "runIndex": 0 },
                  "format": { "bold": true, "fontSizeHalfPoints": "32" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("req-dry-run", result.RequestId);
        AssertEqual(RequestMode.DryRun, result.Mode);
        AssertEqual(3, result.Operations.Count);
        AssertEqual("preview", result.Operations[0].Status);
        AssertEqual("中文摘要", result.Operations[0].Matches[0].PreviewBefore);
        AssertEqual("中文摘要（修改后）", result.Operations[0].Matches[0].PreviewAfter);
        AssertEqual("preview", result.Operations[1].Status);
        AssertEqual("paragraph", result.Operations[1].Matches[0].Type);
        AssertEqual("preview", result.Operations[2].Status);
        AssertEqual("run", result.Operations[2].Matches[0].Type);
        AssertEqual(null, result.Snapshot);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
        AssertBytesEqual(context.OriginalBytes, File.ReadAllBytes(context.SourceDoc));

        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("中文摘要", map.Paragraphs[0].Text);
        AssertEqual("Title", map.Paragraphs[0].StyleId);
        AssertEqual(false, map.Paragraphs[0].Runs[0].Bold);
        AssertEqual(null, map.Paragraphs[0].Runs[0].FontSizeHalfPoints);
    }

    static void CliRunExecuteCanReplaceMultipleParagraphTextMatches()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-multi",
              "mode": "execute",
              "options": {
                "createSnapshot": false,
                "requireSingleMatch": false
              },
              "operations": [
                {
                  "id": "replace-headings",
                  "op": "replaceParagraphText",
                  "target": { "type": "paragraphText", "text": "摘", "match": "contains" },
                  "text": "摘要标题"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(null, result.Snapshot);
        AssertEqual(1, result.Operations.Count);
        AssertEqual("applied", result.Operations[0].Status);
        AssertEqual(2, result.Operations[0].Matches.Count);

        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("摘要标题", map.Paragraphs[0].Text);
        AssertEqual("摘要标题", map.Paragraphs[3].Text);
        AssertBytesEqual(context.OriginalBytes, File.ReadAllBytes(context.SourceDoc));
    }

    static void CliRunExecuteAppliesMicroEditsAndCreatesSnapshot()
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
              "requestId": "req-execute",
              "mode": "execute",
              "options": {
                "createSnapshot": true,
                "requireSingleMatch": true
              },
              "operations": [
                {
                  "id": "replace-title",
                  "op": "replaceParagraphText",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "text": "中文摘要（修改后）"
                },
                {
                  "id": "style-title",
                  "op": "setParagraphStyle",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "format": { "styleId": "Heading1" }
                },
                {
                  "id": "format-title-run",
                  "op": "setRunFormat",
                  "target": { "type": "runIndex", "paragraphIndex": 0, "runIndex": 0 },
                  "format": { "bold": true, "fontSizeHalfPoints": "32" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(RequestMode.Execute, result.Mode);
        AssertEqual(3, result.Operations.Count);
        AssertEqual(true, result.Operations.All(operation => operation.Status == "applied"));
        AssertEqual("0002-before-run-req-execute", result.Snapshot!.Id);
        AssertEqual(true, result.Snapshot.Created);
        AssertBytesEqual(before, File.ReadAllBytes(result.Snapshot.Path!));
        AssertBytesEqual(context.OriginalBytes, File.ReadAllBytes(context.SourceDoc));

        var session = ThesisJson.Deserialize<SessionState>(File.ReadAllText(context.Paths.SessionJson));
        AssertEqual(2, session.SnapshotCounter);

        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("中文摘要（修改后）", map.Paragraphs[0].Text);
        AssertEqual("Heading1", map.Paragraphs[0].StyleId);
        AssertEqual(true, map.Paragraphs[0].Runs[0].Bold);
        AssertEqual("32", map.Paragraphs[0].Runs[0].FontSizeHalfPoints);
    }

    static void CliRunExecuteAbortsTransactionOnOperationError()
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
              "requestId": "req-abort",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "replace-title",
                  "op": "replaceParagraphText",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "text": "changed but not committed"
                },
                {
                  "id": "bad-style",
                  "op": "setParagraphStyle",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "format": { "styleId": "MissingStyle" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual(2, result.Operations.Count);
        AssertEqual("preview", result.Operations[0].Status);
        AssertEqual("error", result.Operations[1].Status);
        AssertEqual("paragraph_style_missing", result.Diagnostics[0].Code);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunWrongTypedTargetReturnsOperationDiagnostic()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-bad-target",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "bad-index",
                  "op": "replaceParagraphText",
                  "target": { "type": "paragraphIndex", "index": "zero" },
                  "text": "unused"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual(1, result.Operations.Count);
        AssertEqual("bad-index", result.Operations[0].Id);
        AssertEqual("error", result.Operations[0].Status);
        AssertEqual("target_value_invalid", result.Operations[0].Reason);
        AssertEqual("target_value_invalid", result.Diagnostics[0].Code);
    }

    static void CliRunRejectsInvalidRunFontSize()
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
              "requestId": "req-bad-size",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "bad-size",
                  "op": "setRunFormat",
                  "target": { "type": "runIndex", "paragraphIndex": 0, "runIndex": 0 },
                  "format": { "fontSizeHalfPoints": "large" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("font_size_invalid", result.Operations[0].Reason);
        AssertEqual("font_size_invalid", result.Diagnostics[0].Code);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunResolveTargetFindsParagraphsByStyleId()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-resolve-style",
              "mode": "dryRun",
              "options": {
                "requireSingleMatch": false
              },
              "operations": [
                {
                  "id": "find-heading1",
                  "op": "resolveTarget",
                  "target": { "type": "styleId", "styleId": "Heading1" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("preview", result.Operations[0].Status);
        AssertEqual(5, result.Operations[0].Matches.Count);
        AssertEqual("p1", result.Operations[0].Matches[0].Id);
        AssertEqual("paragraph", result.Operations[0].Matches[0].Type);
        AssertEqual("第一章 绪论", result.Operations[0].Matches[0].Preview);
    }

    static void CliRunResolveTargetIsReadOnlyInExecuteMode()
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
              "requestId": "req-resolve-execute",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "find-table",
                  "op": "resolveTarget",
                  "target": { "type": "tableIndex", "index": 0 }
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
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunExecuteResolveTargetDoesNotCreateDefaultSnapshot()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var sessionBefore = ThesisJson.Deserialize<SessionState>(File.ReadAllText(context.Paths.SessionJson));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-resolve-default-snapshot",
              "mode": "execute",
              "operations": [
                {
                  "id": "find-table",
                  "op": "resolveTarget",
                  "target": { "type": "tableIndex", "index": 0 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(null, result.Snapshot);
        var sessionAfter = ThesisJson.Deserialize<SessionState>(File.ReadAllText(context.Paths.SessionJson));
        AssertEqual(sessionBefore.SnapshotCounter, sessionAfter.SnapshotCounter);
        AssertEqual(1, Directory.EnumerateFiles(context.Paths.SnapshotsDirectory, "*.docx").Count());
    }

    static void CliRunParagraphOperationRejectsTableTarget()
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
              "requestId": "req-table-reject",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "bad-replace-table",
                  "op": "replaceParagraphText",
                  "target": { "type": "tableIndex", "index": 0 },
                  "text": "not allowed"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("error", result.Operations[0].Status);
        AssertEqual("target_type_unsupported", result.Operations[0].Reason);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

}
