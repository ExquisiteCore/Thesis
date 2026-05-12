# Thesis Rules Engine Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成 P0/P1/P2 收尾，使项目形成模板画像、项目规则补充、最终规则合并、论文生成和后续微调的闭环。

**Architecture:** 在 `Thesis.Schema` 增加项目规则与内容模型；在 `Thesis.Profile` 增加规则合并；在 `Thesis.OpenXml` 增加批注/规则线索检查和 DOCX 生成器；在 `Thesis.Cli` 暴露 `inspect --doc`、`rules merge`、`generate`。页面和 skill 只作为工作流入口，不承载核心逻辑。

**Tech Stack:** .NET 10, OpenXML SDK, existing no-framework HTML/JS profile viewer, existing custom test harness.

---

### Task 1: P0 Inspection And Rule Merge

**Files:**
- Modify: `src/Thesis.Schema/DocumentMapModels.cs`
- Modify: `src/Thesis.Schema/ProfileModels.cs`
- Create: `src/Thesis.Schema/ProjectRuleModels.cs`
- Modify: `src/Thesis.OpenXml/OpenXmlDocumentInspector.cs`
- Create: `src/Thesis.Profile/ProjectRulesMerger.cs`
- Modify: `src/Thesis.OpenXml/ProfileRoleResolver.cs`
- Modify: `src/Thesis.Cli/ThesisCli.cs`
- Modify: `tests/Thesis.Tests/TestCatalog.cs`
- Create: `tests/Thesis.Tests/TestCases/CliRulesCommandTests.cs`

- [ ] Add failing tests for `inspect --doc`, comment extraction, requirement hints, and `rules merge`.
- [ ] Add schema models for `ProjectRules`, `DocumentComment`, `DocumentRequirementHint`, and `TemplateProfile.RoleAliases`.
- [ ] Extend inspector to read `WordprocessingCommentsPart` and extract requirement hints from body paragraphs and comments.
- [ ] Implement `ProjectRulesMerger.Merge(profile, projectRules)`.
- [ ] Teach role resolver to read aliases from final profile and request overrides.
- [ ] Expose `inspect --doc` and `rules merge`.
- [ ] Run targeted tests.

### Task 2: P1 Content Generation

**Files:**
- Create: `src/Thesis.Schema/ThesisContentModels.cs`
- Create: `src/Thesis.OpenXml/ThesisDocumentGenerator.cs`
- Modify: `src/Thesis.Cli/ThesisCli.cs`
- Modify: `tests/Thesis.Tests/TestCatalog.cs`
- Create: `tests/Thesis.Tests/TestCases/CliGenerateCommandTests.cs`

- [ ] Add failing tests for `generate --content --rules --out`.
- [ ] Add content schema for title, abstracts, keywords, chapters, sections, tables, references, acknowledgements.
- [ ] Implement generator that creates a valid DOCX with page setup, headings, body paragraphs, references and basic tables.
- [ ] Expose `generate` command and validate output path safety.
- [ ] Run targeted tests.

### Task 3: P2 Workflow Docs And Viewer

**Files:**
- Modify: `README.md`
- Modify: `thesis-docx/SKILL.md`
- Modify: `profile-viewer/app.js`
- Modify: `profile-viewer/index.html`
- Modify: `profile-viewer/profile-viewer.test.mjs`

- [ ] Add failing viewer tests for project rules and final rules summary.
- [ ] Extend viewer summary to handle `projectRules`.
- [ ] Document the full P0->P1->P2 workflow.
- [ ] Run viewer tests and browser smoke test.

### Task 4: Verification And Commit

- [ ] Run `dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj`.
- [ ] Run `dotnet build ThesisTool.slnx`.
- [ ] Run `node .\profile-viewer\profile-viewer.test.mjs`.
- [ ] Smoke test real `lizi` profile with `rules merge` and `generate`.
- [ ] Commit and push.
