internal static partial class Program
{
    static void CliRunExecuteRefusesLockedWorkspace()
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
              "requestId": "req-locked",
              "mode": "execute",
              "operations": [
                {
                  "id": "replace-title",
                  "op": "replaceParagraphText",
                  "target": { "type": "paragraphIndex", "index": 0 },
                  "text": "locked"
                }
              ]
            }
            """);
        File.WriteAllText(context.Paths.LockFile, "locked");

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("workspace_locked", result.Diagnostics[0].Code);
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliUnknownCommandReturnsJsonError()
    {
        var output = new StringWriter();
        var exitCode = ThesisCli.Run(["bogus"], output, TextWriter.Null);

        AssertEqual(1, exitCode);
        var result = ThesisJson.Deserialize<CliResult>(output.ToString());
        AssertEqual("error", result.Status);
        AssertEqual("unknown_command", result.Diagnostics[0].Code);
    }

    static void CliOperationsListReturnsOperationMetadata()
    {
        var output = new StringWriter();
        var exitCode = ThesisCli.Run(["operations", "list"], output, TextWriter.Null);

        AssertEqual(0, exitCode);
        var result = ThesisJson.Deserialize<CliResult>(output.ToString());
        AssertEqual("success", result.Status);
        AssertEqual(true, result.OperationsCatalog.Any(operation =>
            operation.Op == "insertParagraph"
            && operation.TargetTypes.Contains("paragraphIndex")
            && operation.RequiredFields.Contains("text")));
        AssertEqual(true, result.OperationsCatalog.Any(operation =>
            operation.Op == "applyProfilePageSetup"
            && operation.ProfileRequired));
        AssertEqual(true, result.OperationsCatalog.Any(operation =>
            operation.Op == "insertImage"
            && operation.RequiredFormat.Contains("imagePath")));
    }

    static void CliOperationsSampleReturnsExecutableRequestJson()
    {
        var output = new StringWriter();
        var exitCode = ThesisCli.Run(["operations", "sample", "--op", "insertParagraph"], output, TextWriter.Null);

        AssertEqual(0, exitCode);
        var result = ThesisJson.Deserialize<CliResult>(output.ToString());
        AssertEqual("success", result.Status);
        AssertEqual("insertParagraph", result.OperationSample!.Operations[0].Op);
        AssertEqual("example-insertParagraph", result.OperationSample.RequestId);
        AssertEqual(RequestMode.DryRun, result.OperationSample.Mode);
        AssertEqual("新增段落", result.OperationSample.Operations[0].Text);
    }

    static void CliOperationsSampleRejectsUnknownOperation()
    {
        var output = new StringWriter();
        var exitCode = ThesisCli.Run(["operations", "sample", "--op", "missingOp"], output, TextWriter.Null);

        AssertEqual(1, exitCode);
        var result = ThesisJson.Deserialize<CliResult>(output.ToString());
        AssertEqual("error", result.Status);
        AssertEqual("operation_unknown", result.Diagnostics[0].Code);
    }

}
