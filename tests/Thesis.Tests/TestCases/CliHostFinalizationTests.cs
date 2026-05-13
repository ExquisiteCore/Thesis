internal static partial class Program
{
    static void CliFinalizeApplyValidatesSourceOptions()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var profile = Path.Combine(temp.Path, "profile.json");
        var workspace = Path.Combine(temp.Path, ".thesis");

        WriteFixtureDocx(docx);
        File.WriteAllText(profile, "{}");
        AssertEqual("success", SessionInitializer.Initialize(docx, profile, workspace).Status);

        var (missingExitCode, missing) = RunCli(["finalize", "apply"]);
        AssertEqual(1, missingExitCode);
        AssertEqual("error", missing.Status);
        AssertEqual("finalize_source_missing", missing.Diagnostics[0].Code);

        var (ambiguousExitCode, ambiguous) = RunCli(["finalize", "apply", "--doc", docx, "--workspace", workspace]);
        AssertEqual(1, ambiguousExitCode);
        AssertEqual("error", ambiguous.Status);
        AssertEqual("finalize_source_ambiguous", ambiguous.Diagnostics[0].Code);

        var unsafeDirect = RunCli(["finalize", "apply", "--doc", docx]);
        AssertEqual(1, unsafeDirect.ExitCode);
        AssertEqual("error", unsafeDirect.Result.Status);
        AssertEqual("finalize_output_missing", unsafeDirect.Result.Diagnostics[0].Code);
    }

    static void CliFinalizeApplyReportsUnavailableComHost()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        WriteFixtureDocx(docx);

        var (exitCode, result) = RunCli([
            "finalize",
            "apply",
            "--doc",
            docx,
            "--in-place",
            "--host",
            "wps",
            "--prog-id",
            "Thesis.Tests.MissingComHost"
        ]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual(Path.GetFullPath(docx), result.Document);
        AssertEqual(true, result.FinalizationPlan!.Required);
        AssertEqual(true, result.Diagnostics.Any(diagnostic => diagnostic.Code == "host_application_unavailable"));
        AssertEqual(null, result.HostApplication);
    }

    static void CliFinalizeApplyRemovesCopiedOutputWhenComHostFails()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "source.docx");
        var output = Path.Combine(temp.Path, "finalized.docx");
        WriteFixtureDocx(docx);

        var (exitCode, result) = RunCli([
            "finalize",
            "apply",
            "--doc",
            docx,
            "--out",
            output,
            "--host",
            "wps",
            "--prog-id",
            "Thesis.Tests.MissingComHost"
        ]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual(Path.GetFullPath(docx), result.Document);
        AssertEqual(Path.GetFullPath(output), result.OutputPath);
        AssertEqual(true, result.Diagnostics.Any(diagnostic => diagnostic.Code == "host_application_unavailable"));
        AssertEqual(false, File.Exists(output));
    }

    static void CliFinalizeApplyWorkspaceRespectsSessionLock()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);

        using var lockFile = File.Open(
            context.Paths.LockFile,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        var (exitCode, result) = RunCli([
            "finalize",
            "apply",
            "--workspace",
            context.Workspace,
            "--prog-id",
            "Thesis.Tests.MissingComHost"
        ]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("workspace_locked", result.Diagnostics[0].Code);
        AssertEqual(null, result.HostApplication);
    }

    static void CliValidateHostLayoutReportsUnavailableComHost()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);

        var (exitCode, result) = RunCli([
            "validate",
            "--workspace",
            context.Workspace,
            "--host-layout",
            "--prog-id",
            "Thesis.Tests.MissingComHost"
        ]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual(context.Paths.WorkingDocument, result.Document);
        AssertEqual(true, result.Validation is not null);
        AssertEqual(true, result.Diagnostics.Any(diagnostic => diagnostic.Code == "host_application_unavailable"));
        AssertEqual(null, result.HostApplication);
    }
}
