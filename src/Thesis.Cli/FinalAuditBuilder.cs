using Thesis.Schema;

namespace Thesis.Cli;

public sealed class FinalAuditInputs
{
    public string TemplatePath { get; set; } = "";

    public string ContentPath { get; set; } = "";

    public string ProjectRulesPath { get; set; } = "";

    public string? ReferencePath { get; set; }

    public string OutputPath { get; set; } = "";

    public string CandidatePath { get; set; } = "";

    public string ProfilePath { get; set; } = "";

    public string FinalRulesPath { get; set; } = "";

    public string AssembledPath { get; set; } = "";

    public string ValidateBeforePath { get; set; } = "";

    public string HostFinalizationPath { get; set; } = "";

    public string ValidateAfterPath { get; set; } = "";

    public string RehearsalPath { get; set; } = "";

    public string FinalAuditPath { get; set; } = "";

    public string RepairPlanPath { get; set; } = "";

    public string ManualChecklistPath { get; set; } = "";

    public ValidationReport? BeforeFinalizeValidation { get; set; }

    public ValidationReport? AfterFinalizeValidation { get; set; }

    public HostApplicationReport? HostFinalization { get; set; }

    public bool HostFinalizationCurrent { get; set; }

    public RehearsalComparisonReport? Rehearsal { get; set; }
}

public sealed class FinalAuditBuildResult
{
    public FinalAuditReport FinalAudit { get; set; } = new();

    public RepairPlan RepairPlan { get; set; } = new();
}

public static class FinalAuditBuilder
{
    public static FinalAuditBuildResult Build(FinalAuditInputs inputs)
    {
        var audit = new FinalAuditReport
        {
            Inputs =
            {
                ["template"] = Path.GetFullPath(inputs.TemplatePath),
                ["content"] = Path.GetFullPath(inputs.ContentPath),
                ["projectRules"] = Path.GetFullPath(inputs.ProjectRulesPath)
            },
            Outputs =
            {
                ["final"] = Path.GetFullPath(inputs.OutputPath),
                ["candidate"] = NormalizeOutputPath(inputs.CandidatePath),
                ["profile"] = NormalizeOutputPath(inputs.ProfilePath),
                ["finalRules"] = NormalizeOutputPath(inputs.FinalRulesPath),
                ["assembled"] = NormalizeOutputPath(inputs.AssembledPath),
                ["validateBeforeFinalize"] = NormalizeOutputPath(inputs.ValidateBeforePath),
                ["hostFinalization"] = NormalizeOutputPath(inputs.HostFinalizationPath),
                ["validateAfterFinalize"] = NormalizeOutputPath(inputs.ValidateAfterPath),
                ["finalAudit"] = NormalizeOutputPath(inputs.FinalAuditPath),
                ["repairPlan"] = NormalizeOutputPath(inputs.RepairPlanPath),
                ["manualChecklist"] = NormalizeOutputPath(inputs.ManualChecklistPath)
            },
            Steps =
            [
                new FinalAuditStep
                {
                    Id = "profileExtract",
                    Status = string.IsNullOrWhiteSpace(inputs.ProfilePath) ? "missing" : "success",
                    Artifact = NormalizeOutputPath(inputs.ProfilePath)
                },
                new FinalAuditStep
                {
                    Id = "rulesMerge",
                    Status = string.IsNullOrWhiteSpace(inputs.FinalRulesPath) ? "missing" : "success",
                    Artifact = NormalizeOutputPath(inputs.FinalRulesPath)
                },
                new FinalAuditStep
                {
                    Id = "assemble",
                    Status = string.IsNullOrWhiteSpace(inputs.AssembledPath) ? "missing" : "success",
                    Artifact = NormalizeOutputPath(inputs.AssembledPath)
                },
                new FinalAuditStep
                {
                    Id = "validateBeforeFinalize",
                    Status = inputs.BeforeFinalizeValidation is null ? "missing" : "success",
                    Artifact = NormalizeOutputPath(inputs.ValidateBeforePath)
                },
                new FinalAuditStep
                {
                    Id = "validateAfterFinalize",
                    Status = inputs.AfterFinalizeValidation is null ? "missing" : "success",
                    Artifact = NormalizeOutputPath(inputs.ValidateAfterPath)
                },
                new FinalAuditStep
                {
                    Id = "hostFinalization",
                    Status = inputs.HostFinalization?.Executed == true ? "success" : "missing",
                    Artifact = NormalizeOutputPath(inputs.HostFinalizationPath)
                },
                new FinalAuditStep
                {
                    Id = "rehearsalCompare",
                    Status = inputs.ReferencePath is null ? "skipped" : inputs.Rehearsal is null ? "missing" : "success",
                    Artifact = NormalizeOutputPath(inputs.RehearsalPath)
                }
            ],
            ValidationSummary = BuildValidationSummary(inputs.BeforeFinalizeValidation, inputs.AfterFinalizeValidation),
            HostSummary = BuildHostSummary(inputs.HostFinalization, inputs.HostFinalizationCurrent),
            RehearsalSummary = BuildRehearsalSummary(inputs.Rehearsal)
        };

        if (!string.IsNullOrWhiteSpace(inputs.ReferencePath))
        {
            audit.Inputs["reference"] = Path.GetFullPath(inputs.ReferencePath);
            audit.Outputs["rehearsal"] = NormalizeOutputPath(inputs.RehearsalPath);
        }

        AddValidationFindings(inputs, audit);
        AddHostFindings(inputs, audit);
        AddRehearsalFindings(inputs, audit);

        var repair = BuildRepairPlan(audit);
        audit.Ready = audit.Blocking.Count == 0
            && audit.AutoFixable.Count == 0
            && audit.RequiresWps.Count == 0
            && audit.RequiresHuman.Count == 0
            && inputs.Rehearsal is not null
            && inputs.Rehearsal.ReadyForFinalReview
            && inputs.ReferencePath is not null;
        audit.Readiness = audit.Ready
            ? "ready"
            : audit.Blocking.Count > 0 || audit.AutoFixable.Count > 0 || audit.RequiresWps.Count > 0 || HasBlockingHumanFindings(audit)
                ? "blocked"
                : "reducedConfidence";
        audit.Summary = audit.Ready
            ? "Candidate passed available final-draft checks."
            : audit.Readiness == "reducedConfidence"
                ? "Candidate was produced, but reference-backed content coverage was not available."
                : "Candidate has unresolved final-draft findings.";
        repair.Ready = audit.Ready;

        return new FinalAuditBuildResult
        {
            FinalAudit = audit,
            RepairPlan = repair
        };
    }

