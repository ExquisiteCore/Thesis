# Finalize-All Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `finalize-all` CLI command that runs the existing thesis finalization pipeline and emits a DOCX candidate, merged rules, final audit, repair plan, and manual checklist.

**Architecture:** Keep orchestration in the CLI layer, with focused builders for audit and repair-plan decisions. Use existing `profile extract`, `rules merge`, `assemble`, `validate`, `finalize apply`, and `rehearsal compare` building blocks where possible, and keep WPS/COM assertions conservative.

**Tech Stack:** C#/.NET 10, Open XML SDK through existing project helpers, existing JSON serialization via `ThesisJson`, current custom test runner in `tests/Thesis.Tests`.

---

## File Structure

- Create `src/Thesis.Schema/FinalAuditModels.cs` for `FinalAuditReport`, `FinalAuditFinding`, `FinalAuditStep`, and `RepairPlan` DTOs.
- Modify `src/Thesis.Schema/CliModels.cs` to expose `FinalAudit` and `RepairPlan` on `CliResult`.
- Create `src/Thesis.Cli/FinalAuditBuilder.cs` for readiness classification from validation, host finalization, and rehearsal reports.
- Create `src/Thesis.Cli/FinalizeAllCommand.cs` for path validation, artifact names, orchestration helpers, and output writing.
- Modify `src/Thesis.Cli/ThesisCli.cs` to route `finalize-all` and update help text.
- Create `tests/Thesis.Tests/TestCases/CliFinalizeAllAuditTests.cs` for model, audit, and CLI orchestration tests.
- Modify `tests/Thesis.Tests/TestCatalog.cs` to register the new tests.
- Modify `README.md` after implementation to document the new single-command finalization flow.

## Task 1: Add Audit And Repair Schema

**Files:**
- Create: `src/Thesis.Schema/FinalAuditModels.cs`
- Modify: `src/Thesis.Schema/CliModels.cs`
- Modify: `tests/Thesis.Tests/TestCases/JsonSerializationTests.cs`
- Modify: `tests/Thesis.Tests/TestCatalog.cs`

- [ ] **Step 1: Write the failing JSON model test**

Add this test method to `tests/Thesis.Tests/TestCases/JsonSerializationTests.cs`:

```csharp
static void FinalAuditModelsSerializeAsCamelCaseJson()
{
    var result = new CliResult
    {
        FinalAudit = new FinalAuditReport
        {
            Ready = false,
            Readiness = "blocked",
            Summary = "Candidate has blocking findings.",
            Inputs = new Dictionary<string, string>
            {
                ["template"] = "template.docx"
            },
            Outputs = new Dictionary<string, string>
            {
                ["final"] = "final.docx"
            },
            Steps =
            [
                new FinalAuditStep
                {
                    Id = "assemble",
                    Status = "success",
                    Artifact = "assembled.docx"
                }
            ],
            Blocking =
            [
                new FinalAuditFinding
                {
                    Id = "missing_reference_content",
                    Severity = "error",
                    Source = "rehearsal",
                    Message = "Reference content is missing.",
                    DiagnosticCode = "missing_reference_content"
                }
            ]
        },
        RepairPlan = new RepairPlan
        {
            Ready = false,
            Items =
            [
                new RepairPlanItem
                {
                    IssueId = "missing_reference_content",
                    Severity = "error",
                    Source = "rehearsal",
                    TargetArtifact = "content.json",
                    SuggestedCommand = "Add the missing content to content.json and rerun finalize-all.",
                    Automatic = false,
                    RequiresWps = false,
                    Explanation = "The reference thesis contains content not present in the candidate."
                }
            ]
        }
    };

    var json = ThesisJson.Serialize(result);

    AssertContains(json, "\"finalAudit\"");
    AssertContains(json, "\"repairPlan\"");
    AssertContains(json, "\"ready\":false");
    AssertContains(json, "\"blocking\"");
    AssertContains(json, "\"issueId\":\"missing_reference_content\"");
}
```

Register it in `tests/Thesis.Tests/TestCatalog.cs` near the JSON tests:

```csharp
("Final audit models serialize as camelCase JSON", FinalAuditModelsSerializeAsCamelCaseJson),
```

- [ ] **Step 2: Run the failing test**

Run:

```powershell
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: compile failure because `FinalAuditReport`, `FinalAuditStep`, `FinalAuditFinding`, `RepairPlan`, and `RepairPlanItem` do not exist.

- [ ] **Step 3: Create schema models**

Create `src/Thesis.Schema/FinalAuditModels.cs`:

```csharp
namespace Thesis.Schema;

public sealed class FinalAuditReport
{
    public bool Ready { get; set; }

    public string Readiness { get; set; } = "unknown";

    public string Summary { get; set; } = "";

    public Dictionary<string, string> Inputs { get; set; } = [];

    public Dictionary<string, string> Outputs { get; set; } = [];

    public List<FinalAuditStep> Steps { get; set; } = [];

    public List<FinalAuditFinding> Blocking { get; set; } = [];

    public List<FinalAuditFinding> AutoFixable { get; set; } = [];

    public List<FinalAuditFinding> RequiresWps { get; set; } = [];

    public List<FinalAuditFinding> RequiresHuman { get; set; } = [];
}

