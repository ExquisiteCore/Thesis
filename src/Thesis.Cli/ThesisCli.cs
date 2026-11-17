using static Thesis.Cli.ProfileCommands;
using static Thesis.Cli.CliInputReader;
using static Thesis.Cli.CliCommandSupport;
using static Thesis.Cli.DocumentBuildCommands;
using Thesis.Schema;
using Thesis.Session;
using Thesis.OpenXml;
using Thesis.Profile;
using Thesis.Host;
using Thesis.Core;

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
        if (args is ["project", .. var projectArgs]) return ProjectCommand.Execute(projectArgs);
        if (args is ["internal-host-finalize", .. var workerArgs]) return OfficeWorkerCommand.Execute(workerArgs);
        if (args is ["document-convert", .. var convertArgs]) return DocumentConvertCommand.Execute(convertArgs);
        if (args is ["doctor", .. var doctorArgs]) return DoctorCommand.Execute(doctorArgs);
        if (args is ["delivery-review", .. var reviewArgs]) return DeliveryReviewCommand.Execute(reviewArgs);
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

        if (args is ["template-plan", .. var planArgs]) return TemplatePlanCommand.Execute(planArgs);

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

        if (args is ["apply-template" or "compose", .. var templateArgs])
        {
            if (args[0] == "compose" && !templateArgs.Contains("--require-ready") && templateArgs is not (["--help"] or ["-h"]))
                templateArgs = [.. templateArgs, "--require-ready", "true"];
            return ApplyTemplateCommand.Execute(templateArgs);
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
            if (finalizeAllArgs.Contains("--plan"))
                return ApplyTemplateCommand.Execute([.. finalizeAllArgs, "--require-ready", "true"]);
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
        var outputPath = output is null ? sourcePath : Path.GetFullPath(output);
        if (output is not null && SamePath(outputPath, sourcePath))
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
            using var transaction = new FileOutputTransaction();
            var staged = transaction.Stage(outputPath);
            File.Copy(sourcePath, staged);
            var result = ApplyFinalizationToDocument(args, staged, allowInPlace: true);
            result.Document = sourcePath;
            result.OutputPath = outputPath;
            if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                transaction.Commit();
                if (result.HostApplication is not null) result.HostApplication.Document = outputPath;
            }
            return result;
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
            return Thesis.Application.DocumentFinalizer.FinalizeWithOffice(documentPath, hostOptions);
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
            "  apply-template --doc <source.docx> --template <template.docx> --plan <plan.json> --out <output.docx> [--report <report.json>] [--render-config <render.json> --pdf <output.pdf>] [--workdir <directory>]",
            "  project (create|inspect|update|compose) --help    Shared desktop/Agent project workflow",
            "  compose (--doc <source.docx> | --content <content.json>) --template <template.docx> [--plan <plan.json>] --out <final.docx> [--render-config <render.json> --pdf <final.pdf>] [--require-ready true|false]",
            "  doctor --render-config <render.json>",
            "  document-convert --doc <source.doc> --out <converted.docx> [--host wps|word|auto]",
            "  delivery-review --doc <candidate.docx> --pdf <candidate.pdf> --report <candidate.json> --review <review.json> --out <final.docx> --out-pdf <final.pdf> --out-report <final.json>",
            "  template-plan bind|inspect --doc <source.docx> --template <template.docx> --plan <plan.json> --out <result.json>",
            "  template-plan draft --doc <source.docx> --template <template.docx> --out <draft.json>",
            "  template-plan analyze --template <template.docx> --out <analysis.json>",
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