    private static bool HasBlockingHumanFindings(FinalAuditReport audit)
    {
        return audit.RequiresHuman.Any(finding =>
            !string.Equals(finding.Id, "reference_not_provided", StringComparison.Ordinal));
    }

    private static string NormalizeOutputPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);
    }

    private static FinalAuditValidationSummary BuildValidationSummary(
        ValidationReport? before,
        ValidationReport? after)
    {
        return new FinalAuditValidationSummary
        {
            Before = BuildValidationSnapshot(before),
            After = BuildValidationSnapshot(after)
        };
    }

    private static FinalAuditValidationSnapshot? BuildValidationSnapshot(ValidationReport? validation)
    {
        if (validation is null)
        {
            return null;
        }

        return new FinalAuditValidationSnapshot
        {
            Compliant = validation.Compliant,
            CheckedParagraphs = validation.CheckedParagraphs,
            CheckedTables = validation.CheckedTables,
            DiagnosticCount = validation.Diagnostics.Count
        };
    }

    private static FinalAuditHostSummary? BuildHostSummary(
        HostApplicationReport? host,
        bool hostFinalizationCurrent)
    {
        if (host is null)
        {
            return null;
        }

        return new FinalAuditHostSummary
        {
            Executed = host.Executed,
            Current = hostFinalizationCurrent,
            RequestedHost = host.RequestedHost,
            ProgId = host.ProgId,
            PageCount = host.Layout.PageCount,
            ParagraphCount = host.Layout.ParagraphCount,
            TableCount = host.Layout.TableCount,
            FieldCount = host.Layout.FieldCount,
            TableOfContentsCount = host.Layout.TableOfContentsCount
        };
    }

    private static FinalAuditRehearsalSummary? BuildRehearsalSummary(RehearsalComparisonReport? rehearsal)
    {
        if (rehearsal is null)
        {
            return null;
        }

        return new FinalAuditRehearsalSummary
        {
            ReadyForFinalReview = rehearsal.ReadyForFinalReview,
            HeadingCoverage = rehearsal.ContentCoverage.HeadingCoverage,
            MissingReferenceParagraphCount = rehearsal.ContentCoverage.MissingReferenceParagraphCount,
            MissingReferenceTableCount = rehearsal.ContentCoverage.MissingReferenceTableCount,
            GapCount = rehearsal.ContentCoverage.Gaps.Count,
            DiagnosticCount = rehearsal.Diagnostics.Count
        };
    }

    private static void AddValidationFindings(FinalAuditInputs inputs, FinalAuditReport audit)
    {
        if (inputs.AfterFinalizeValidation is null)
        {
            audit.Blocking.Add(new FinalAuditFinding
            {
                Id = "validation_missing",
                Severity = "error",
                Source = "validate",
                Message = "After-finalization validation did not run.",
                DiagnosticCode = "validation_missing"
            });
            return;
        }

        if (inputs.AfterFinalizeValidation.Compliant)
        {
            return;
        }

        foreach (var diagnostic in inputs.AfterFinalizeValidation.Diagnostics)
        {
            audit.Blocking.Add(new FinalAuditFinding
            {
                Id = diagnostic.Code,
                Severity = diagnostic.Severity,
                Source = "validate",
                Message = diagnostic.Message,
                DiagnosticCode = diagnostic.Code,
                TargetArtifact = "final.docx"
            });
        }
    }

    private static void AddHostFindings(FinalAuditInputs inputs, FinalAuditReport audit)
    {
        if (inputs.HostFinalization is null || !inputs.HostFinalization.Executed)
        {
            audit.RequiresWps.Add(new FinalAuditFinding
            {
                Id = "host_finalization_missing",
                Severity = "error",
                Source = "finalize",
                Message = "WPS/Word finalization was not executed.",
                DiagnosticCode = "host_finalization_missing",
                TargetArtifact = "final.docx"
            });
            return;
        }

        if (!inputs.HostFinalizationCurrent)
        {
            audit.AutoFixable.Add(new FinalAuditFinding
            {
                Id = "host_finalization_stale",
                Severity = "warning",
                Source = "finalize",
                Message = "Host finalization metadata is stale.",
                DiagnosticCode = "host_finalization_stale",
                TargetArtifact = "final.docx"
            });
        }
    }

    private static void AddRehearsalFindings(FinalAuditInputs inputs, FinalAuditReport audit)
    {
        if (inputs.ReferencePath is null)
        {
            audit.RequiresHuman.Add(new FinalAuditFinding
            {
                Id = "reference_not_provided",
                Severity = "warning",
                Source = "rehearsal",
                Message = "Reference thesis was not provided, so content coverage confidence is reduced.",
                DiagnosticCode = "reference_not_provided",
                TargetArtifact = "final.docx"
            });
            return;
        }

        if (inputs.Rehearsal is null)
        {
            audit.Blocking.Add(new FinalAuditFinding
            {
                Id = "rehearsal_missing",
                Severity = "error",
                Source = "rehearsal",
                Message = "Reference thesis was provided, but rehearsal comparison did not run.",
                DiagnosticCode = "rehearsal_missing",
                TargetArtifact = "rehearsal-report.json"
            });
            return;
        }

        var rehearsalFindingCountBefore = CountRehearsalFindings(audit);

        if (inputs.Rehearsal.ContentCoverage.HeadingCoverage < 1)
        {
            audit.Blocking.Add(new FinalAuditFinding
            {
                Id = "heading_coverage_gap",
                Severity = "error",
                Source = "rehearsal",
                Message = "Candidate does not cover all reference headings.",
                DiagnosticCode = "heading_coverage_gap",
                TargetArtifact = "final.docx"
            });
        }

        if (inputs.Rehearsal.ContentCoverage.Gaps.Count > 0
            || inputs.Rehearsal.ContentCoverage.MissingReferenceParagraphCount > 0
            || inputs.Rehearsal.ContentCoverage.MissingReferenceTableCount > 0)
        {
            audit.Blocking.Add(new FinalAuditFinding
            {
                Id = "missing_reference_content",
                Severity = "error",
                Source = "rehearsal",
                Message = "Candidate is missing body content or tables found in the reference thesis.",
                DiagnosticCode = "missing_reference_content",
                TargetArtifact = "content.json"
            });
        }

        foreach (var diagnostic in inputs.Rehearsal.Diagnostics
            .Where(diagnostic => IsWarningOrError(diagnostic.Severity)))
        {
            var finding = new FinalAuditFinding
            {
                Id = string.IsNullOrWhiteSpace(diagnostic.Code) ? "rehearsal_diagnostic" : diagnostic.Code,
                Severity = diagnostic.Severity,
                Source = "rehearsal",
                Message = diagnostic.Message,
                DiagnosticCode = diagnostic.Code,
                TargetArtifact = "final.docx"
            };

            if (string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase))
            {
                audit.Blocking.Add(finding);
            }
            else
            {
                audit.RequiresHuman.Add(finding);
            }
        }

        if (!inputs.Rehearsal.ReadyForFinalReview
            && CountRehearsalFindings(audit) == rehearsalFindingCountBefore)
        {
            audit.RequiresHuman.Add(new FinalAuditFinding
            {
                Id = "rehearsal_not_ready",
                Severity = "warning",
                Source = "rehearsal",
                Message = "Rehearsal comparison did not approve the candidate for final review.",
                DiagnosticCode = "rehearsal_not_ready",
                TargetArtifact = "rehearsal-report.json"
            });
        }
    }

    private static int CountRehearsalFindings(FinalAuditReport audit)
    {
        return audit.Blocking.Count(finding => finding.Source == "rehearsal")
            + audit.AutoFixable.Count(finding => finding.Source == "rehearsal")
            + audit.RequiresWps.Count(finding => finding.Source == "rehearsal")
            + audit.RequiresHuman.Count(finding => finding.Source == "rehearsal");
    }

    private static bool IsWarningOrError(string severity)
    {
        return string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase);
    }

    private static RepairPlan BuildRepairPlan(FinalAuditReport audit)
    {
        var plan = new RepairPlan();
        foreach (var finding in audit.Blocking.Concat(audit.AutoFixable).Concat(audit.RequiresWps).Concat(audit.RequiresHuman))
        {
            plan.Items.Add(new RepairPlanItem
            {
                IssueId = finding.Id,
                Severity = finding.Severity,
                Source = finding.Source,
                TargetArtifact = finding.TargetArtifact ?? "final.docx",
                SuggestedCommand = SuggestedCommand(finding),
                Automatic = finding.Id is "host_finalization_stale",
                RequiresWps = finding.Source == "finalize" || finding.Id.StartsWith("host_", StringComparison.Ordinal),
                Explanation = finding.Message
            });
        }

        return plan;
    }

    private static string SuggestedCommand(FinalAuditFinding finding)
    {
        return finding.Id switch
        {
            "missing_reference_content" => "Add the missing content to content.json and rerun finalize-all.",
            "rehearsal_missing" => "Rerun finalize-all with the reference thesis and inspect rehearsal-report.json.",
            "rehearsal_not_ready" => "Inspect rehearsal-report.json, resolve the reported uncertainty, then rerun finalize-all.",
            "reference_not_provided" => "Provide --reference <accepted-thesis.docx> or complete a manual content coverage review.",
            "host_finalization_stale" => "Rerun finalize-all or finalize apply to refresh fields and pagination.",
            "host_finalization_missing" => "Open WPS/Word finalization support and rerun finalize-all.",
            _ when finding.Source == "validate" => "Apply the suggested validation operation or update final-rules.json, then rerun finalize-all.",
            _ => "Review the finding and rerun finalize-all after correction."
        };
    }
}
