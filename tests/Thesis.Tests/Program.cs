using System.Diagnostics;
using System.Text.Json.Nodes;
using Thesis.Cli;
using Thesis.Schema;
using Thesis.Session;

var tests = new (string Name, Action Test)[]
{
    ("JSON roundtrip uses camelCase and enum strings", JsonRoundtripUsesCamelCaseAndEnumStrings),
    ("SessionPaths resolves expected filenames", SessionPathsResolvesExpectedFilenames),
    ("SessionInitializer creates workspace files and refuses existing workspace", SessionInitializerCreatesFilesAndRefusesExistingWorkspace),
    ("SessionInitializer refuses non-empty stale workspace", SessionInitializerRefusesNonEmptyStaleWorkspace),
    ("SessionInitializer refuses locked workspace", SessionInitializerRefusesLockedWorkspace),
    ("CLI run reads request JSON and returns success JSON", CliRunReadsRequestJsonAndReturnsSuccessJson),
    ("CLI unknown command returns JSON error", CliUnknownCommandReturnsJsonError),
    ("CLI stub commands return structured JSON", CliStubCommandsReturnStructuredJson)
};

var failures = new List<string>();
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine(ex);
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"{failures.Count} test(s) failed.");
    Environment.Exit(1);
}

static void JsonRoundtripUsesCamelCaseAndEnumStrings()
{
    var request = new OperationRequest
    {
        SchemaVersion = "1.0",
        RequestId = "fix-abstract-001",
        Mode = RequestMode.ValidateOnly,
        Operations =
        [
            new ThesisOperation
            {
                Id = "op-001",
                Op = "replaceParagraph",
                Target = JsonNode.Parse("""{"type":"role","role":"abstract.zh.body"}"""),
                Text = "updated"
            }
        ]
    };

    var json = ThesisJson.Serialize(request);

    AssertContains(json, "\"schemaVersion\"");
    AssertContains(json, "\"requestId\"");
    AssertContains(json, "\"mode\":\"validateOnly\"");
    AssertDoesNotContain(json, "SchemaVersion");

    var roundtrip = ThesisJson.Deserialize<OperationRequest>(json);
    AssertEqual(RequestMode.ValidateOnly, roundtrip.Mode);
    AssertEqual("op-001", roundtrip.Operations[0].Id);
    AssertEqual(true, roundtrip.Options.CreateSnapshot);
    AssertEqual(true, roundtrip.Options.StopOnError);
    AssertEqual(false, roundtrip.Options.RequireSingleMatch);
    AssertEqual(false, roundtrip.Options.TrackChanges);
}

static void SessionPathsResolvesExpectedFilenames()
{
    var root = Path.Combine(Path.GetTempPath(), "thesis-tests", Guid.NewGuid().ToString("N"));
    var paths = SessionPaths.FromWorkspace(root);

    AssertEqual(Path.GetFullPath(root), paths.Workspace);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "session.json"), paths.SessionJson);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "profile.json"), paths.ProfileJson);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "working.docx"), paths.WorkingDocument);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "session.lock"), paths.LockFile);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "snapshots"), paths.SnapshotsDirectory);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "logs"), paths.LogsDirectory);
    AssertEqual(Path.Combine(Path.GetFullPath(root), "cache"), paths.CacheDirectory);
}

static void SessionInitializerCreatesFilesAndRefusesExistingWorkspace()
{
    using var temp = new TempDirectory();
    var sourceDoc = Path.Combine(temp.Path, "source.docx");
    var profile = Path.Combine(temp.Path, "input-profile.json");
    var workspace = Path.Combine(temp.Path, ".thesis");

    File.WriteAllText(sourceDoc, "doc");
    File.WriteAllText(profile, "{}");

    var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);
    var paths = SessionPaths.FromWorkspace(workspace);

    AssertEqual("success", result.Status);
    AssertEqual(true, File.Exists(paths.WorkingDocument));
    AssertEqual(true, File.Exists(paths.ProfileJson));
    AssertEqual(true, File.Exists(paths.SessionJson));
    AssertEqual(true, File.Exists(Path.Combine(paths.SnapshotsDirectory, "0001-init.docx")));
    AssertEqual("doc", File.ReadAllText(sourceDoc));

    var sessionJson = ThesisJson.Deserialize<JsonObject>(File.ReadAllText(paths.SessionJson));
    AssertEqual("1.0", sessionJson["schemaVersion"]!.GetValue<string>());
    AssertEqual(Path.GetFullPath(sourceDoc), sessionJson["originalPath"]!.GetValue<string>());
    AssertEqual(paths.WorkingDocument, sessionJson["workingPath"]!.GetValue<string>());
    AssertEqual(paths.ProfileJson, sessionJson["profilePath"]!.GetValue<string>());
    AssertEqual(1, sessionJson["snapshotCounter"]!.GetValue<int>());

    var refused = SessionInitializer.Initialize(sourceDoc, profile, workspace);
    AssertEqual("error", refused.Status);
    AssertEqual(true, refused.Diagnostics.Count > 0);
}

