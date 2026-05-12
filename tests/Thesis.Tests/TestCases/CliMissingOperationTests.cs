internal static partial class Program
{
    static void CliRunSupportsRunTextTargetForSetRunFormat()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-run-text",
              "mode": "execute",
              "options": { "createSnapshot": false },
              "operations": [
                {
                  "id": "format-run-text",
                  "op": "setRunFormat",
                  "target": { "type": "runText", "text": "中文摘要", "match": "exact" },
                  "format": { "bold": true, "fontSizeHalfPoints": "30" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        AssertEqual("run", result.Operations[0].Matches[0].Type);
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual(true, map.Paragraphs[0].Runs[0].Bold);
        AssertEqual("30", map.Paragraphs[0].Runs[0].FontSizeHalfPoints);
    }

    static void CliRunExecutesTextLevelOperations()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-text-ops",
              "mode": "execute",
              "options": { "createSnapshot": false },
              "operations": [
                {
                  "id": "replace-text",
                  "op": "replaceText",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "text": "绪论与背景",
                  "format": { "find": "绪论" }
                },
                {
                  "id": "insert-before",
                  "op": "insertTextBefore",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "text": "第1章 ",
                  "format": { "find": "第一章" }
                },
                {
                  "id": "insert-after",
                  "op": "insertTextAfter",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "text": "（修订）",
                  "format": { "find": "背景" }
                },
                {
                  "id": "replace-regex",
                  "op": "replaceRegex",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "text": "第1章",
                  "format": { "pattern": "第1章\\s+第一章" }
                },
                {
                  "id": "delete-text",
                  "op": "deleteText",
                  "target": { "type": "paragraphIndex", "index": 1 },
                  "format": { "find": "（修订）" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.Operations.All(operation => operation.Status == "applied"));
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("第1章 绪论与背景", map.Paragraphs[1].Text);
    }

    static void CliRunExecutesTableStructureOperations()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-table-structure",
              "mode": "execute",
              "options": { "createSnapshot": false },
              "operations": [
                {
                  "id": "insert-table",
                  "op": "insertTable",
                  "target": { "type": "paragraphIndex", "index": 6 },
                  "format": {
                    "position": "after",
                    "rows": [
                      ["H1", "H2"],
                      ["C1", "C2"]
                    ]
                  }
                },
                {
                  "id": "merge-cells",
                  "op": "mergeCells",
                  "target": { "type": "tableIndex", "index": 1 },
                  "format": { "rowIndex": 0, "startCellIndex": 0, "endCellIndex": 1 }
                },
                {
                  "id": "split-cell",
                  "op": "splitCell",
                  "target": { "type": "tableCell", "tableIndex": 1, "rowIndex": 0, "cellIndex": 0 },
                  "format": { "cellCount": 2, "texts": ["H1", "H2"] }
                },
                {
                  "id": "delete-table",
                  "op": "deleteTable",
                  "target": { "type": "tableIndex", "index": 0 }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.Operations.All(operation => operation.Status == "applied"));
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual(1, map.Tables.Count);
        AssertEqual(2, map.Tables[0].RowCount);
        AssertEqual(2, map.Tables[0].CellCounts[0]);
        AssertContains(map.Tables[0].TextPreview, "H1");
        AssertContains(map.Tables[0].TextPreview, "C2");
    }

    static void CliRunExecutesFieldAndCleanupOperations()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var profile = Path.Combine(temp.Path, "profile.json");
        var workspace = Path.Combine(temp.Path, ".thesis");
        WriteSimpleDocx(
            docx,
            """
            <w:p><w:r><w:t>目录</w:t></w:r></w:p>
            <w:p><w:r><w:t>正文  ，  含  多余  空格</w:t></w:r></w:p>
            <w:p><w:r><w:br w:type="page"/></w:r></w:p>
            <w:p><w:r><w:br w:type="page"/></w:r></w:p>
            <w:p><w:r><w:t>第二章</w:t></w:r></w:p>
            <w:p><w:r><w:t>第一章</w:t></w:r></w:p>
            """);
        File.WriteAllText(profile, "{}");
        AssertEqual("success", SessionInitializer.Initialize(docx, profile, workspace).Status);
        var paths = SessionPaths.FromWorkspace(workspace);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-fields-cleanup",
              "mode": "execute",
              "options": { "createSnapshot": false },
              "operations": [
                {
                  "id": "insert-toc",
                  "op": "insertTocField",
                  "target": { "type": "paragraphText", "text": "目录", "match": "exact" },
                  "format": { "position": "after", "levels": "1-3" }
                },
                { "id": "mark-fields", "op": "markTocNeedsUpdate" },
                { "id": "simple-fields", "op": "updateSimpleFields" },
                {
                  "id": "normalize-spaces",
                  "op": "removeExtraSpaces",
                  "target": { "type": "paragraphText", "text": "多余", "match": "contains" }
                },
                {
                  "id": "punctuation",
                  "op": "normalizeChinesePunctuationSpacing",
                  "target": { "type": "paragraphText", "text": "多余", "match": "contains" }
                },
                { "id": "page-breaks", "op": "removeDuplicatePageBreaks" },
                {
                  "id": "role-order",
                  "op": "ensureRoleOrder",
                  "format": { "order": ["第一章", "第二章"] }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.Operations.All(operation => operation.Status == "applied"));
        var map = OpenXmlDocumentInspector.Inspect(paths.WorkingDocument);
        AssertEqual(true, map.RequiresFinalization);
        AssertEqual(true, map.FinalizationReasons.Contains("toc"));
        AssertEqual("正文，含 多余 空格", map.Paragraphs.First(paragraph => paragraph.Text.Contains("多余", StringComparison.Ordinal)).Text);
        var chapterTexts = map.Paragraphs
            .Where(paragraph => paragraph.Text is "第一章" or "第二章")
            .Select(paragraph => paragraph.Text)
            .ToList();
        AssertEqual("第一章", chapterTexts[0]);
        AssertEqual("第二章", chapterTexts[1]);
    }

    static void CliRunExecutesReferenceOperations()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-reference-ops",
              "mode": "execute",
              "options": { "createSnapshot": false },
              "operations": [
                {
                  "id": "replace-refs",
                  "op": "replaceReferences",
                  "target": { "type": "paragraphText", "text": "参考文献", "match": "exact" },
                  "format": { "items": ["作者A. 题名A[J]. 期刊, 2024.", "作者B. 题名B[M]. 北京: 出版社, 2025."] }
                },
                {
                  "id": "insert-ref",
                  "op": "insertReferenceItem",
                  "target": { "type": "paragraphText", "text": "作者B", "match": "contains" },
                  "text": "作者C. 题名C[D]. 学校, 2026.",
                  "format": { "position": "after" }
                },
                {
                  "id": "format-refs",
                  "op": "applyReferenceFormat",
                  "target": { "type": "sectionRange", "start": { "type": "paragraphText", "text": "参考文献", "match": "exact" }, "includeStart": false }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.Operations.All(operation => operation.Status == "applied"));
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text.StartsWith("[1] 作者A", StringComparison.Ordinal)));
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text.StartsWith("[2] 作者B", StringComparison.Ordinal)));
        AssertEqual(true, map.Paragraphs.Any(paragraph => paragraph.Text.StartsWith("[3] 作者C", StringComparison.Ordinal)));
        var reference = map.Paragraphs.First(paragraph => paragraph.Text.StartsWith("[1]", StringComparison.Ordinal));
        AssertEqual(420, reference.Format.FirstLineIndentTwips);
    }
}
