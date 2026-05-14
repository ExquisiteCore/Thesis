using Thesis.Host;
using Thesis.OpenXml;
using Thesis.Profile;
using Thesis.Schema;

namespace Thesis.Cli;

internal sealed class FinalizeAllOptions
{
    public string TemplatePath { get; set; } = "";

    public string ContentPath { get; set; } = "";

    public string ProjectRulesPath { get; set; } = "";

    public string? ReferencePath { get; set; }

    public string OutputPath { get; set; } = "";

    public string Workdir { get; set; } = "";

    public List<string> FrontMatterDocPaths { get; set; } = [];

    public bool SkipHostFinalize { get; set; }

    public string RequestedHost { get; set; } = "wps";

    public string? ProgId { get; set; }
}

internal static class FinalizeAllCommand
{
    private static readonly string[] ArtifactFileNames =
    [
        "profile.json",
        "final-rules.json",
        "assembled.docx",
        "candidate.docx",
        "validate-before-finalize.json",
        "host-finalization.json",
        "validate-after-finalize.json",
        "rehearsal-report.json",
        "final-audit.json",
        "repair-plan.json",
        "manual-checklist.md"
    ];

    public static CliResult? TryParse(string[] args, out FinalizeAllOptions options)
    {
        options = new FinalizeAllOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next(string code)
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException(code);
                }