static void SessionInitializerRefusesNonEmptyStaleWorkspace()
{
    using var temp = new TempDirectory();
    var sourceDoc = Path.Combine(temp.Path, "source.docx");
    var profile = Path.Combine(temp.Path, "input-profile.json");
    var workspace = Path.Combine(temp.Path, ".thesis");
    var paths = SessionPaths.FromWorkspace(workspace);

    Directory.CreateDirectory(workspace);
    File.WriteAllText(sourceDoc, "doc");
    File.WriteAllText(profile, "{}");
    File.WriteAllText(paths.SessionJson, "{}");

    var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);

    AssertEqual("error", result.Status);
    AssertEqual("workspace_exists", result.Diagnostics[0].Code);
    AssertEqual(false, File.Exists(paths.WorkingDocument));
}

static void SessionInitializerRefusesLockedWorkspace()
{
    using var temp = new TempDirectory();
    var sourceDoc = Path.Combine(temp.Path, "source.docx");
    var profile = Path.Combine(temp.Path, "input-profile.json");
    var workspace = Path.Combine(temp.Path, ".thesis");
    var paths = SessionPaths.FromWorkspace(workspace);

    Directory.CreateDirectory(workspace);
    File.WriteAllText(sourceDoc, "doc");
    File.WriteAllText(profile, "{}");
    File.WriteAllText(paths.LockFile, "locked");

    var result = SessionInitializer.Initialize(sourceDoc, profile, workspace);

    AssertEqual("error", result.Status);
    AssertEqual("workspace_locked", result.Diagnostics[0].Code);
    AssertEqual(false, File.Exists(paths.WorkingDocument));
}

static void CliRunReadsRequestJsonAndReturnsSuccessJson()
{
    using var temp = new TempDirectory();
    var workspace = Path.Combine(temp.Path, ".thesis");
    var requestPath = Path.Combine(temp.Path, "request.json");
    Directory.CreateDirectory(workspace);
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
        ["run", "--workspace", workspace, "--request", requestPath],
        output,
        error);

    AssertEqual(0, exitCode);
    var result = ThesisJson.Deserialize<CliResult>(output.ToString());
    AssertEqual("success", result.Status);
    AssertEqual("req-123", result.RequestId);
    AssertEqual(Path.GetFullPath(workspace), result.Workspace);
    AssertEqual(Path.Combine(Path.GetFullPath(workspace), "working.docx"), result.Document);
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

static void CliStubCommandsReturnStructuredJson()
{
    using var temp = new TempDirectory();
    var workspace = Path.Combine(temp.Path, ".thesis");
    var requestPath = Path.Combine(temp.Path, "request.json");
    File.WriteAllText(requestPath, """{"schemaVersion":"1.0","requestId":"inspect-1","mode":"dryRun","operations":[]}""");

    foreach (var command in new[] { "inspect", "snapshot", "rollback", "export" })
    {
        var output = new StringWriter();
        var exitCode = ThesisCli.Run([command, "--workspace", workspace, "--request", requestPath], output, TextWriter.Null);
        var result = ThesisJson.Deserialize<CliResult>(output.ToString());

        AssertEqual(1, exitCode);
        AssertEqual("notImplemented", result.Status);
        AssertEqual("not_implemented", result.Diagnostics[0].Code);
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new UnreachableException($"Expected '{expected}', got '{actual}'.");
    }
}

static void AssertContains(string text, string expected)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        throw new UnreachableException($"Expected text to contain '{expected}'.");
    }
}

static void AssertDoesNotContain(string text, string unexpected)
{
    if (text.Contains(unexpected, StringComparison.Ordinal))
    {
        throw new UnreachableException($"Expected text not to contain '{unexpected}'.");
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "thesis-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