public sealed class FinalAuditStep
{
    public string Id { get; set; } = "";

    public string Status { get; set; } = "";

    public string Artifact { get; set; } = "";

    public string Message { get; set; } = "";
}

public sealed class FinalAuditFinding
{
    public string Id { get; set; } = "";

    public string Severity { get; set; } = "info";

    public string Source { get; set; } = "";

    public string Message { get; set; } = "";

    public string? DiagnosticCode { get; set; }

    public string? TargetArtifact { get; set; }
}

public sealed class RepairPlan
{
    public bool Ready { get; set; }

    public List<RepairPlanItem> Items { get; set; } = [];
}

public sealed class RepairPlanItem
{
    public string IssueId { get; set; } = "";

    public string Severity { get; set; } = "info";

    public string Source { get; set; } = "";

    public string TargetArtifact { get; set; } = "";

    public string SuggestedCommand { get; set; } = "";

    public bool Automatic { get; set; }

    public bool RequiresWps { get; set; }

    public string Explanation { get; set; } = "";
}
```

- [ ] **Step 4: Add fields to CLI result**

Modify `src/Thesis.Schema/CliModels.cs` after `RehearsalComparison`:

```csharp
public FinalAuditReport? FinalAudit { get; set; }

public RepairPlan? RepairPlan { get; set; }
```

- [ ] **Step 5: Run the test to verify it passes**

Run:

```powershell
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: after rebuilding is required, this may still fail under `--no-build`. If so, run:

```powershell
dotnet build ThesisTool.slnx
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: the new JSON model test passes.

- [ ] **Step 6: Commit**

```powershell
git add src\Thesis.Schema\FinalAuditModels.cs src\Thesis.Schema\CliModels.cs tests\Thesis.Tests\TestCases\JsonSerializationTests.cs tests\Thesis.Tests\TestCatalog.cs
git commit -m "feat: add final audit schema"
```

## Task 2: Build Audit Decisions From Existing Reports

**Files:**
- Create: `src/Thesis.Cli/FinalAuditBuilder.cs`
- Create: `tests/Thesis.Tests/TestCases/CliFinalizeAllAuditTests.cs`
- Modify: `tests/Thesis.Tests/TestCatalog.cs`

- [ ] **Step 1: Write failing builder tests**

Create `tests/Thesis.Tests/TestCases/CliFinalizeAllAuditTests.cs` with:

```csharp
internal static partial class Program
{
    static void FinalAuditBuilderBlocksOnValidationDiagnostics()
    {
        var validation = new ValidationReport
        {
            Compliant = false,
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "warning",
                    Code = "profile_role_mismatch",
                    Message = "Body format does not match profile."
                }
            ]
        };

        var audit = FinalAuditBuilder.Build(new FinalAuditInputs
        {
            TemplatePath = "template.docx",
            ContentPath = "content.json",
            ProjectRulesPath = "project-rules.json",
            OutputPath = "final.docx",
            AfterFinalizeValidation = validation,
            HostFinalization = new HostApplicationReport { Executed = true },
            HostFinalizationCurrent = true
        });

        AssertEqual(false, audit.FinalAudit.Ready);
        AssertEqual("blocked", audit.FinalAudit.Readiness);
        AssertEqual(true, audit.FinalAudit.Blocking.Any(finding => finding.DiagnosticCode == "profile_role_mismatch"));
        AssertEqual(true, audit.RepairPlan.Items.Any(item => item.IssueId == "profile_role_mismatch"));
    }

    static void FinalAuditBuilderBlocksOnRehearsalContentGaps()
    {
        var rehearsal = new RehearsalComparisonReport
        {
            ReadyForFinalReview = false,
            ContentCoverage = new RehearsalContentCoverage
            {
                HeadingCoverage = 1,
                MissingReferenceParagraphCount = 1,
                Gaps =
                [
                    new RehearsalContentGap
                    {
                        GapType = "paragraph",
                        ReferenceContext = "第二章 系统设计",
                        ReferenceTextPreview = "系统设计正文缺失。"
                    }
                ]
            }
        };

        var audit = FinalAuditBuilder.Build(new FinalAuditInputs
        {
            TemplatePath = "template.docx",
            ContentPath = "content.json",
            ProjectRulesPath = "project-rules.json",
            ReferencePath = "reference.docx",
            OutputPath = "final.docx",
            AfterFinalizeValidation = new ValidationReport { Compliant = true },
            HostFinalization = new HostApplicationReport { Executed = true },
            HostFinalizationCurrent = true,
            Rehearsal = rehearsal
        });

        AssertEqual(false, audit.FinalAudit.Ready);
        AssertEqual(true, audit.FinalAudit.Blocking.Any(finding => finding.Id == "missing_reference_content"));
        AssertEqual(true, audit.RepairPlan.Items.Any(item =>
            item.IssueId == "missing_reference_content"
            && item.TargetArtifact == "content.json"));
    }

    static void FinalAuditBuilderMarksNoReferenceAsReducedConfidence()
    {
        var audit = FinalAuditBuilder.Build(new FinalAuditInputs
        {
            TemplatePath = "template.docx",
            ContentPath = "content.json",
            ProjectRulesPath = "project-rules.json",
            OutputPath = "final.docx",
            AfterFinalizeValidation = new ValidationReport { Compliant = true },
            HostFinalization = new HostApplicationReport { Executed = true },
            HostFinalizationCurrent = true
        });

        AssertEqual(false, audit.FinalAudit.Ready);
        AssertEqual("reducedConfidence", audit.FinalAudit.Readiness);
        AssertEqual(true, audit.FinalAudit.RequiresHuman.Any(finding => finding.Id == "reference_not_provided"));
        AssertEqual(0, audit.FinalAudit.Blocking.Count);
    }

    static void FinalAuditBuilderReadyWhenAllEvidencePasses()
    {
        var audit = FinalAuditBuilder.Build(new FinalAuditInputs
        {
            TemplatePath = "template.docx",
            ContentPath = "content.json",
            ProjectRulesPath = "project-rules.json",
            ReferencePath = "reference.docx",
            OutputPath = "final.docx",
            AfterFinalizeValidation = new ValidationReport { Compliant = true },
            HostFinalization = new HostApplicationReport { Executed = true },
            HostFinalizationCurrent = true,
            Rehearsal = new RehearsalComparisonReport
            {
                ReadyForFinalReview = true,
                ContentCoverage = new RehearsalContentCoverage
                {
                    HeadingCoverage = 1,
                    MissingReferenceParagraphCount = 0,
                    MissingReferenceTableCount = 0
                }
            }
        });

        AssertEqual(true, audit.FinalAudit.Ready);
        AssertEqual("ready", audit.FinalAudit.Readiness);
        AssertEqual(true, audit.RepairPlan.Ready);
        AssertEqual(0, audit.RepairPlan.Items.Count);
    }
}
```

Register these tests in `tests/Thesis.Tests/TestCatalog.cs` near the rehearsal/finalize tests:

```csharp
("Final audit builder blocks on validation diagnostics", FinalAuditBuilderBlocksOnValidationDiagnostics),
("Final audit builder blocks on rehearsal content gaps", FinalAuditBuilderBlocksOnRehearsalContentGaps),
("Final audit builder marks no reference as reduced confidence", FinalAuditBuilderMarksNoReferenceAsReducedConfidence),
("Final audit builder ready when all evidence passes", FinalAuditBuilderReadyWhenAllEvidencePasses),
```

- [ ] **Step 2: Run the failing tests**

Run:

```powershell
dotnet build ThesisTool.slnx
```

Expected: compile failure because `FinalAuditBuilder` and `FinalAuditInputs` do not exist.

- [ ] **Step 3: Implement the builder**

Create `src/Thesis.Cli/FinalAuditBuilder.cs`:

```csharp
using Thesis.Host;
using Thesis.OpenXml;
using Thesis.Profile;
using Thesis.Schema;

