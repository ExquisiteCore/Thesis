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
        if (args.Length == 0 || args is ["--help"] or ["-h"] or ["help"])
        {
            return Help(GeneralUsage());
        }

        if (args is ["operations", "--help"] or ["operations", "-h"] or ["operations", "help"])
        {
            return Help(OperationsUsage());
        }

        if (args is ["finalize-all", "--help"] or ["finalize-all", "-h"] or ["finalize-all", "help"])
        {
            return Help(FinalizeAllUsage());
        }

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
            if (!TryReadRequest(requestPath, out var request, out var requestError))
            {
                return requestError!;
            }

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
            return Inspect(inspectArgs);
        }

        if (args is ["finalize-all", .. var finalizeAllArgs])
        {
            return FinalizeAll(finalizeAllArgs);
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

        if (args is ["content", "extract", .. var contentExtractArgs])
        {
            return ContentExtractCommand.Execute(contentExtractArgs);
        }

        if (args is ["rules", "merge", .. var rulesArgs])
        {
            return MergeRules(rulesArgs);
        }

        if (args is ["rehearsal", "compare", .. var rehearsalCompareArgs])
        {
            return CompareRehearsal(rehearsalCompareArgs);
        }

        if (args is ["assemble", .. var assembleArgs])
        {
            return AssembleDocument(assembleArgs);
        }

        if (args is ["generate", .. var generateArgs])
        {
            return GenerateDocument(generateArgs);
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

    private static CliResult FinalizeAll(string[] args)
    {
        try
        {
            var parseError = FinalizeAllCommand.TryParse(args, out var options);
            if (parseError is not null)
            {
                return parseError;
            }

            return FinalizeAllCommand.Execute(options);
        }
        catch (ArgumentException ex)
        {
            return FinalizeAllCommand.Error(ex.Message, $"Missing value for {ex.Message}.");
        }
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

    private static CliResult Inspect(string[] args)
    {
        var workspace = OptionalOption(args, "--workspace");
        var doc = OptionalOption(args, "--doc");
        if (workspace is not null && doc is not null)
        {
            return Error("inspect_source_ambiguous", "Specify either --workspace or --doc, not both.");
        }

        if (doc is not null)
        {
            var fullDocPath = Path.GetFullPath(doc);
            if (!OpenXmlDocumentInspector.TryInspect(fullDocPath, out var documentMap, out var diagnostic) || documentMap is null)
            {
                return new CliResult
                {
                    Status = "error",
                    Document = fullDocPath,
                    Diagnostics = diagnostic is null ? [] : [diagnostic]
                };
            }

            return new CliResult
            {
                Status = "success",
                Document = fullDocPath,
                DocumentMap = documentMap
            };
        }

        workspace ??= RequiredOption(args, "--workspace");
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

    private static CliResult MergeRules(string[] args)
    {
        var profilePath = RequiredOption(args, "--profile");
        var projectPath = RequiredOption(args, "--project");
        var outputPath = RequiredOption(args, "--out");
        var fullOutputPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Error("rules_output_directory_missing", $"Rules output directory not found: {parent}");
        }

        if (SamePath(fullOutputPath, profilePath) || SamePath(fullOutputPath, projectPath))
        {
            return Error("rules_output_refused", "Rules output path must not overwrite input rule files.");
        }

        if (!TryReadProfile(profilePath, out var profile, out var profileError))
        {
            return profileError!;
        }

        if (!TryReadProjectRules(projectPath, out var projectRules, out var projectError))
        {
            return projectError!;
        }

        NormalizeProfile(profile!);
        var merged = ProjectRulesMerger.Merge(profile!, projectRules!);
        File.WriteAllText(fullOutputPath, ThesisJson.Serialize(merged));
        return new CliResult
        {
            Status = "success",
            OutputPath = fullOutputPath,
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "info",
                    Code = "rules_merged",
                    Message = "Template profile and project rules were merged into final rules.",
                    Path = fullOutputPath
                }
            ]
        };
    }

    private static CliResult GenerateDocument(string[] args)
    {
        var contentPath = RequiredOption(args, "--content");
        var rulesPath = RequiredOption(args, "--rules");
        var outputPath = RequiredOption(args, "--out");
        var fullOutputPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Error("generate_output_directory_missing", $"Generate output directory not found: {parent}");
        }

        if (SamePath(fullOutputPath, contentPath) || SamePath(fullOutputPath, rulesPath))
        {
            return Error("generate_output_refused", "Generate output path must not overwrite input JSON files.");
        }

        if (!TryReadContent(contentPath, out var content, out var contentError))
        {
            return contentError!;
        }

        if (!TryReadProfile(rulesPath, out var rules, out var rulesError))
        {
            return rulesError!;
        }

        NormalizeContent(content!);
        NormalizeProfile(rules!);

        var tempPath = Path.Combine(parent, Path.GetFileName(fullOutputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp.docx");
        try
        {
            ThesisDocumentGenerator.Generate(content!, rules!, tempPath);
            File.Move(tempPath, fullOutputPath, overwrite: true);
            return new CliResult
            {
                Status = "success",
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "info",
                        Code = "thesis_generated",
                        Message = "Thesis content JSON was generated into a DOCX document.",
                        Path = fullOutputPath
                    }
                ]
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CliResult
            {
                Status = "error",
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "generate_failed",
                        Message = $"Generate failed: {ex.Message}",
                        Path = fullOutputPath
                    }
                ]
            };
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private static CliResult AssembleDocument(string[] args)
    {
        var docPath = RequiredOption(args, "--doc");
        var contentPath = RequiredOption(args, "--content");
        var profilePath = RequiredOption(args, "--profile");
        var outputPath = RequiredOption(args, "--out");
        var frontMatterDocPaths = Options(args, "--front-matter-doc");
        var fullDocPath = Path.GetFullPath(docPath);
        var fullOutputPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Error("assemble_output_directory_missing", $"Assemble output directory not found: {parent}");
        }

        if (SamePath(fullOutputPath, fullDocPath)
            || SamePath(fullOutputPath, contentPath)
            || SamePath(fullOutputPath, profilePath)
            || frontMatterDocPaths.Any(path => SamePath(fullOutputPath, path)))
        {
            return Error("assemble_output_refused", "Assemble output path must not overwrite input files.");
        }

        if (!TryReadContent(contentPath, out var content, out var contentError))
        {
            return contentError!;
        }

        if (!TryReadProfile(profilePath, out var profile, out var profileError))
        {
            return profileError!;
        }

        NormalizeContent(content!);
        NormalizeProfile(profile!);

        var tempPath = Path.Combine(parent, Path.GetFileName(fullOutputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp.docx");
        try
        {
            File.Copy(fullDocPath, tempPath, overwrite: true);
            ThesisDocumentGenerator.AssembleIntoTemplate(content!, profile!, tempPath, frontMatterDocPaths);
            File.Move(tempPath, fullOutputPath, overwrite: true);
            return new CliResult
            {
                Status = "success",
                Document = fullDocPath,
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "info",
                        Code = "thesis_assembled",
                        Message = "Thesis content JSON was assembled into a template DOCX copy.",
                        Path = fullOutputPath
                    }
                ]
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
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
                        Code = "assemble_failed",
                        Message = $"Assemble failed: {ex.Message}",
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

    private static CliResult CompareRehearsal(string[] args)
    {
        var candidatePath = Path.GetFullPath(RequiredOption(args, "--candidate"));
        var referencePath = Path.GetFullPath(RequiredOption(args, "--reference"));
        var profilePath = RequiredOption(args, "--profile");
        var outputPath = OptionalOption(args, "--out");
        if (outputPath is not null)
        {
            outputPath = Path.GetFullPath(outputPath);
            var parent = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            {
                return Error("rehearsal_output_directory_missing", $"Rehearsal output directory not found: {parent}");
            }

            if (SamePath(outputPath, candidatePath)
                || SamePath(outputPath, referencePath)
                || SamePath(outputPath, profilePath))
            {
                return Error("rehearsal_output_refused", "Rehearsal output path must not overwrite input files.");
            }
        }

        if (!OpenXmlDocumentInspector.TryInspect(candidatePath, out var candidateMap, out var candidateDiagnostic)
            || candidateMap is null)
        {
            return new CliResult
            {
                Status = "error",
                Document = candidatePath,
                OutputPath = outputPath,
                Diagnostics = candidateDiagnostic is null ? [] : [candidateDiagnostic]
            };
        }

        if (!OpenXmlDocumentInspector.TryInspect(referencePath, out var referenceMap, out var referenceDiagnostic)
            || referenceMap is null)
        {
            return new CliResult
            {
                Status = "error",
                Document = candidatePath,
                OutputPath = outputPath,
                Diagnostics = referenceDiagnostic is null ? [] : [referenceDiagnostic]
            };
        }

        if (!TryReadProfile(profilePath, out var profile, out var profileError))
        {
            return profileError!;
        }

        NormalizeProfile(profile!);
        var validation = ProfileComplianceValidator.Validate(candidateMap, profile!);
        var report = RehearsalComparisonBuilder.Build(candidateMap, referenceMap, validation);
        if (outputPath is not null)
        {
            File.WriteAllText(outputPath, ThesisJson.Serialize(report));
        }

        return new CliResult
        {
            Status = "success",
            Document = candidatePath,
            OutputPath = outputPath,
            RehearsalComparison = report,
            Diagnostics = report.Diagnostics
        };
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

        AddFinalizationDiagnostic(map, result.Diagnostics);

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
                workingDocument => ApplyFinalizationToDocument(args, workingDocument, allowInPlace: true));
        }

        return ApplyFinalizationToDirectDocument(args, doc!);
    }

    private static CliResult ApplyFinalizationToDirectDocument(string[] args, string doc)
    {
        var output = OptionalOption(args, "--out");
        var inPlace = HasFlag(args, "--in-place");
        if (output is not null && inPlace)
        {
            return Error("finalize_output_ambiguous", "Specify either --out or --in-place, not both.");
        }

        if (output is null && !inPlace)
        {
            return Error("finalize_output_missing", "Direct finalize --doc requires --out or explicit --in-place.");
        }

        var sourcePath = Path.GetFullPath(doc);
        if (output is null)
        {
            return ApplyFinalizationToDocument(args, sourcePath, allowInPlace: true);
        }

        var outputPath = Path.GetFullPath(output);
        if (SamePath(outputPath, sourcePath))
        {
            return Error("finalize_output_refused", "Finalize output path must not overwrite the source document unless --in-place is used.");
        }

        var parent = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Error("finalize_output_directory_missing", $"Finalize output directory not found: {parent}");
        }

        try
        {
            File.Copy(sourcePath, outputPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CliResult
            {
                Status = "error",
                Document = sourcePath,
                OutputPath = outputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "finalize_copy_failed",
                        Message = $"Finalize source could not be copied: {ex.Message}",
                        Path = outputPath
                    }
                ]
            };
        }

        var result = ApplyFinalizationToDocument(args, outputPath, allowInPlace: true);
        result.Document = sourcePath;
        result.OutputPath = outputPath;
        if (!string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            DeleteIfExists(outputPath);
        }

        return result;
    }

    private static CliResult ApplyFinalizationToDocument(string[] args, string doc, bool allowInPlace)
    {
        var fullDocPath = Path.GetFullPath(doc);
        if (!allowInPlace)
        {
            return Error("finalize_output_missing", "Finalize requires --out or explicit --in-place.");
        }

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
            OpenXmlFinalizationMetadata.MarkHostFinalized(
                fullDocPath,
                report,
                result.FinalizationPlan.Reasons);
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
            if (!IsValidProfile(profile))
            {
                error = ProfileInvalid(fullPath, "Profile has an invalid structure.");
                return false;
            }

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

    private static bool IsValidProfile(TemplateProfile? profile)
    {
        return profile is not null
            && profile.StyleRoles is not null
            && profile.RolePolicies is not null
            && profile.FormatClusters is not null
            && profile.FinalizationReasons is not null
            && profile.PageSetup is not null
            && profile.StructurePolicy is not null
            && profile.StructurePolicy.Sections is not null
            && profile.StructurePolicy.Sections.All(section => section is not null)
            && profile.StylePolicy is not null
            && profile.StylePolicy.NumericStyleIds is not null
            && profile.StylePolicy.DisallowedGeneratedStyleIds is not null
            && profile.PackagePolicy is not null
            && profile.FieldPolicy is not null
            && profile.ZonePolicy is not null
            && profile.ZonePolicy.Landmarks is not null
            && profile.ZonePolicy.ForbiddenFrontMatterHeadings is not null
            && profile.NumberingPolicy is not null
            && profile.TablePolicy is not null
            && profile.TableArchetypes is not null
            && profile.Diagnostics is not null
            && profile.SourceEvidence is not null
            && profile.StyleRoles.All(role => role.Evidence is not null)
            && profile.RolePolicies.All(policy =>
                policy.Match is not null
                && policy.Match.StyleIds is not null
                && policy.Match.TextPatterns is not null
                && policy.Match.OutlineLevels is not null
                && IsValidRoleFormatMatch(policy.Match.Format))
            && profile.FormatClusters.All(IsValidFormatCluster)
            && profile.NumberingPolicy.Instances is not null
            && profile.NumberingPolicy.ParagraphUses is not null
            && profile.TablePolicy.ObservedColumnCounts is not null
            && profile.TableArchetypes.All(archetype =>
                archetype.Match is not null
                && archetype.Match.ColumnCounts is not null)
            && profile.Diagnostics.All(diagnostic => diagnostic.Evidence is not null)
            && profile.SourceEvidence.ParagraphSamples is not null;
    }

    private static bool IsValidFormatCluster(ProfileFormatCluster cluster)
    {
        return !string.IsNullOrWhiteSpace(cluster.Id)
            && string.Equals(cluster.AppliesTo, "paragraph", StringComparison.OrdinalIgnoreCase)
            && IsKnownClusterRoleHint(cluster.RoleHint)
            && cluster.Count >= 0
            && cluster.Confidence is >= 0 and <= 1
            && cluster.StyleIds is not null
            && cluster.Match is not null
            && cluster.Match.StyleIds is not null
            && cluster.Match.TextPatterns is not null
            && cluster.Match.OutlineLevels is not null
            && IsValidRoleFormatMatch(cluster.Match.Format)
            && cluster.Format is not null
            && cluster.Evidence is not null
            && cluster.Evidence.All(evidence => evidence is not null);
    }

    private static bool IsKnownClusterRoleHint(string? roleHint)
    {
        return roleHint is "unknown" or "title" or "heading1" or "heading2" or "heading3" or "body" or "abstract.zh" or "abstract.en" or "toc" or "toc.title" or "references";
    }

    private static bool IsValidRoleFormatMatch(ProfileRoleFormatMatch? format)
    {
        return format is null
            || (IsValidRange(format.FirstLineIndentTwips)
                && IsValidRange(format.LeftIndentTwips)
                && IsValidRange(format.RightIndentTwips));
    }

    private static bool IsValidRange(IntRangeMatch? range)
    {
        if (range is null)
        {
            return true;
        }

        if (range.Exact is null && range.Min is null && range.Max is null)
        {
            return false;
        }

        if (range.Exact is not null && (range.Min is not null || range.Max is not null))
        {
            return false;
        }

        return range.Min is null || range.Max is null || range.Min <= range.Max;
    }

    private static CliResult ProfileInvalid(string path, string message)
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
                    Message = message,
                    Path = path
                }
            ]
        };
    }

    private static bool TryReadRequest(string path, out OperationRequest request, out CliResult? error)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            request = ThesisJson.Deserialize<OperationRequest>(File.ReadAllText(fullPath));
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            request = new OperationRequest();
            error = new CliResult
            {
                Status = "error",
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "request_invalid",
                        Message = $"Request JSON could not be read: {ex.Message}",
                        Path = fullPath
                    }
                ]
            };
            return false;
        }
    }

    private static bool TryReadProjectRules(string path, out ProjectRules? rules, out CliResult? error)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            rules = ThesisJson.Deserialize<ProjectRules>(File.ReadAllText(fullPath));
            if (!string.Equals(rules.RulesKind, "projectRules", StringComparison.OrdinalIgnoreCase))
            {
                error = new CliResult
                {
                    Status = "error",
                    Diagnostics =
                    [
                        new Diagnostic
                        {
                            Severity = "error",
                            Code = "project_rules_invalid",
                            Message = "Project rules JSON must have rulesKind 'projectRules'.",
                            Path = fullPath
                        }
                    ]
                };
                return false;
            }

            NormalizeProjectRules(rules);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            rules = null;
            error = new CliResult
            {
                Status = "error",
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "project_rules_invalid",
                        Message = $"Project rules JSON could not be read: {ex.Message}",
                        Path = fullPath
                    }
                ]
            };
            return false;
        }
    }

    private static bool TryReadContent(string path, out ThesisContent? content, out CliResult? error)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            content = ThesisJson.Deserialize<ThesisContent>(File.ReadAllText(fullPath));
            if (!string.Equals(content.DocumentKind, "thesisContent", StringComparison.OrdinalIgnoreCase))
            {
                error = new CliResult
                {
                    Status = "error",
                    Diagnostics =
                    [
                        new Diagnostic
                        {
                            Severity = "error",
                            Code = "content_invalid",
                            Message = "Content JSON must have documentKind 'thesisContent'.",
                            Path = fullPath
                        }
                    ]
                };
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            content = null;
            error = new CliResult
            {
                Status = "error",
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "content_invalid",
                        Message = $"Content JSON could not be read: {ex.Message}",
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
        profile.RoleAliases ??= [];
        profile.Diagnostics ??= [];
        profile.TableArchetypes ??= [];
        profile.TablePolicy ??= new ProfileTablePolicy();
        profile.PageSetup ??= new ProfilePageSetup();
        profile.StructurePolicy ??= new ProfileStructurePolicy();
        profile.StructurePolicy.Sections ??= [];
        profile.StylePolicy ??= new ProfileStylePolicy();
        profile.StylePolicy.NumericStyleIds ??= [];
        profile.StylePolicy.DisallowedGeneratedStyleIds ??= [];
        profile.PackagePolicy ??= new ProfilePackagePolicy();
        profile.FieldPolicy ??= new ProfileFieldPolicy();
        profile.ZonePolicy ??= new ProfileZonePolicy();
        profile.ZonePolicy.Landmarks ??= [];
        profile.ZonePolicy.ForbiddenFrontMatterHeadings ??= [];
        profile.SourceEvidence ??= new ProfileSourceEvidence();
    }

    private static void NormalizeProjectRules(ProjectRules rules)
    {
        rules.RoleAliases ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        rules.RoleFormats ??= new Dictionary<string, ProjectParagraphFormatRule>(StringComparer.OrdinalIgnoreCase);
        rules.RolePolicies ??= [];
        if (rules.StructurePolicy is not null)
        {
            rules.StructurePolicy.Sections ??= [];
        }

        if (rules.StylePolicy is not null)
        {
            rules.StylePolicy.NumericStyleIds ??= [];
            rules.StylePolicy.DisallowedGeneratedStyleIds ??= [];
        }

        if (rules.ZonePolicy is not null)
        {
            rules.ZonePolicy.Landmarks ??= [];
            rules.ZonePolicy.ForbiddenFrontMatterHeadings ??= [];
        }

        rules.TableArchetypes ??= [];
        rules.Diagnostics ??= [];
    }

    private static void NormalizeContent(ThesisContent content)
    {
        content.KeywordsZh ??= [];
        content.KeywordsEn ??= [];
        content.Chapters ??= [];
        content.References ??= [];
        foreach (var chapter in content.Chapters)
        {
            chapter.Paragraphs ??= [];
            chapter.Sections ??= [];
            chapter.Tables ??= [];
            foreach (var section in chapter.Sections)
            {
                section.Paragraphs ??= [];
                section.Tables ??= [];
                foreach (var table in section.Tables)
                {
                    table.Headers ??= [];
                    table.Rows ??= [];
                }
            }

            foreach (var table in chapter.Tables)
            {
                table.Headers ??= [];
                table.Rows ??= [];
            }
        }
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

    private static void AddFinalizationDiagnostic(DocumentMap map, List<Diagnostic> diagnostics)
    {
        var plan = FinalizationPlanBuilder.Build(map);
        if (!plan.Required)
        {
            return;
        }

        diagnostics.Add(new Diagnostic
        {
            Severity = "warning",
            Code = "finalization_required",
            Message = "Document still requires Word/WPS finalization for fields, TOC page numbers, or true pagination.",
            Path = map.Path
        });
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

    private static List<string> Options(string[] args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                values.Add(args[i + 1]);
            }
        }

        return values;
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

    private static CliResult Help(string message)
    {
        return new CliResult
        {
            Status = "success",
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "info",
                    Code = "help",
                    Message = message
                }
            ]
        };
    }

    private static string GeneralUsage()
    {
        return string.Join(
            Environment.NewLine,
            "Thesis DOCX CLI usage:",
            "  session init --doc <source.docx> --profile <profile.json> --workspace <dir>",
            "  profile extract --doc <template.docx> --out <profile.json>",
            "  profile explain --profile <profile.json>",
            "  inspect --doc <docx>",
            "  content extract --doc <source.docx> --out <content.json> [--report <report.json>]",
            "  rules merge --profile <profile.json> --project <project-rules.json> --out <final-rules.json>",
            "  finalize-all --template <template.docx> [--front-matter-doc <docx>] --content <content.json> --project-rules <project-rules.json> --out <final.docx> --workdir <dir> [--reference <reference.docx>] [--skip-host-finalize]",
            "  rehearsal compare --candidate <docx> --reference <docx> --profile <profile.json> [--out <report.json>]",
            "  assemble --doc <template.docx> [--front-matter-doc <docx>] --content <content.json> --profile <final-rules.json> --out <thesis.docx>",
            "  generate --content <content.json> --rules <final-rules.json> --out <thesis.docx>",
            "  run --workspace <dir> --request <request.json>",
            "  apply --doc <source.docx> --profile <profile.json> --request <request.json> --out <output.docx>",
            "  validate --doc <docx> --profile <profile.json>",
            "  validate --workspace <dir> [--host-layout]",
            "  finalize plan --doc <docx>",
            "  finalize apply --doc <docx> --out <output.docx>",
            "  finalize apply --doc <docx> --in-place",
            "  finalize apply --workspace <dir>",
            "  operations list",
            "  operations sample --op <operation>");
    }

    private static string FinalizeAllUsage()
    {
        return string.Join(
            Environment.NewLine,
            "Finalize-all usage:",
            "  finalize-all --template <template.docx> [--front-matter-doc <docx>] --content <content.json> --project-rules <project-rules.json> --out <final.docx> --workdir <dir> [--reference <reference.docx>] [--skip-host-finalize] [--host wps|word|auto] [--prog-id <com.prog.id>]",
            "  Repeat --front-matter-doc to insert task books, proposal reports, or other front-matter DOCX files before the thesis body.",
            "Artifacts written to --workdir:",
            "  profile.json, final-rules.json, assembled.docx, candidate.docx, validate-before-finalize.json, host-finalization.json, validate-after-finalize.json, rehearsal-report.json, final-audit.json, repair-plan.json, manual-checklist.md",
            "Readiness:",
            "  final-audit.ready=true only when validation passes, WPS/Word finalization is current, and reference rehearsal approves the candidate.",
            "  --out is written only when final-audit.ready=true; otherwise inspect workdir candidate.docx and audit artifacts.");
    }

    private static string OperationsUsage()
    {
        return string.Join(
            Environment.NewLine,
            "Operation catalog usage:",
            "  operations list",
            "  operations sample --op <operation>",
            "Targets commonly use:",
            "  { \"type\": \"paragraphIndex\", \"index\": 0 }",
            "  { \"type\": \"paragraphText\", \"text\": \"摘要\", \"match\": \"exact|contains|regex\" }",
            "  { \"type\": \"tableIndex\", \"index\": 0 }",
            "  { \"type\": \"tableCell\", \"tableIndex\": 0, \"rowIndex\": 0, \"cellIndex\": 0 }",
            "  { \"type\": \"sectionRange\", \"start\": <target>, \"end\": <target>, \"includeStart\": false }");
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
