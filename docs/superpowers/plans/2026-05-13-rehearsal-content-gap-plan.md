# Rehearsal Content Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enhance `rehearsal compare` so it reports actionable missing paragraph/table gaps, not only aggregate count differences.

**Architecture:** Extend `RehearsalContentCoverage` with gap counts and structured gap entries. Add comparison helpers inside `RehearsalComparisonBuilder` that normalize text, skip headings/TOC/table captions/field instructions, scope comparison to the thesis body start, infer nearest heading context, and compare candidate/reference body paragraphs and table previews. Keep the existing CLI command and JSON shape backward compatible by only adding fields.

**Tech Stack:** .NET 10, existing OpenXML inspector, existing custom `Thesis.Tests` harness.

---

### Task 1: Schema And Failing Tests

**Files:**
- Modify: `src/Thesis.Schema/RehearsalModels.cs`
- Modify: `tests/Thesis.Tests/TestCases/CliRehearsalCompareTests.cs`

- [x] Add `RehearsalContentGap` model and new `RehearsalContentCoverage` fields.
- [x] Update `CliRehearsalCompareReportsCandidateGaps` to assert missing paragraph/table gap details.
- [x] Run `dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj` and verify failure before implementation.

### Task 2: Gap Detection

**Files:**
- Modify: `src/Thesis.Cli/RehearsalComparisonBuilder.cs`
- Modify: `src/Thesis.Schema/DocumentMapModels.cs`
- Modify: `src/Thesis.OpenXml/OpenXmlDocumentInspector.cs`

- [x] Add paragraph gap detection for reference body paragraphs not represented in candidate body paragraphs.
- [x] Add table gap detection for reference table previews not represented in candidate tables.
- [x] Cap reported gap list to a small actionable set while preserving total missing counts.
- [x] Add body element positions and scope content gaps to the first abstract/chapter heading so cover, task-book, declaration, authorization, and other frontmatter forms do not pollute body gap results.
- [x] Strip Word/WPS TOC and reference field instructions before comparison.
- [x] Run `dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj` and verify pass.

### Task 3: Docs, Verification, Commit

**Files:**
- Modify: `README.md`
- Modify: `Thesis/thesis-docx/SKILL.md`
- Add: `docs/superpowers/specs/2026-05-13-rehearsal-content-gap-design.md`
- Add: `docs/superpowers/plans/2026-05-13-rehearsal-content-gap-plan.md`

- [x] Document `contentCoverage.gaps`.
- [x] Run `dotnet build ThesisTool.slnx`.
- [x] Run lizi `rehearsal compare` against the latest finalized candidate and inspect gap output.
- [ ] Commit and push.