namespace Thesis.Cli;

public sealed class FinalAuditInputs
{
    public string TemplatePath { get; set; } = "";

    public string ContentPath { get; set; } = "";

    public string ProjectRulesPath { get; set; } = "";

    public string? ReferencePath { get; set; }

    public string OutputPath { get; set; } = "";

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
                ["final"] = Path.GetFullPath(inputs.OutputPath)
            },
            Steps =
            [
                new FinalAuditStep { Id = "validateAfterFinalize", Status = inputs.AfterFinalizeValidation is null ? "missing" : "success" },
                new FinalAuditStep { Id = "hostFinalization", Status = inputs.HostFinalization is null ? "missing" : "success" },
                new FinalAuditStep { Id = "rehearsalCompare", Status = inputs.Rehearsal is null ? "skipped" : "success" }
            ]
        };

        if (!string.IsNullOrWhiteSpace(inputs.ReferencePath))
        {
            audit.Inputs["reference"] = Path.GetFullPath(inputs.ReferencePath);
        }

        AddValidationFindings(inputs, audit);
        AddHostFindings(inputs, audit);
        AddRehearsalFindings(inputs, audit);

        var repair = BuildRepairPlan(audit);
        audit.Ready = audit.Blocking.Count == 0
            && audit.AutoFixable.Count == 0
            && audit.RequiresWps.Count == 0
            && inputs.Rehearsal is not null
            && inputs.ReferencePath is not null;
        audit.Readiness = audit.Ready
            ? "ready"
            : audit.Blocking.Count > 0 || audit.AutoFixable.Count > 0 || audit.RequiresWps.Count > 0
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
        if (inputs.ReferencePath is null || inputs.Rehearsal is null)
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
    }

    private static RepairPlan BuildRepairPlan(FinalAuditReport audit)
    {
        var plan = new RepairPlan();
        foreach (var finding in audit.Blocking.Concat(audit.AutoFixable).Concat(audit.RequiresWps))
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
            "host_finalization_stale" => "Rerun finalize-all or finalize apply to refresh fields and pagination.",
            "host_finalization_missing" => "Open WPS/Word finalization support and rerun finalize-all.",
            _ when finding.Source == "validate" => "Apply the suggested validation operation or update final-rules.json, then rerun finalize-all.",
            _ => "Review the finding and rerun finalize-all after correction."
        };
    }
}
```

- [ ] **Step 4: Run builder tests**

Run:

```powershell
dotnet build ThesisTool.slnx
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: all tests pass, including the four new builder tests.

