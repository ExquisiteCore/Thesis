internal static partial class Program
{
    static void CliRunWriteBlockInsertsRoleFormattedParagraph()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithBodyFormat(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false,
                "requireSingleMatch": true
              },
              "operations": [
                {
                  "id": "write-body",
                  "op": "writeBlock",
                  "role": "body",
                  "target": { "type": "paragraphText", "text": "第一章 绪论", "match": "exact" },
                  "text": "新增正文段落"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("第一章 绪论", map.Paragraphs[1].Text);
        AssertEqual("新增正文段落", map.Paragraphs[2].Text);
        AssertEqual("Normal", map.Paragraphs[2].Format.StyleId);
        AssertEqual("both", map.Paragraphs[2].Format.Alignment);
        AssertEqual("360", map.Paragraphs[2].Format.LineSpacing);
        AssertEqual("auto", map.Paragraphs[2].Format.LineSpacingRule);
        AssertEqual(480, map.Paragraphs[2].Format.FirstLineIndentTwips);
        var runFormat = map.Paragraphs[2].Format.RunFormat ?? throw new UnreachableException("Expected run format.");
        AssertEqual("24", runFormat.FontSizeHalfPoints);
        AssertEqual("宋体", runFormat.EastAsiaFont);
    }

    static void CliRunWriteBlockFormatOverridesProfileValues()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithBodyFormat(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false,
                "requireSingleMatch": true
              },
              "operations": [
                {
                  "id": "write-overridden-body",
                  "op": "writeBlock",
                  "role": "body",
                  "target": { "type": "paragraphText", "text": "第一章 绪论", "match": "exact" },
                  "text": "覆盖格式正文段落",
                  "format": {
                    "position": "after",
                    "alignment": "center",
                    "fontSizeHalfPoints": "26",
                    "eastAsiaFont": "黑体",
                    "firstLineIndentTwips": 0
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual("覆盖格式正文段落", map.Paragraphs[2].Text);
        AssertEqual("Normal", map.Paragraphs[2].Format.StyleId);
        AssertEqual("center", map.Paragraphs[2].Format.Alignment);
        AssertEqual("360", map.Paragraphs[2].Format.LineSpacing);
        AssertEqual(0, map.Paragraphs[2].Format.FirstLineIndentTwips);
        var runFormat = map.Paragraphs[2].Format.RunFormat ?? throw new UnreachableException("Expected run format.");
        AssertEqual("26", runFormat.FontSizeHalfPoints);
        AssertEqual("黑体", runFormat.EastAsiaFont);
    }

    static void CliRunWriteBlockCanReplaceTemplatePlaceholder()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithBodyFormat(context);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false,
                "requireSingleMatch": true
              },
              "operations": [
                {
                  "id": "replace-placeholder",
                  "op": "writeBlock",
                  "role": "body",
                  "target": { "type": "paragraphText", "text": "第一章 绪论", "match": "exact" },
                  "text": "替换后的正文占位段落",
                  "format": { "position": "replace" }
                }
              ]
            }
            """);

        var before = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        var map = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument);
        AssertEqual(before.Paragraphs.Count, map.Paragraphs.Count);
        AssertEqual("替换后的正文占位段落", map.Paragraphs[1].Text);
        AssertEqual("列表项", map.Paragraphs[2].Text);
        AssertEqual("both", map.Paragraphs[1].Format.Alignment);
        AssertEqual(480, map.Paragraphs[1].Format.FirstLineIndentTwips);
        var runFormat = map.Paragraphs[1].Format.RunFormat ?? throw new UnreachableException("Expected run format.");
        AssertEqual("24", runFormat.FontSizeHalfPoints);
        AssertEqual("宋体", runFormat.EastAsiaFont);
    }

    static void CliRunWriteBlockRequiresRoleFormat()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        WriteProfileWithAbstractFormat(context);
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
                "requireSingleMatch": true
              },
              "operations": [
                {
                  "id": "write-missing-role",
                  "op": "writeBlock",
                  "role": "body",
                  "target": { "type": "paragraphText", "text": "第一章 绪论", "match": "exact" },
                  "text": "不应写入的正文"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("role_not_found", result.Operations[0].Reason);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }
}