                return args[++i];
            }

            switch (arg)
            {
                case "--template":
                    options.TemplatePath = Next("finalize_all_template_missing");
                    break;
                case "--content":
                    options.ContentPath = Next("finalize_all_content_missing");
                    break;
                case "--project-rules":
                    options.ProjectRulesPath = Next("finalize_all_project_rules_missing");
                    break;
                case "--reference":
                    options.ReferencePath = Next("finalize_all_reference_missing");
                    break;
                case "--out":
                    options.OutputPath = Next("finalize_all_output_missing");
                    break;
                case "--workdir":
                    options.Workdir = Next("finalize_all_workdir_missing");
                    break;
                case "--front-matter-doc":
                    options.FrontMatterDocPaths.Add(Next("finalize_all_front_matter_doc_missing"));
                    break;
                case "--skip-host-finalize":
                    options.SkipHostFinalize = true;
                    break;
                case "--host":
                    options.RequestedHost = Next("finalize_all_host_missing");
                    break;
                case "--prog-id":
                    options.ProgId = Next("finalize_all_prog_id_missing");
                    break;
                default:
                    return Error("finalize_all_unknown_option", $"Unknown finalize-all option: {arg}");
            }
        }

        return Validate(options);
    }

    public static CliResult Execute(FinalizeAllOptions options)
    {
        var fullTemplatePath = Path.GetFullPath(options.TemplatePath);
        var fullContentPath = Path.GetFullPath(options.ContentPath);
        var fullProjectRulesPath = Path.GetFullPath(options.ProjectRulesPath);
        var fullOutputPath = Path.GetFullPath(options.OutputPath);
        var fullWorkdir = Path.GetFullPath(options.Workdir);
        var fullFrontMatterDocPaths = options.FrontMatterDocPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToList();
        Directory.CreateDirectory(fullWorkdir);

        var profilePath = Path.Combine(fullWorkdir, "profile.json");
        var finalRulesPath = Path.Combine(fullWorkdir, "final-rules.json");
        var assembledPath = Path.Combine(fullWorkdir, "assembled.docx");
        var validateBeforePath = Path.Combine(fullWorkdir, "validate-before-finalize.json");
        var hostFinalizationPath = Path.Combine(fullWorkdir, "host-finalization.json");
        var validateAfterPath = Path.Combine(fullWorkdir, "validate-after-finalize.json");
        var rehearsalPath = Path.Combine(fullWorkdir, "rehearsal-report.json");
        var finalAuditPath = Path.Combine(fullWorkdir, "final-audit.json");
        var repairPlanPath = Path.Combine(fullWorkdir, "repair-plan.json");
        var manualChecklistPath = Path.Combine(fullWorkdir, "manual-checklist.md");
        var candidatePath = Path.Combine(fullWorkdir, "candidate.docx");
        var tempFinalPath = Path.Combine(fullWorkdir, $".finalize-all-{Guid.NewGuid():N}.tmp.docx");

        if (!OpenXmlDocumentInspector.TryInspect(fullTemplatePath, out var templateMap, out var templateDiagnostic)
            || templateMap is null)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullTemplatePath,
                OutputPath = fullOutputPath,
                Diagnostics = templateDiagnostic is null ? [] : [templateDiagnostic]
            };
        }

        var profile = TemplateProfileBuilder.Build(templateMap, "doc");
        File.WriteAllText(profilePath, ThesisJson.Serialize(profile));

        ProjectRules projectRules;
        ThesisContent content;
        try
        {
            projectRules = ThesisJson.Deserialize<ProjectRules>(File.ReadAllText(fullProjectRulesPath));
            content = ThesisJson.Deserialize<ThesisContent>(File.ReadAllText(fullContentPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return Error("finalize_all_input_invalid", $"Finalize-all input could not be read: {ex.Message}");
        }

        NormalizeProjectRules(projectRules);
        NormalizeContent(content);
        var finalRules = ProjectRulesMerger.Merge(profile, projectRules);
        File.WriteAllText(finalRulesPath, ThesisJson.Serialize(finalRules));

        try
        {
            File.Copy(fullTemplatePath, assembledPath, overwrite: true);
            ThesisDocumentGenerator.AssembleIntoTemplate(content, finalRules, assembledPath, fullFrontMatterDocPaths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullTemplatePath,
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "finalize_all_assemble_failed",
                        Message = $"Finalize-all assemble failed: {ex.Message}",
                        Path = fullTemplatePath
                    }
                ]
            };
        }

        if (!OpenXmlDocumentInspector.TryInspect(assembledPath, out var assembledMap, out var assembledDiagnostic)
            || assembledMap is null)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullTemplatePath,
                OutputPath = fullOutputPath,
                Diagnostics = assembledDiagnostic is null ? [] : [assembledDiagnostic]
            };
        }

        var validateBefore = ProfileComplianceValidator.Validate(assembledMap, finalRules);
        File.WriteAllText(validateBeforePath, ThesisJson.Serialize(validateBefore));

        HostApplicationReport? hostReport = null;
        try
        {
            File.Copy(assembledPath, tempFinalPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullTemplatePath,
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "finalize_all_output_failed",
                        Message = $"Finalize-all temporary output could not be written: {ex.Message}",
                        Path = tempFinalPath
                    }
                ]
            };
        }

        if (!options.SkipHostFinalize)
        {
            var hostOptions = new HostApplicationOptions
            {
                Action = "finalize",
                RequestedHost = options.RequestedHost,
                ProgId = options.ProgId
            };

            try
            {
                hostReport = new WpsComAutomationHost().FinalizeDocument(tempFinalPath, hostOptions);
                OpenXmlFinalizationMetadata.MarkHostFinalized(
                    tempFinalPath,
                    hostReport,
                    FinalizationPlanBuilder.Build(assembledMap).Reasons);
                File.WriteAllText(hostFinalizationPath, ThesisJson.Serialize(hostReport));
            }
            catch (HostApplicationException ex)
            {
                DeleteIfExists(tempFinalPath);
                hostReport = new HostApplicationReport
                {
                    Action = "finalize",
                    RequestedHost = options.RequestedHost,
                    ProgId = options.ProgId ?? "",
                    Document = fullOutputPath,
                    Executed = false
                };
                File.WriteAllText(hostFinalizationPath, ThesisJson.Serialize(hostReport));
                var failureAuditResult = BuildAudit(
                    fullTemplatePath,
                    fullContentPath,
                    fullProjectRulesPath,
                    options.ReferencePath is null ? null : Path.GetFullPath(options.ReferencePath),
                    fullOutputPath,
                    candidatePath,
                    profilePath,
                    finalRulesPath,
                    assembledPath,
                    validateBeforePath,
                    hostFinalizationPath,
                    validateAfterPath,
                    rehearsalPath,
                    finalAuditPath,
                    repairPlanPath,
                    manualChecklistPath,
                    validateBefore,
                    afterFinalizeValidation: null,
                    hostReport,
                    hostFinalizationCurrent: false,
                    rehearsal: null);
                WriteAuditArtifacts(finalAuditPath, repairPlanPath, manualChecklistPath, failureAuditResult);
                return new CliResult
                {
                    Status = "error",
                    Document = fullTemplatePath,
                    OutputPath = fullOutputPath,
                    HostApplication = hostReport,
                    FinalAudit = failureAuditResult.FinalAudit,
                    RepairPlan = failureAuditResult.RepairPlan,
                    Diagnostics =
                    [
                        new Diagnostic
                        {
                            Severity = "error",
                            Code = ex.Code,
                            Message = ex.Message,
                            Path = fullOutputPath
                        }
                    ]
                };
            }
        }
        else
        {
            hostReport = new HostApplicationReport
            {
                Action = "finalize",
                RequestedHost = options.RequestedHost,
                Document = fullOutputPath,
                Executed = false
            };
            File.WriteAllText(hostFinalizationPath, ThesisJson.Serialize(hostReport));
        }

        if (hostReport is not null)
        {
            hostReport.Document = fullOutputPath;
            File.WriteAllText(hostFinalizationPath, ThesisJson.Serialize(hostReport));
        }

        if (!OpenXmlDocumentInspector.TryInspect(tempFinalPath, out var finalMap, out var finalDiagnostic)
            || finalMap is null)
        {
            DeleteIfExists(tempFinalPath);
            return new CliResult
            {
                Status = "error",
                Document = fullTemplatePath,
                OutputPath = fullOutputPath,
                Diagnostics = finalDiagnostic is null ? [] : [finalDiagnostic]
            };
        }

        var validateAfter = ProfileComplianceValidator.Validate(finalMap, finalRules);
        File.WriteAllText(validateAfterPath, ThesisJson.Serialize(validateAfter));

        DocumentMap? referenceMapForRehearsal = null;
        if (!string.IsNullOrWhiteSpace(options.ReferencePath))
        {
            var fullReferencePath = Path.GetFullPath(options.ReferencePath);
            if (!OpenXmlDocumentInspector.TryInspect(fullReferencePath, out var referenceMap, out var referenceDiagnostic)
                || referenceMap is null)
            {
                var referenceAuditResult = BuildAudit(
                    fullTemplatePath,
                    fullContentPath,
                    fullProjectRulesPath,
                    fullReferencePath,
                    fullOutputPath,
                    candidatePath,
                    profilePath,
                    finalRulesPath,
                    assembledPath,
                    validateBeforePath,
                    hostFinalizationPath,
                    validateAfterPath,
                    rehearsalPath,
                    finalAuditPath,
                    repairPlanPath,
                    manualChecklistPath,
                    validateBefore,
                    validateAfter,
                    hostReport,
                    finalMap.HostFinalization?.IsCurrent == true,
                    rehearsal: null);
                WriteAuditArtifacts(finalAuditPath, repairPlanPath, manualChecklistPath, referenceAuditResult);
                DeleteIfExists(tempFinalPath);
                return new CliResult
                {
                    Status = "error",
                    Document = fullTemplatePath,
                    OutputPath = fullOutputPath,
                    FinalAudit = referenceAuditResult.FinalAudit,
                    RepairPlan = referenceAuditResult.RepairPlan,
                    Diagnostics = referenceDiagnostic is null ? [] : [referenceDiagnostic]
                };
            }
            referenceMapForRehearsal = referenceMap;
        }

        try
        {
            File.Move(tempFinalPath, candidatePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteIfExists(tempFinalPath);
            return new CliResult
            {
                Status = "error",
                Document = fullTemplatePath,
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "finalize_all_output_failed",
                        Message = $"Finalize-all candidate could not be written: {ex.Message}",
                        Path = candidatePath
                    }
                ]
            };
        }

        if (!OpenXmlDocumentInspector.TryInspect(candidatePath, out var candidateMap, out var candidateDiagnostic)
            || candidateMap is null)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullTemplatePath,
                OutputPath = fullOutputPath,
                Diagnostics = candidateDiagnostic is null ? [] : [candidateDiagnostic]
            };
        }

        if (hostReport is not null)
        {
            hostReport.Document = candidatePath;
            File.WriteAllText(hostFinalizationPath, ThesisJson.Serialize(hostReport));
        }

        RehearsalComparisonReport? rehearsal = null;
        if (referenceMapForRehearsal is not null)
        {
            rehearsal = RehearsalComparisonBuilder.Build(candidateMap, referenceMapForRehearsal, validateAfter);
            File.WriteAllText(rehearsalPath, ThesisJson.Serialize(rehearsal));
        }

        var auditResult = BuildAudit(
            fullTemplatePath,
            fullContentPath,
            fullProjectRulesPath,
            options.ReferencePath is null ? null : Path.GetFullPath(options.ReferencePath),
            fullOutputPath,
            candidatePath,
            profilePath,
            finalRulesPath,
            assembledPath,
            validateBeforePath,
            hostFinalizationPath,
            validateAfterPath,
            rehearsalPath,
            finalAuditPath,
            repairPlanPath,
            manualChecklistPath,
            validateBefore,
            validateAfter,
            hostReport,
            candidateMap.HostFinalization?.IsCurrent == true,
            rehearsal);

        WriteAuditArtifacts(finalAuditPath, repairPlanPath, manualChecklistPath, auditResult);

        if (!auditResult.FinalAudit.Ready)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullTemplatePath,
                OutputPath = fullOutputPath,
                Validation = validateAfter,
                HostApplication = hostReport,
                RehearsalComparison = rehearsal,
                FinalAudit = auditResult.FinalAudit,
                RepairPlan = auditResult.RepairPlan,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "finalize_all_not_ready",
                        Message = "Finalize-all candidate did not pass final audit. Existing output was not overwritten; inspect workdir candidate and audit artifacts.",
                        Path = candidatePath
                    }
                ]
            };
        }

        try
        {
            File.Copy(candidatePath, fullOutputPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullTemplatePath,
                OutputPath = fullOutputPath,
                FinalAudit = auditResult.FinalAudit,
                RepairPlan = auditResult.RepairPlan,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "finalize_all_output_failed",
                        Message = $"Finalize-all output could not be written: {ex.Message}",
                        Path = fullOutputPath
                    }
                ]
            };
        }

        if (hostReport is not null)
        {
            hostReport.Document = fullOutputPath;
            File.WriteAllText(hostFinalizationPath, ThesisJson.Serialize(hostReport));
        }

        if (rehearsal is not null)
        {
            rehearsal.CandidateDocument = fullOutputPath;
            File.WriteAllText(rehearsalPath, ThesisJson.Serialize(rehearsal));
        }

        return new CliResult
        {
            Status = "success",
            Document = fullTemplatePath,
            OutputPath = fullOutputPath,
            Validation = validateAfter,
            HostApplication = hostReport,
            RehearsalComparison = rehearsal,
            FinalAudit = auditResult.FinalAudit,
            RepairPlan = auditResult.RepairPlan
        };
    }

    public static CliResult Error(string code, string message)
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

    private static CliResult? Validate(FinalizeAllOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TemplatePath))
        {
            return Error("finalize_all_template_missing", "Specify --template <template.docx>.");
        }

        if (string.IsNullOrWhiteSpace(options.ContentPath))
        {
            return Error("finalize_all_content_missing", "Specify --content <content.json>.");
        }

        if (string.IsNullOrWhiteSpace(options.ProjectRulesPath))
        {
            return Error("finalize_all_project_rules_missing", "Specify --project-rules <project-rules.json>.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            return Error("finalize_all_output_missing", "Specify --out <final.docx>.");
        }

        if (string.IsNullOrWhiteSpace(options.Workdir))
        {
            return Error("finalize_all_workdir_missing", "Specify --workdir <run-directory>.");
        }

        var output = Path.GetFullPath(options.OutputPath);
        var workdir = Path.GetFullPath(options.Workdir);
        var artifacts = ArtifactPaths(workdir).ToList();
        var inputs = new[]
        {
            options.TemplatePath,
            options.ContentPath,
            options.ProjectRulesPath,
            options.ReferencePath
        }
            .OfType<string>()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Concat(options.FrontMatterDocPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            .Select(Path.GetFullPath)
            .ToList();

        if (inputs.Any(input => SamePath(input, output)))
        {
            return Error("finalize_all_output_refused", "Finalize-all output path must not overwrite input files.");
        }

        if (artifacts.Any(artifact => SamePath(artifact, output)))
        {
            return Error("finalize_all_output_refused", "Finalize-all output path must not overwrite workdir artifacts.");
        }

        if (inputs.Any(input => artifacts.Any(artifact => SamePath(artifact, input))))
        {
            return Error("finalize_all_workdir_refused", "Finalize-all workdir artifacts must not overwrite input files.");
        }

        var outputParent = Path.GetDirectoryName(output);
        if (string.IsNullOrWhiteSpace(outputParent) || !Directory.Exists(outputParent))
        {
            return Error("finalize_all_output_directory_missing", $"Finalize-all output directory not found: {outputParent}.");
        }

        return null;
    }

    private static FinalAuditBuildResult BuildAudit(
        string templatePath,
        string contentPath,
        string projectRulesPath,
        string? referencePath,
        string outputPath,
        string candidatePath,
        string profilePath,
        string finalRulesPath,
        string assembledPath,
        string validateBeforePath,
        string hostFinalizationPath,
        string validateAfterPath,
        string rehearsalPath,
        string finalAuditPath,
        string repairPlanPath,
        string manualChecklistPath,
        ValidationReport? beforeFinalizeValidation,
        ValidationReport? afterFinalizeValidation,
        HostApplicationReport? hostReport,
        bool hostFinalizationCurrent,
        RehearsalComparisonReport? rehearsal)
    {
        return FinalAuditBuilder.Build(new FinalAuditInputs
        {
            TemplatePath = templatePath,
            ContentPath = contentPath,
            ProjectRulesPath = projectRulesPath,
            ReferencePath = referencePath,
            OutputPath = outputPath,
            CandidatePath = candidatePath,
            ProfilePath = profilePath,
            FinalRulesPath = finalRulesPath,
            AssembledPath = assembledPath,
            ValidateBeforePath = validateBeforePath,
            HostFinalizationPath = hostFinalizationPath,
            ValidateAfterPath = validateAfterPath,
            RehearsalPath = rehearsalPath,
            FinalAuditPath = finalAuditPath,
            RepairPlanPath = repairPlanPath,
            ManualChecklistPath = manualChecklistPath,
            BeforeFinalizeValidation = beforeFinalizeValidation,
            AfterFinalizeValidation = afterFinalizeValidation,
            HostFinalization = hostReport,
            HostFinalizationCurrent = hostFinalizationCurrent,
            Rehearsal = rehearsal
        });
    }

    private static void WriteAuditArtifacts(
        string finalAuditPath,
        string repairPlanPath,
        string manualChecklistPath,
        FinalAuditBuildResult auditResult)
    {
        File.WriteAllText(finalAuditPath, ThesisJson.Serialize(auditResult.FinalAudit));
        File.WriteAllText(repairPlanPath, ThesisJson.Serialize(auditResult.RepairPlan));
        File.WriteAllText(manualChecklistPath, ManualChecklist(auditResult.FinalAudit));
    }

    private static IEnumerable<string> ArtifactPaths(string fullWorkdir)
    {
        return ArtifactFileNames.Select(fileName => Path.GetFullPath(Path.Combine(fullWorkdir, fileName)));
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ManualChecklist(FinalAuditReport audit)
    {
        var lines = new List<string>
        {
            "# Manual Final-Draft Checklist",
            "",
            "- Open the DOCX in WPS/Word and confirm real pagination.",
            "- Confirm table-of-contents page numbers after field update.",
            "- Check cross-page tables and continued-table captions.",
            "- Check orphan headings and isolated lines.",
            ""
        };

        foreach (var finding in audit.RequiresHuman.Concat(audit.RequiresWps))
        {
            lines.Add($"- {finding.Id}: {finding.Message}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
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