- [ ] **Step 5: Commit**

```powershell
git add src\Thesis.Cli\FinalAuditBuilder.cs tests\Thesis.Tests\TestCases\CliFinalizeAllAuditTests.cs tests\Thesis.Tests\TestCatalog.cs
git commit -m "feat: build final audit decisions"
```

## Task 3: Add Finalize-All CLI Argument Validation

**Files:**
- Create: `src/Thesis.Cli/FinalizeAllCommand.cs`
- Modify: `src/Thesis.Cli/ThesisCli.cs`
- Modify: `tests/Thesis.Tests/TestCases/CliFinalizeAllAuditTests.cs`
- Modify: `tests/Thesis.Tests/TestCatalog.cs`

- [ ] **Step 1: Write failing CLI validation tests**

Append these methods to `tests/Thesis.Tests/TestCases/CliFinalizeAllAuditTests.cs`:

```csharp
static void CliFinalizeAllValidatesRequiredArguments()
{
    var (exitCode, result) = RunCli(["finalize-all"]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("finalize_all_template_missing", result.Diagnostics[0].Code);
}

static void CliFinalizeAllRefusesUnsafeOutputPaths()
{
    using var temp = new TempDirectory();
    var template = Path.Combine(temp.Path, "template.docx");
    var content = Path.Combine(temp.Path, "content.json");
    var projectRules = Path.Combine(temp.Path, "project-rules.json");
    var workdir = Path.Combine(temp.Path, "run");

    WriteFixtureDocx(template);
    File.WriteAllText(content, """{"documentKind":"thesisContent","title":"论文题目"}""");
    File.WriteAllText(projectRules, """{"rulesKind":"projectRules"}""");

    var (exitCode, result) = RunCli([
        "finalize-all",
        "--template", template,
        "--content", content,
        "--project-rules", projectRules,
        "--out", template,
        "--workdir", workdir,
        "--skip-host-finalize"
    ]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual("finalize_all_output_refused", result.Diagnostics[0].Code);
}
```

Register:

```csharp
("CLI finalize-all validates required arguments", CliFinalizeAllValidatesRequiredArguments),
("CLI finalize-all refuses unsafe output paths", CliFinalizeAllRefusesUnsafeOutputPaths),
```

- [ ] **Step 2: Run the failing tests**

Run:

```powershell
dotnet build ThesisTool.slnx
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: tests fail because `finalize-all` is an unknown command.

- [ ] **Step 3: Implement parser and route**

Create `src/Thesis.Cli/FinalizeAllCommand.cs`:

```csharp
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

    public bool SkipHostFinalize { get; set; }
}

internal static class FinalizeAllCommand
{
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
                case "--skip-host-finalize":
                    options.SkipHostFinalize = true;
                    break;
                default:
                    return Error("finalize_all_unknown_option", $"Unknown finalize-all option: {arg}");
            }
        }

        return Validate(options);
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
        var inputs = new[]
        {
            options.TemplatePath,
            options.ContentPath,
            options.ProjectRulesPath,
            options.ReferencePath
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath);

        if (inputs.Any(input => string.Equals(input, output, StringComparison.OrdinalIgnoreCase)))
        {
            return Error("finalize_all_output_refused", "Finalize-all output path must not overwrite input files.");
        }

        return null;
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

    public static CliResult CreateParsedResult(FinalizeAllOptions options)
    {
        return new CliResult
        {
            Status = "success",
            Document = Path.GetFullPath(options.TemplatePath),
            OutputPath = Path.GetFullPath(options.OutputPath),
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "info",
                    Code = "finalize_all_parsed",
                    Message = "Finalize-all options were parsed and validated."
                }
            ]
        };
    }
}
```

Modify `src/Thesis.Cli/ThesisCli.cs` in `Dispatch` before the `validate` route:

```csharp
if (args is ["finalize-all", .. var finalizeAllArgs])
{
    return FinalizeAll(finalizeAllArgs);
}
```

Add this method near other command handlers. `ThesisCli.Run` already converts `CliResult.Status != "success"` into exit code `1`, so this handler returns `CliResult` rather than writing output directly:

```csharp
private static CliResult FinalizeAll(string[] args)
{
    try
    {
        var parseError = FinalizeAllCommand.TryParse(args, out var options);
        if (parseError is not null)
        {
            return parseError;
        }

        return FinalizeAllCommand.CreateParsedResult(options);
    }
    catch (ArgumentException ex)
    {
        return FinalizeAllCommand.Error(ex.Message, $"Missing value for {ex.Message}.");
    }
}
```

Add help usage line:

```csharp
"  finalize-all --template <template.docx> --content <content.json> --project-rules <project-rules.json> --out <final.docx> --workdir <dir> [--reference <reference.docx>]",
```

- [ ] **Step 4: Run validation tests**

Run:

```powershell
dotnet build ThesisTool.slnx
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: required argument and unsafe output tests pass; valid arguments return a parse-only success result until Task 4 adds full orchestration.

- [ ] **Step 5: Commit**

```powershell
git add src\Thesis.Cli\FinalizeAllCommand.cs src\Thesis.Cli\ThesisCli.cs tests\Thesis.Tests\TestCases\CliFinalizeAllAuditTests.cs tests\Thesis.Tests\TestCatalog.cs
git commit -m "feat: add finalize-all CLI validation"
```

