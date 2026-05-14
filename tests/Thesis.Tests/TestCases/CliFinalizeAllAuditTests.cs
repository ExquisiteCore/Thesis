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

    static void FinalAuditBuilderBlocksOnRehearsalDiagnostics()
    {
        var rehearsal = new RehearsalComparisonReport
        {
            ReadyForFinalReview = false,
            ContentCoverage = new RehearsalContentCoverage
            {
                HeadingCoverage = 1,
                MissingReferenceParagraphCount = 0,
                MissingReferenceTableCount = 0
            },
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "warning",
                    Code = "table_count_gap",
                    Message = "Candidate has fewer tables than the reference."
                }
            ]
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
        AssertEqual("blocked", audit.FinalAudit.Readiness);
        AssertEqual(true, audit.FinalAudit.RequiresHuman.Any(finding => finding.Id == "table_count_gap"));
        AssertEqual(true, audit.RepairPlan.Items.Any(item => item.IssueId == "table_count_gap"));
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
        AssertEqual(true, audit.RepairPlan.Items.Any(item =>
            item.IssueId == "reference_not_provided"
            && !item.Automatic
            && !item.RequiresWps));
    }

    static void FinalAuditBuilderBlocksWhenReferenceProvidedButRehearsalMissing()
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
            HostFinalizationCurrent = true
        });

        AssertEqual(false, audit.FinalAudit.Ready);
        AssertEqual("blocked", audit.FinalAudit.Readiness);
        AssertEqual(true, audit.FinalAudit.Blocking.Any(finding => finding.Id == "rehearsal_missing"));
        AssertEqual(false, audit.FinalAudit.RequiresHuman.Any(finding => finding.Id == "reference_not_provided"));
        AssertEqual(true, audit.RepairPlan.Items.Any(item => item.IssueId == "rehearsal_missing"));
    }

    static void FinalAuditBuilderBlocksWhenRehearsalNotReadyWithoutDiagnostics()
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
                ReadyForFinalReview = false,
                ContentCoverage = new RehearsalContentCoverage
                {
                    HeadingCoverage = 1,
                    MissingReferenceParagraphCount = 0,
                    MissingReferenceTableCount = 0
                }
            }
        });

        AssertEqual(false, audit.FinalAudit.Ready);
        AssertEqual("blocked", audit.FinalAudit.Readiness);
        AssertEqual(true, audit.FinalAudit.RequiresHuman.Any(finding => finding.Id == "rehearsal_not_ready"));
        AssertEqual(true, audit.RepairPlan.Items.Any(item => item.IssueId == "rehearsal_not_ready"));
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

    static void FinalAuditBuilderIncludesPipelineArtifactsAndSummaries()
    {
        var audit = FinalAuditBuilder.Build(new FinalAuditInputs
        {
            TemplatePath = "template.docx",
            ContentPath = "content.json",
            ProjectRulesPath = "project-rules.json",
            ReferencePath = "reference.docx",
            OutputPath = "final.docx",
            ProfilePath = "profile.json",
            FinalRulesPath = "final-rules.json",
            AssembledPath = "assembled.docx",
            ValidateBeforePath = "validate-before-finalize.json",
            HostFinalizationPath = "host-finalization.json",
            ValidateAfterPath = "validate-after-finalize.json",
            RehearsalPath = "rehearsal-report.json",
            FinalAuditPath = "final-audit.json",
            RepairPlanPath = "repair-plan.json",
            ManualChecklistPath = "manual-checklist.md",
            BeforeFinalizeValidation = new ValidationReport
            {
                Compliant = true,
                CheckedParagraphs = 3,
                CheckedTables = 1
            },
            AfterFinalizeValidation = new ValidationReport
            {
                Compliant = true,
                CheckedParagraphs = 4,
                CheckedTables = 2
            },
            HostFinalization = new HostApplicationReport
            {
                Executed = true,
                Layout = new HostLayoutMetrics
                {
                    PageCount = 12,
                    TableCount = 2,
                    TableOfContentsCount = 1
                }
            },
            HostFinalizationCurrent = true,
            Rehearsal = new RehearsalComparisonReport
            {
                ReadyForFinalReview = true,
                ContentCoverage = new RehearsalContentCoverage
                {
                    ReferenceHeadingCount = 2,
                    MatchedHeadingCount = 2,
                    HeadingCoverage = 1,
                    MissingReferenceParagraphCount = 0,
                    MissingReferenceTableCount = 0
                }
            }
        });

        AssertEqual(true, audit.FinalAudit.Outputs.ContainsKey("profile"));
        AssertEqual(true, audit.FinalAudit.Outputs.ContainsKey("hostFinalization"));
        AssertEqual(true, audit.FinalAudit.Outputs.ContainsKey("manualChecklist"));
        AssertEqual(true, audit.FinalAudit.Steps.Any(step => step.Id == "profileExtract" && step.Artifact.EndsWith("profile.json", StringComparison.Ordinal)));
        AssertEqual(true, audit.FinalAudit.Steps.Any(step => step.Id == "validateBeforeFinalize" && step.Status == "success"));
        AssertEqual(4, audit.FinalAudit.ValidationSummary!.After!.CheckedParagraphs);
        AssertEqual(12, audit.FinalAudit.HostSummary!.PageCount);
        AssertEqual(1, audit.FinalAudit.RehearsalSummary!.HeadingCoverage);
    }

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

    static void CliFinalizeAllRefusesOutputArtifactCollision()
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
            "--out", Path.Combine(workdir, "final-audit.json"),
            "--workdir", workdir,
            "--skip-host-finalize"
        ]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("finalize_all_output_refused", result.Diagnostics[0].Code);
    }

    static void CliFinalizeAllRefusesWorkdirArtifactInputCollision()
    {
        using var temp = new TempDirectory();
        var template = Path.Combine(temp.Path, "template.docx");
        var workdir = Path.Combine(temp.Path, "run");
        var content = Path.Combine(workdir, "profile.json");
        var projectRules = Path.Combine(temp.Path, "project-rules.json");
        var output = Path.Combine(temp.Path, "final.docx");

        Directory.CreateDirectory(workdir);
        WriteFixtureDocx(template);
        File.WriteAllText(content, """{"documentKind":"thesisContent","title":"论文题目"}""");
        File.WriteAllText(projectRules, """{"rulesKind":"projectRules"}""");

        var (exitCode, result) = RunCli([
            "finalize-all",
            "--template", template,
            "--content", content,
            "--project-rules", projectRules,
            "--out", output,
            "--workdir", workdir,
            "--skip-host-finalize"
        ]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("finalize_all_workdir_refused", result.Diagnostics[0].Code);
        AssertContains(File.ReadAllText(content), "thesisContent");
    }

    static void CliFinalizeAllHelpListsSkipHostFinalize()
    {
        var (exitCode, result) = RunCli(["finalize-all", "--help"]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertContains(result.Diagnostics[0].Message, "--skip-host-finalize");
    }

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

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual(Path.GetFullPath(output), result.OutputPath);
        AssertEqual(false, File.Exists(output));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "profile.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "final-rules.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "assembled.docx")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "candidate.docx")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "validate-before-finalize.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "validate-after-finalize.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "host-finalization.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "final-audit.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "repair-plan.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "manual-checklist.md")));
        AssertEqual(true, result.FinalAudit!.Outputs.ContainsKey("hostFinalization"));
        AssertEqual(true, result.FinalAudit.Steps.Any(step => step.Id == "assemble"));
        AssertEqual(false, result.FinalAudit.Ready);
        AssertEqual(true, result.FinalAudit.RequiresWps.Any(finding => finding.Id == "host_finalization_missing"));
    }

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

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual(false, File.Exists(output));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "candidate.docx")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "rehearsal-report.json")));
        AssertEqual(true, result.FinalAudit!.Blocking.Any(finding => finding.Id == "missing_reference_content"));
        AssertEqual(true, result.RehearsalComparison!.ContentCoverage.MissingReferenceParagraphCount > 0);
    }

    static void CliFinalizeAllPreservesExistingOutputWhenRehearsalBlocks()
    {
        using var temp = new TempDirectory();
        var template = Path.Combine(temp.Path, "template.docx");
        var reference = Path.Combine(temp.Path, "reference.docx");
        var content = Path.Combine(temp.Path, "content.json");
        var projectRules = Path.Combine(temp.Path, "project-rules.json");
        var output = Path.Combine(temp.Path, "final.docx");
        var workdir = Path.Combine(temp.Path, "run");

        WriteFixtureDocx(template);
        WriteSimpleDocx(output, """<w:p><w:r><w:t>旧终稿</w:t></w:r></w:p>""");
        var oldOutputBytes = File.ReadAllBytes(output);
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

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertBytesEqual(oldOutputBytes, File.ReadAllBytes(output));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "candidate.docx")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "rehearsal-report.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "final-audit.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "repair-plan.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "manual-checklist.md")));
        AssertEqual(true, result.FinalAudit!.Blocking.Any(finding => finding.Id == "missing_reference_content"));
    }

    static void CliFinalizeAllRehearsalArtifactsPointToCandidateWhenAuditBlocks()
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

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual(false, File.Exists(output));
        AssertEqual(Path.GetFullPath(Path.Combine(workdir, "candidate.docx")), result.RehearsalComparison!.CandidateDocument);

        var rehearsal = ThesisJson.Deserialize<RehearsalComparisonReport>(
            File.ReadAllText(Path.Combine(workdir, "rehearsal-report.json")));
        AssertEqual(Path.GetFullPath(Path.Combine(workdir, "candidate.docx")), rehearsal.CandidateDocument);
        AssertEqual(false, rehearsal.Diagnostics.Any(diagnostic =>
            diagnostic.Path is not null
            && diagnostic.Path.Contains(".finalize-all-", StringComparison.Ordinal)));
    }

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
        AssertEqual(true, File.Exists(Path.Combine(workdir, "validate-before-finalize.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "host-finalization.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "final-audit.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "repair-plan.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "manual-checklist.md")));
    }

    static void CliFinalizeAllPreservesExistingOutputWhenHostFails()
    {
        using var temp = new TempDirectory();
        var template = Path.Combine(temp.Path, "template.docx");
        var content = Path.Combine(temp.Path, "content.json");
        var projectRules = Path.Combine(temp.Path, "project-rules.json");
        var output = Path.Combine(temp.Path, "final.docx");
        var workdir = Path.Combine(temp.Path, "run");

        WriteFixtureDocx(template);
        WriteSimpleDocx(output, """<w:p><w:r><w:t>旧终稿</w:t></w:r></w:p>""");
        var oldOutputBytes = File.ReadAllBytes(output);
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
        AssertBytesEqual(oldOutputBytes, File.ReadAllBytes(output));
    }

    static void CliFinalizeAllPreservesExistingOutputWhenReferenceInvalid()
    {
        using var temp = new TempDirectory();
        var template = Path.Combine(temp.Path, "template.docx");
        var content = Path.Combine(temp.Path, "content.json");
        var projectRules = Path.Combine(temp.Path, "project-rules.json");
        var reference = Path.Combine(temp.Path, "missing-reference.docx");
        var output = Path.Combine(temp.Path, "final.docx");
        var workdir = Path.Combine(temp.Path, "run");

        WriteFixtureDocx(template);
        WriteSimpleDocx(output, """<w:p><w:r><w:t>旧终稿</w:t></w:r></w:p>""");
        var oldOutputBytes = File.ReadAllBytes(output);
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

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertBytesEqual(oldOutputBytes, File.ReadAllBytes(output));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "host-finalization.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "validate-after-finalize.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "final-audit.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "repair-plan.json")));
        AssertEqual(true, File.Exists(Path.Combine(workdir, "manual-checklist.md")));
        AssertEqual(true, result.FinalAudit!.Blocking.Any(finding => finding.Id == "rehearsal_missing"));
    }
}
