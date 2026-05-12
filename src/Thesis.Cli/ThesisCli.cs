using Thesis.Schema;
using Thesis.Session;
using Thesis.OpenXml;
using Thesis.Profile;
using Thesis.Host;

namespace Thesis.Cli;

public static class ThesisCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        CliResult result;
        var exitCode = 0;

        try
        {
            result = Dispatch(args);
            if (!string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                exitCode = 1;
            }
        }
        catch (CliException ex)
        {
            result = Error(ex.Code, ex.Message);
            exitCode = 1;
        }
        catch (Exception ex)
        {
            result = Error("unhandled_error", ex.Message);
            exitCode = 1;
        }

        output.WriteLine(ThesisJson.Serialize(result));
        return exitCode;
    }

    private static CliResult Dispatch(string[] args)
    {
        if (args is ["session", "init", .. var initArgs])
        {
            var doc = RequiredOption(initArgs, "--doc");
            var profile = RequiredOption(initArgs, "--profile");
            var workspace = RequiredOption(initArgs, "--workspace");
            return SessionInitializer.Initialize(doc, profile, workspace);
        }

        if (args is ["run", .. var runArgs])
        {
            var workspace = RequiredOption(runArgs, "--workspace");
            var requestPath = RequiredOption(runArgs, "--request");
            var request = ThesisJson.Deserialize<OperationRequest>(File.ReadAllText(requestPath));
            request.Options ??= new RunOptions();
            request.Operations ??= [];
            return SessionLifecycle.Run(workspace, request, OpenXmlMicroEditor.Apply);
        }

        if (args is ["apply", .. var applyArgs])
        {
            return ApplyOneShot(applyArgs);
        }

        if (args is ["snapshot", .. var snapshotArgs])
        {
            var workspace = RequiredOption(snapshotArgs, "--workspace");
            var name = RequiredOption(snapshotArgs, "--name");
            return SessionLifecycle.Snapshot(workspace, name);
        }

        if (args is ["rollback", .. var rollbackArgs])
        {
            var workspace = RequiredOption(rollbackArgs, "--workspace");
            var snapshot = RequiredOption(rollbackArgs, "--snapshot");
            return SessionLifecycle.Rollback(workspace, snapshot);
        }

        if (args is ["export", .. var exportArgs])
        {
            var workspace = RequiredOption(exportArgs, "--workspace");
            var outputPath = RequiredOption(exportArgs, "--out");
            return SessionLifecycle.Export(workspace, outputPath);
        }

        if (args is ["inspect", .. var inspectArgs])
        {
            var workspace = RequiredOption(inspectArgs, "--workspace");
            var result = SessionLifecycle.Inspect(workspace);
            if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(result.Document))
            {
                if (OpenXmlDocumentInspector.TryInspect(result.Document, out var documentMap, out var diagnostic))
                {
                    result.DocumentMap = documentMap;
                }
                else if (diagnostic is not null)
                {
                    result.Diagnostics.Add(diagnostic);
                }
            }

            return result;
        }

        if (args is ["validate", .. var validateArgs])
        {
            return Validate(validateArgs);
        }

        if (args is ["profile", "extract", .. var profileArgs])
        {
            return ExtractProfile(profileArgs);
        }

        if (args is ["profile", "explain", .. var explainArgs])
        {
            return ExplainProfile(explainArgs);
        }

        if (args is ["profile", "diff", .. var diffArgs])
        {
            return DiffProfiles(diffArgs);
        }

        if (args is ["operations", "list"])
        {
            return new CliResult
            {
                Status = "success",
                OperationsCatalog = OperationCatalog.List()
            };
        }

        if (args is ["operations", "sample", .. var operationArgs])
        {
            var op = RequiredOption(operationArgs, "--op");
            var sample = OperationCatalog.CreateSample(op);
            if (sample is null)
            {
                return Error("operation_unknown", $"Unknown operation: {op}");
            }

            return new CliResult
            {
                Status = "success",
                OperationSample = sample
            };
        }

        if (args is ["finalize", "plan", .. var finalizeArgs])
        {
            return BuildFinalizationPlan(finalizeArgs);
        }

        if (args is ["finalize", "apply", .. var finalizeApplyArgs])
        {
            return ApplyFinalization(finalizeApplyArgs);
        }

        throw new CliException("unknown_command", "Unknown command.");
    }

    private static CliResult ApplyOneShot(string[] args)
    {
        var doc = RequiredOption(args, "--doc");
        var profilePath = RequiredOption(args, "--profile");
        var requestPath = RequiredOption(args, "--request");
        var outputPath = RequiredOption(args, "--out");
        var fullDocPath = Path.GetFullPath(doc);
        var fullOutputPath = Path.GetFullPath(outputPath);

        if (SamePath(fullOutputPath, fullDocPath))
        {
            return Error("apply_output_refused", "Apply output path must not overwrite the source document.");
        }

        var parent = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Error("apply_output_directory_missing", $"Apply output directory not found: {parent}");
        }

        if (!TryReadProfile(profilePath, out var profile, out var profileError))
        {
            return profileError!;
        }

        NormalizeProfile(profile!);
        OperationRequest request;
        try
        {
            request = ThesisJson.Deserialize<OperationRequest>(File.ReadAllText(requestPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullDocPath,
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "request_invalid",
                        Message = $"Request JSON could not be read: {ex.Message}",
                        Path = Path.GetFullPath(requestPath)
                    }
                ]
            };
        }

        request.Options ??= new RunOptions();
        request.Operations ??= [];
        request.Mode = RequestMode.Execute;
        request.Options.CreateSnapshot = false;

        var tempPath = Path.Combine(parent, Path.GetFileName(fullOutputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp.docx");
        try
        {
            File.Copy(fullDocPath, tempPath, overwrite: true);
            var edit = OpenXmlMicroEditor.Apply(tempPath, request, profile);
            var result = new CliResult
            {
                Status = edit.Diagnostics.Any(diagnostic => string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase))
                    ? "error"
                    : "success",
                RequestId = request.RequestId,
                Mode = request.Mode,
                Document = fullDocPath,
                OutputPath = fullOutputPath,
                Operations = edit.Operations,
                Diagnostics = edit.Diagnostics
            };

            if (!string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            File.Move(tempPath, fullOutputPath, overwrite: true);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CliResult
            {
                Status = "error",
                RequestId = request.RequestId,
                Mode = request.Mode,
                Document = fullDocPath,
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "apply_failed",
                        Message = $"Apply failed: {ex.Message}",
                        Path = fullDocPath
                    }
                ]
            };
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private static CliResult Validate(string[] args)
    {
        var workspace = OptionalOption(args, "--workspace");
        var doc = OptionalOption(args, "--doc");
        if (workspace is not null && doc is not null)
        {
            return Error("validate_source_ambiguous", "Specify either --workspace or --doc, not both.");
        }

        if (doc is not null)
        {
            return ValidateDocument(args, Path.GetFullPath(doc), sessionResult: null);
        }

        if (workspace is null)
        {
            workspace = RequiredOption(args, "--workspace");
        }

        return ValidateWorkspace(args, workspace);
    }

    private static CliResult ValidateWorkspace(string[] args, string workspace)
    {
        var profilePath = OptionalOption(args, "--profile");
        var hostOption = OptionalOption(args, "--host");
        var progId = OptionalOption(args, "--prog-id");
        var inspect = SessionLifecycle.Inspect(workspace);
        if (!string.Equals(inspect.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return inspect;
        }

        var documentPath = inspect.Document
            ?? throw new CliException("working_doc_missing", "Workspace inspect did not return a working document.");
        return ValidateDocument(args, documentPath, inspect);
    }

    private static CliResult ValidateDocument(string[] args, string documentPath, CliResult? sessionResult)
    {
        var profilePath = OptionalOption(args, "--profile");
        var hostOption = OptionalOption(args, "--host");
        var progId = OptionalOption(args, "--prog-id");
        if (!OpenXmlDocumentInspector.TryInspect(documentPath, out var map, out var diagnostic) || map is null)
        {
            return new CliResult
            {
                Status = "error",
                Workspace = sessionResult?.Workspace,
                Document = documentPath,
                Diagnostics = diagnostic is null ? [] : [diagnostic]
            };
        }

        if (profilePath is null)
        {
            if (sessionResult?.Workspace is null)
            {
                return Error("profile_missing", "Validate --doc requires --profile.");
            }

            profilePath = SessionPaths.FromWorkspace(sessionResult.Workspace).ProfileJson;
        }

        if (!TryReadProfile(profilePath, out var profile, out var profileError))
        {
            return profileError!;
        }

        NormalizeProfile(profile!);
        var result = new CliResult
        {
            Status = "success",
            Workspace = sessionResult?.Workspace,
            Document = documentPath,
            Session = sessionResult?.Session,
            Snapshots = sessionResult?.Snapshots ?? [],
            Validation = ProfileComplianceValidator.Validate(map, profile!)
        };

        if (hostOption is not null || progId is not null || HasFlag(args, "--host-layout"))
        {
            var hostOptions = ParseHostOptions(args, action: "validate", defaultHost: hostOption ?? "wps");
            hostOptions.ProgId = progId;
            result.HostApplication = RunHostValidation(documentPath, hostOptions, result.Diagnostics);
            if (result.HostApplication is null)
            {
                result.Status = "error";
            }
        }

        return result;
    }

    private static CliResult BuildFinalizationPlan(string[] args)
    {
        var doc = RequiredOption(args, "--doc");
        var fullDocPath = Path.GetFullPath(doc);
        if (!OpenXmlDocumentInspector.TryInspect(fullDocPath, out var map, out var diagnostic) || map is null)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullDocPath,
                Diagnostics = diagnostic is null ? [] : [diagnostic]
            };
        }

        var plan = FinalizationPlanBuilder.Build(map);
        var result = new CliResult
        {
            Status = "success",
            Document = map.Path,
            FinalizationPlan = plan
        };

        if (plan.Required && plan.Steps.Any(step =>
            step.Required && string.Equals(step.Capability, "hostApplication", StringComparison.Ordinal)))
        {
            result.Diagnostics.Add(new Diagnostic
            {
                Severity = plan.Required ? "warning" : "info",
                Code = "finalization_requires_host_application",
                Message = "True pagination, TOC page numbers, and field values require Word/WPS or another layout-capable host application."
            });
        }

        return result;
    }

    private static CliResult ApplyFinalization(string[] args)
    {
        var workspace = OptionalOption(args, "--workspace");
        var doc = OptionalOption(args, "--doc");
        if (workspace is not null && doc is not null)
        {
            return Error("finalize_source_ambiguous", "Specify either --workspace or --doc, not both.");
        }

        if (workspace is null && doc is null)
        {
            return Error("finalize_source_missing", "Specify either --workspace or --doc.");
        }

        if (workspace is not null)
        {
            return SessionLifecycle.RunWithWorkingDocumentLock(
                workspace,
                "before-finalize-apply",
                workingDocument => ApplyFinalizationToDocument(args, workingDocument));
        }

        return ApplyFinalizationToDocument(args, doc!);
    }

    private static CliResult ApplyFinalizationToDocument(string[] args, string doc)
    {
        var fullDocPath = Path.GetFullPath(doc);
        if (!OpenXmlDocumentInspector.TryInspect(fullDocPath, out var map, out var diagnostic) || map is null)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullDocPath,
                Diagnostics = diagnostic is null ? [] : [diagnostic]
            };
        }

        var result = new CliResult
        {
            Status = "success",
            Document = map.Path,
            FinalizationPlan = FinalizationPlanBuilder.Build(map)
        };

        var hostOptions = ParseHostOptions(args, action: "finalize", defaultHost: "wps");
        var report = RunHostFinalization(fullDocPath, hostOptions, result.Diagnostics);
        if (report is null)
        {
            result.Status = "error";
        }
        else
        {
            result.HostApplication = report;
        }

        return result;
    }

    private static HostApplicationReport? RunHostFinalization(
        string documentPath,
        HostApplicationOptions hostOptions,
        List<Diagnostic> diagnostics)
    {
        try
        {
            return new WpsComAutomationHost().FinalizeDocument(documentPath, hostOptions);
        }
        catch (HostApplicationException ex)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "error",
                Code = ex.Code,
                Message = ex.Message,
                Path = documentPath
            });
            return null;
        }
    }

    private static HostApplicationReport? RunHostValidation(
        string documentPath,
        HostApplicationOptions hostOptions,
        List<Diagnostic> diagnostics)
    {
        try
        {
            return new WpsComAutomationHost().ValidateLayout(documentPath, hostOptions);
        }
        catch (HostApplicationException ex)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "error",
                Code = ex.Code,
                Message = ex.Message,
                Path = documentPath
            });
            return null;
        }
    }

    private static HostApplicationOptions ParseHostOptions(string[] args, string action, string defaultHost)
    {
        return new HostApplicationOptions
        {
            Action = action,
            RequestedHost = OptionalOption(args, "--host") ?? defaultHost,
            ProgId = OptionalOption(args, "--prog-id"),
            Visible = HasFlag(args, "--visible"),
            KeepOpen = HasFlag(args, "--keep-open"),
            UpdateFields = !HasFlag(args, "--skip-fields"),
            UpdateTableOfContents = !HasFlag(args, "--skip-toc"),
            Repaginate = !HasFlag(args, "--skip-repaginate"),
            Save = !HasFlag(args, "--no-save")
        };
    }

    private static CliResult ExtractProfile(string[] args)
    {
        var outputPath = RequiredOption(args, "--out");
        var sourceType = "doc";
        var doc = OptionalOption(args, "--doc");
        var workspaceOption = OptionalOption(args, "--workspace");
        SessionPaths? workspacePaths = null;

        if (doc is not null && workspaceOption is not null)
        {
            return Error("profile_source_ambiguous", "Specify either --doc or --workspace, not both.");
        }

        if (doc is null && workspaceOption is null)
        {
            return Error("profile_source_missing", "Specify either --doc or --workspace.");
        }

        if (doc is null)
        {
            workspacePaths = SessionPaths.FromWorkspace(workspaceOption!);
            var inspect = SessionLifecycle.Inspect(workspaceOption!);
            if (!string.Equals(inspect.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return inspect;
            }

            doc = inspect.Document
                ?? throw new CliException("working_doc_missing", "Workspace inspect did not return a working document.");
            sourceType = "workspace";
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Error("profile_output_directory_missing", $"Profile output directory not found: {parent}");
        }

        var fullDocPath = Path.GetFullPath(doc);
        if (IsProfileOutputRefused(fullOutputPath, fullDocPath, workspacePaths))
        {
            return Error("profile_output_refused", "Profile output path must not overwrite the source document or workspace state.");
        }

        if (!OpenXmlDocumentInspector.TryInspect(fullDocPath, out var map, out var diagnostic) || map is null)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullDocPath,
                OutputPath = fullOutputPath,
                Diagnostics = diagnostic is null ? [] : [diagnostic]
            };
        }

        var profile = TemplateProfileBuilder.Build(map, sourceType);
        File.WriteAllText(fullOutputPath, ThesisJson.Serialize(profile));

        return new CliResult
        {
            Status = "success",
            Document = map.Path,
            OutputPath = fullOutputPath,
            ProfileExplanation = ProfileExplanationBuilder.Build(profile, fullOutputPath)
        };
    }

    private static CliResult ExplainProfile(string[] args)
    {
        var profilePath = OptionalOption(args, "--profile");
        var workspace = OptionalOption(args, "--workspace");
        if (profilePath is not null && workspace is not null)
        {
            return Error("profile_source_ambiguous", "Specify either --profile or --workspace, not both.");
        }

        if (profilePath is null && workspace is null)
        {
            return Error("profile_source_missing", "Specify either --profile or --workspace.");
        }

        profilePath ??= SessionPaths.FromWorkspace(workspace!).ProfileJson;
        var fullProfilePath = Path.GetFullPath(profilePath);
        TemplateProfile profile;
        try
        {
            profile = ThesisJson.Deserialize<TemplateProfile>(File.ReadAllText(fullProfilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new CliResult
            {
                Status = "error",
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "profile_invalid",
                        Message = $"Profile JSON could not be read: {ex.Message}",
                        Path = fullProfilePath
                    }
                ]
            };
        }

        profile.StyleRoles ??= [];
        profile.Diagnostics ??= [];
        profile.TableArchetypes ??= [];
        profile.TablePolicy ??= new ProfileTablePolicy();
        profile.SourceEvidence ??= new ProfileSourceEvidence();

        return new CliResult
        {
            Status = "success",
            ProfileExplanation = ProfileExplanationBuilder.Build(profile, fullProfilePath)
        };
    }

    private static CliResult DiffProfiles(string[] args)
    {
        var leftPath = RequiredOption(args, "--left");
        var rightPath = RequiredOption(args, "--right");
        if (!TryReadProfile(leftPath, out var left, out var leftError))
        {
            return leftError!;
        }

        if (!TryReadProfile(rightPath, out var right, out var rightError))
        {
            return rightError!;
        }

        NormalizeProfile(left!);
        NormalizeProfile(right!);

        return new CliResult
        {
            Status = "success",
            ProfileDiff = ProfileDiffBuilder.Build(left!, leftPath, right!, rightPath)
        };
    }

    private static bool TryReadProfile(string path, out TemplateProfile? profile, out CliResult? error)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            profile = ThesisJson.Deserialize<TemplateProfile>(File.ReadAllText(fullPath));
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            profile = null;
            error = new CliResult
            {
                Status = "error",
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "profile_invalid",
                        Message = $"Profile JSON could not be read: {ex.Message}",
                        Path = fullPath
                    }
                ]
            };
            return false;
        }
    }

    private static void NormalizeProfile(TemplateProfile profile)
    {
        profile.StyleRoles ??= [];
        profile.Diagnostics ??= [];
        profile.TableArchetypes ??= [];
        profile.TablePolicy ??= new ProfileTablePolicy();
        profile.PageSetup ??= new ProfilePageSetup();
        profile.SourceEvidence ??= new ProfileSourceEvidence();
    }

    private static bool IsProfileOutputRefused(string outputPath, string docPath, SessionPaths? workspacePaths)
    {
        if (SamePath(outputPath, docPath))
        {
            return true;
        }

        return workspacePaths is not null
            && (SamePath(outputPath, workspacePaths.WorkingDocument)
                || SamePath(outputPath, workspacePaths.ProfileJson)
                || SamePath(outputPath, workspacePaths.SessionJson)
                || IsPathInsideDirectory(workspacePaths.Workspace, outputPath));
    }

    private static string RequiredOption(string[] args, string name)
    {
        return OptionalOption(args, name)
            ?? throw new CliException("missing_option", $"Missing required option: {name}");
    }

    private static string? OptionalOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(string[] args, string name)
    {
        return args.Any(arg => string.Equals(arg, name, StringComparison.Ordinal));
    }

    private static CliResult Error(string code, string message)
    {
        return new CliResult
        {
            Status = "error",
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "error",
                    Code = code,
                    Message = message
                }
            ]
        };
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathInsideDirectory(string directory, string path)
    {
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class CliException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