## Task 4: Implement Offline Finalize-All Orchestration

**Files:**
- Modify: `src/Thesis.Cli/FinalizeAllCommand.cs`
- Modify: `src/Thesis.Cli/ThesisCli.cs`
- Modify: `tests/Thesis.Tests/TestCases/CliFinalizeAllAuditTests.cs`

- [ ] **Step 1: Write failing orchestration test without host finalization**

Append:

```csharp
static void CliFinalizeAllWritesArtifactsWithoutHostWhenSkipped()
{
    using var temp = new TempDirectory();
    var template = Path.Combine(temp.Path, "template.docx");
    var content = Path.Combine(temp.Path, "content.json");
    var projectRules = Path.Combine(temp.Path, "project-rules.json");
    var output = Path.Combine(temp.Path, "final.docx");
    var workdir = Path.Combine(temp.Path, "run");

    WriteFixtureDocx(template);
    File.WriteAllText(content, """{"documentKind":"thesisContent","title":"论文题目","chapters":[{"title":"第一章 绪论","paragraphs":["正文。"]}]}""");
    File.WriteAllText(projectRules, """{"rulesKind":"projectRules","schemaVersion":"1.0"}""");

    var (exitCode, result) = RunCli([
        "finalize-all",
        "--template", template,
        "--content", content,
        "--project-rules", projectRules,
        "--out", output,
        "--workdir", workdir,
        "--skip-host-finalize"
    ]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(Path.GetFullPath(output), result.OutputPath);
    AssertEqual(true, File.Exists(output));
    AssertEqual(true, File.Exists(Path.Combine(workdir, "profile.json")));
    AssertEqual(true, File.Exists(Path.Combine(workdir, "final-rules.json")));
    AssertEqual(true, File.Exists(Path.Combine(workdir, "assembled.docx")));
    AssertEqual(true, File.Exists(Path.Combine(workdir, "validate-before-finalize.json")));
    AssertEqual(true, File.Exists(Path.Combine(workdir, "validate-after-finalize.json")));
    AssertEqual(true, File.Exists(Path.Combine(workdir, "final-audit.json")));
    AssertEqual(true, File.Exists(Path.Combine(workdir, "repair-plan.json")));
    AssertEqual(true, File.Exists(Path.Combine(workdir, "manual-checklist.md")));
    AssertEqual(false, result.FinalAudit!.Ready);
    AssertEqual(true, result.FinalAudit.RequiresWps.Any(finding => finding.Id == "host_finalization_missing"));
}
```

Register:

```csharp
("CLI finalize-all writes artifacts without host when skipped", CliFinalizeAllWritesArtifactsWithoutHostWhenSkipped),
```

- [ ] **Step 2: Run the failing test**

Run:

```powershell
dotnet build ThesisTool.slnx
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: test fails because `final.docx`, `profile.json`, `final-rules.json`, and audit artifacts are not written by the parse-only Task 3 implementation.

- [ ] **Step 3: Implement orchestration**

In `src/Thesis.Cli/FinalizeAllCommand.cs`, add:

```csharp
public static CliResult Execute(FinalizeAllOptions options)
{
    Directory.CreateDirectory(options.Workdir);

    var profilePath = Path.Combine(options.Workdir, "profile.json");
    var finalRulesPath = Path.Combine(options.Workdir, "final-rules.json");
    var assembledPath = Path.Combine(options.Workdir, "assembled.docx");
    var validateBeforePath = Path.Combine(options.Workdir, "validate-before-finalize.json");
    var validateAfterPath = Path.Combine(options.Workdir, "validate-after-finalize.json");
    var finalAuditPath = Path.Combine(options.Workdir, "final-audit.json");
    var repairPlanPath = Path.Combine(options.Workdir, "repair-plan.json");
    var manualChecklistPath = Path.Combine(options.Workdir, "manual-checklist.md");

    if (!OpenXmlDocumentInspector.TryInspect(options.TemplatePath, out var templateMap, out var templateDiagnostic)
        || templateMap is null)
    {
        return new CliResult
        {
            Status = "error",
            Document = Path.GetFullPath(options.TemplatePath),
            Diagnostics = templateDiagnostic is null ? [] : [templateDiagnostic]
        };
    }

    var profile = TemplateProfileBuilder.Build(templateMap, "doc");
    File.WriteAllText(profilePath, ThesisJson.Serialize(profile));

    var projectRules = ThesisJson.Deserialize<ProjectRules>(File.ReadAllText(options.ProjectRulesPath));
    NormalizeProjectRules(projectRules);
    var merged = ProjectRulesMerger.Merge(profile, projectRules);
    File.WriteAllText(finalRulesPath, ThesisJson.Serialize(merged));

    var content = ThesisJson.Deserialize<ThesisContent>(File.ReadAllText(options.ContentPath));
    NormalizeContent(content);
    File.Copy(options.TemplatePath, assembledPath, overwrite: true);
    ThesisDocumentGenerator.AssembleIntoTemplate(content, merged, assembledPath);

    if (!OpenXmlDocumentInspector.TryInspect(assembledPath, out var assembledMap, out var assembledDiagnostic)
        || assembledMap is null)
    {
        return new CliResult
        {
            Status = "error",
            Document = Path.GetFullPath(options.TemplatePath),
            OutputPath = Path.GetFullPath(options.OutputPath),
            Diagnostics = assembledDiagnostic is null ? [] : [assembledDiagnostic]
        };
    }

    var validateBefore = ProfileComplianceValidator.Validate(assembledMap, merged);
    File.WriteAllText(validateBeforePath, ThesisJson.Serialize(validateBefore));

    HostApplicationReport? hostReport;
    var finalPath = Path.GetFullPath(options.OutputPath);
    if (options.SkipHostFinalize)
    {
        File.Copy(assembledPath, finalPath, overwrite: true);
        hostReport = null;
    }
    else
    {
        File.Copy(assembledPath, finalPath, overwrite: true);
        hostReport = new WpsComAutomationHost().FinalizeDocument(
            finalPath,
            new HostApplicationOptions { Action = "finalize", RequestedHost = "wps" });
        OpenXmlFinalizationMetadata.MarkHostFinalized(
            finalPath,
            hostReport,
            FinalizationPlanBuilder.Build(assembledMap).Reasons);
    }

    if (!OpenXmlDocumentInspector.TryInspect(finalPath, out var finalMap, out var finalDiagnostic)
        || finalMap is null)
    {
        return new CliResult
        {
            Status = "error",
            Document = Path.GetFullPath(options.TemplatePath),
            OutputPath = finalPath,
            Diagnostics = finalDiagnostic is null ? [] : [finalDiagnostic]
        };
    }

    var validateAfter = ProfileComplianceValidator.Validate(finalMap, merged);
    File.WriteAllText(validateAfterPath, ThesisJson.Serialize(validateAfter));

    var auditResult = FinalAuditBuilder.Build(new FinalAuditInputs
    {
        TemplatePath = options.TemplatePath,
        ContentPath = options.ContentPath,
        ProjectRulesPath = options.ProjectRulesPath,
        ReferencePath = options.ReferencePath,
        OutputPath = finalPath,
        AfterFinalizeValidation = validateAfter,
        HostFinalization = hostReport,
        HostFinalizationCurrent = finalMap.HostFinalization?.IsCurrent == true,
        Rehearsal = null
    });

    File.WriteAllText(finalAuditPath, ThesisJson.Serialize(auditResult.FinalAudit));
    File.WriteAllText(repairPlanPath, ThesisJson.Serialize(auditResult.RepairPlan));
    File.WriteAllText(manualChecklistPath, ManualChecklist(auditResult.FinalAudit));

    return new CliResult
    {
        Status = "success",
        Document = Path.GetFullPath(options.TemplatePath),
        OutputPath = finalPath,
        Validation = validateAfter,
        HostApplication = hostReport,
        FinalAudit = auditResult.FinalAudit,
        RepairPlan = auditResult.RepairPlan
    };
}

private static void NormalizeProjectRules(ProjectRules rules)
{
    rules.RoleAliases ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    rules.RoleFormats ??= new Dictionary<string, ProjectParagraphFormatRule>(StringComparer.OrdinalIgnoreCase);
    rules.RolePolicies ??= [];
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
```

Modify `ThesisCli.FinalizeAll` to call `Execute`:

```csharp
var parseError = FinalizeAllCommand.TryParse(args, out var options);
if (parseError is not null)
{
    return parseError;
}

return FinalizeAllCommand.Execute(options);
```

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet build ThesisTool.slnx
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: new offline finalize-all test passes with exit code `0`, JSON status `success`, `finalAudit.ready=false`, and all artifacts written. The existing `ThesisCli.Run` maps only `Status != "success"` to exit code `1`; final-draft readiness is expressed by `finalAudit.ready`, not the process exit code.

- [ ] **Step 5: Commit**

```powershell
git add src\Thesis.Cli\FinalizeAllCommand.cs src\Thesis.Cli\ThesisCli.cs tests\Thesis.Tests\TestCases\CliFinalizeAllAuditTests.cs
git commit -m "feat: orchestrate finalize-all artifacts"
```

## Task 5: Add Reference-Backed Rehearsal Integration

**Files:**
- Modify: `src/Thesis.Cli/FinalizeAllCommand.cs`
- Modify: `tests/Thesis.Tests/TestCases/CliFinalizeAllAuditTests.cs`

- [ ] **Step 1: Write failing reference-gap test**

Append:

```csharp
static void CliFinalizeAllUsesReferenceForBlockingContentGaps()
{
    using var temp = new TempDirectory();
    var template = Path.Combine(temp.Path, "template.docx");
    var reference = Path.Combine(temp.Path, "reference.docx");
    var content = Path.Combine(temp.Path, "content.json");
    var projectRules = Path.Combine(temp.Path, "project-rules.json");
    var output = Path.Combine(temp.Path, "final.docx");
    var workdir = Path.Combine(temp.Path, "run");

    WriteFixtureDocx(template);
    WriteSimpleDocx(reference, """
    <w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
    <w:p><w:r><w:t>正文。</w:t></w:r></w:p>
    <w:p><w:r><w:t>第二章 系统设计</w:t></w:r></w:p>
    <w:p><w:r><w:t>系统设计正文缺失。</w:t></w:r></w:p>
    """);
    File.WriteAllText(content, """{"documentKind":"thesisContent","title":"论文题目","chapters":[{"title":"第一章 绪论","paragraphs":["正文。"]}]}""");
    File.WriteAllText(projectRules, """{"rulesKind":"projectRules","schemaVersion":"1.0"}""");

    var (exitCode, result) = RunCli([
        "finalize-all",
        "--template", template,
        "--content", content,
        "--project-rules", projectRules,
        "--reference", reference,
        "--out", output,
        "--workdir", workdir,
        "--skip-host-finalize"
    ]);

    AssertEqual(0, exitCode);
    AssertEqual("success", result.Status);
    AssertEqual(true, File.Exists(Path.Combine(workdir, "rehearsal-report.json")));
    AssertEqual(true, result.FinalAudit!.Blocking.Any(finding => finding.Id == "missing_reference_content"));
    AssertEqual(true, result.RehearsalComparison!.ContentCoverage.MissingReferenceParagraphCount > 0);
}
```

Register:

```csharp
("CLI finalize-all uses reference for blocking content gaps", CliFinalizeAllUsesReferenceForBlockingContentGaps),
```

- [ ] **Step 2: Run failing test**

Run:

```powershell
dotnet build ThesisTool.slnx
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: test fails because `rehearsal-report.json` is not written and `RehearsalComparison` is null.

- [ ] **Step 3: Invoke rehearsal comparison in finalize-all**

In `FinalizeAllCommand.Execute`, add before audit building:

```csharp
RehearsalComparisonReport? rehearsal = null;
var rehearsalPath = Path.Combine(options.Workdir, "rehearsal-report.json");
if (!string.IsNullOrWhiteSpace(options.ReferencePath))
{
    if (!OpenXmlDocumentInspector.TryInspect(options.ReferencePath, out var referenceMap, out var referenceDiagnostic)
        || referenceMap is null)
    {
        return new CliResult
        {
            Status = "error",
            Document = Path.GetFullPath(options.TemplatePath),
            OutputPath = finalPath,
            Diagnostics = referenceDiagnostic is null ? [] : [referenceDiagnostic]
        };
    }

    rehearsal = RehearsalComparisonBuilder.Build(finalMap, referenceMap, validateAfter);
    File.WriteAllText(rehearsalPath, ThesisJson.Serialize(rehearsal));
}
```

Pass `Rehearsal = rehearsal` into `FinalAuditInputs`, and set `CliResult.RehearsalComparison = rehearsal`.

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet build ThesisTool.slnx
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: reference-backed finalize-all test passes.

- [ ] **Step 5: Commit**

```powershell
git add src\Thesis.Cli\FinalizeAllCommand.cs tests\Thesis.Tests\TestCases\CliFinalizeAllAuditTests.cs
git commit -m "feat: include rehearsal in finalize-all audit"
```

## Task 6: Add Host-Failure Cleanup And WPS Boundary

**Files:**
- Modify: `src/Thesis.Cli/FinalizeAllCommand.cs`
- Modify: `tests/Thesis.Tests/TestCases/CliFinalizeAllAuditTests.cs`

- [ ] **Step 1: Write failing host unavailable test**

Append:

```csharp
static void CliFinalizeAllReportsUnavailableHostAndKeepsAuditArtifacts()
{
    using var temp = new TempDirectory();
    var template = Path.Combine(temp.Path, "template.docx");
    var content = Path.Combine(temp.Path, "content.json");
    var projectRules = Path.Combine(temp.Path, "project-rules.json");
    var output = Path.Combine(temp.Path, "final.docx");
    var workdir = Path.Combine(temp.Path, "run");

    WriteFixtureDocx(template);
    File.WriteAllText(content, """{"documentKind":"thesisContent","title":"论文题目","chapters":[{"title":"第一章 绪论","paragraphs":["正文。"]}]}""");
    File.WriteAllText(projectRules, """{"rulesKind":"projectRules","schemaVersion":"1.0"}""");

    var (exitCode, result) = RunCli([
        "finalize-all",
        "--template", template,
        "--content", content,
        "--project-rules", projectRules,
        "--out", output,
        "--workdir", workdir,
        "--prog-id", "Thesis.Tests.MissingComHost"
    ]);

    AssertEqual(1, exitCode);
    AssertEqual("error", result.Status);
    AssertEqual(true, result.Diagnostics.Any(diagnostic => diagnostic.Code == "host_application_unavailable"));
    AssertEqual(false, File.Exists(output));
    AssertEqual(true, File.Exists(Path.Combine(workdir, "assembled.docx")));
}
```

Register:

```csharp
("CLI finalize-all reports unavailable host and keeps audit artifacts", CliFinalizeAllReportsUnavailableHostAndKeepsAuditArtifacts),
```

- [ ] **Step 2: Extend parser for host options**

Add to `FinalizeAllOptions`:

```csharp
public string RequestedHost { get; set; } = "wps";

public string? ProgId { get; set; }
```

Add parser cases:

```csharp
case "--host":
    options.RequestedHost = Next("finalize_all_host_missing");
    break;
case "--prog-id":
    options.ProgId = Next("finalize_all_prog_id_missing");
    break;
```

- [ ] **Step 3: Implement host failure behavior**

When building `HostApplicationOptions`, use:

```csharp
new HostApplicationOptions
{
    Action = "finalize",
    RequestedHost = options.RequestedHost,
    ProgId = options.ProgId
}
```

Wrap host execution:

```csharp
try
{
    hostReport = new WpsComAutomationHost().FinalizeDocument(finalPath, hostOptions);
    OpenXmlFinalizationMetadata.MarkHostFinalized(
        finalPath,
        hostReport,
        FinalizationPlanBuilder.Build(assembledMap).Reasons);
}
catch (HostApplicationException ex)
{
    if (File.Exists(finalPath))
    {
        File.Delete(finalPath);
    }

    var error = Error(ex.Code, ex.Message);
    error.Document = Path.GetFullPath(options.TemplatePath);
    error.OutputPath = finalPath;
    return error;
}
```

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet build ThesisTool.slnx
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected: unavailable host test passes and existing finalize host tests remain green.

- [ ] **Step 5: Commit**

```powershell
git add src\Thesis.Cli\FinalizeAllCommand.cs tests\Thesis.Tests\TestCases\CliFinalizeAllAuditTests.cs
git commit -m "feat: handle finalize-all host failures"
```

## Task 7: Document The Command And Run Full Verification

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-05-13-finalize-all-audit.md` only if implementation details changed during execution.

- [ ] **Step 1: Update README final-draft section**

Add this after the recommended final-draft flow in `README.md`:

```markdown
### 一键终稿候选

完成规则和正文准备后，可以使用 `finalize-all` 串起提取、合并、装配、最终化、校验、对比和审计：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize-all `
  --template "论文正文格式.docx" `
  --content ".analysis\content.json" `
  --project-rules ".analysis\project-rules.json" `
  --reference "成品论文.docx" `
  --out ".analysis\final.docx" `
  --workdir ".analysis\final-run"
```

输出目录会包含 `profile.json`、`final-rules.json`、`validate-before-finalize.json`、`validate-after-finalize.json`、`rehearsal-report.json`、`final-audit.json`、`repair-plan.json` 和 `manual-checklist.md`。`final-audit.json` 的 `ready=true` 才表示工具掌握的自动检查均已通过；真实分页、跨页表格、续表标题和视觉细节仍以 `manual-checklist.md` 为准。
```
```

- [ ] **Step 2: Run full verification**

Run:

```powershell
dotnet build ThesisTool.slnx
dotnet run --no-build --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected:

```text
已成功生成。
0 个警告
0 个错误
...
PASS CLI finalize-all writes artifacts without host when skipped
PASS CLI finalize-all uses reference for blocking content gaps
PASS CLI finalize-all reports unavailable host and keeps audit artifacts
```

- [ ] **Step 3: Run a lizi dry regression without host**

Use existing `lizi` files and skip host finalization so the regression is deterministic:

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize-all `
  --template "lizi\论文正文格式.docx" `
  --content ".analysis\lizi-md-content.json" `
  --project-rules ".analysis\lizi-real-project-rules.json" `
  --reference "lizi\论文_信安2201_2022010082_陶与柯_工业控制系统（ICS）安全防护方案设计与验证.docx" `
  --out ".analysis\finalize-all-lizi-final.docx" `
  --workdir ".analysis\finalize-all-lizi-run" `
  --skip-host-finalize
```

Expected: command exits `0` with JSON `status="success"` even when `ready=false` because host finalization was skipped. It writes `.analysis\finalize-all-lizi-run\final-audit.json`, `.analysis\finalize-all-lizi-run\repair-plan.json`, and `.analysis\finalize-all-lizi-run\manual-checklist.md`. Readiness is expressed in `final-audit.json`, not by the process exit code.

- [ ] **Step 4: Inspect lizi audit**

Run:

```powershell
Get-Content -LiteralPath ".analysis\finalize-all-lizi-run\final-audit.json"
```

Expected: audit JSON includes `requiresWps` with `host_finalization_missing`, and no unhandled exception text.

- [ ] **Step 5: Commit**

```powershell
git add README.md
git commit -m "docs: document finalize-all flow"
```

## Self-Review Checklist

- Spec coverage: tasks cover schema, CLI command, orchestration, audit readiness, repair plan, WPS boundary, reference rehearsal, README, and lizi regression.
- Placeholder scan: the plan contains no `TBD`, `TODO`, or empty implementation steps.
- Type consistency: `FinalAuditReport`, `FinalAuditFinding`, `RepairPlan`, `FinalAuditBuilder`, `FinalAuditInputs`, and `FinalizeAllCommand` names are consistent across tests and implementation tasks.
- API consistency: the plan uses current project APIs directly: `OpenXmlDocumentInspector.TryInspect`, `TemplateProfileBuilder.Build(DocumentMap, string)`, `ProjectRulesMerger.Merge`, `ThesisDocumentGenerator.AssembleIntoTemplate`, `ProfileComplianceValidator.Validate(DocumentMap, TemplateProfile)`, `new WpsComAutomationHost().FinalizeDocument`, `OpenXmlFinalizationMetadata.MarkHostFinalized`, and `RehearsalComparisonBuilder.Build(DocumentMap, DocumentMap, ValidationReport?)`.
