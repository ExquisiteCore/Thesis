# Section-Aware Assemble Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `assemble` 在真实论文模板上保留封面/前置页和正文节设置，只替换论文主体，避免把模板尾部格式说明带进候选终稿。

**Architecture:** `ThesisDocumentGenerator.AssembleIntoTemplate` 先在模板 body 中寻找论文主体起点，再定位主体结束节边界；找到边界时只重写该范围，找不到边界时回退到旧的整正文重写路径。测试用多节 DOCX fixture 覆盖封面保留、正文占位替换、模板尾部丢弃、页眉节关系保留。

**Tech Stack:** .NET 10, OpenXML SDK, custom `Thesis.Tests` harness, WPS finalization smoke test.

---

### Task 1: Section-Aware Assemble Behavior

**Files:**
- Modify: `src/Thesis.OpenXml/ThesisDocumentGenerator.cs`
- Modify: `tests/Thesis.Tests/Support/DocxFixtures.cs`
- Modify: `tests/Thesis.Tests/TestCases/CliAssembleCommandTests.cs`
- Modify: `tests/Thesis.Tests/TestCatalog.cs`

- [x] **Step 1: Add failing regression test**

Create a multi-section template fixture with:

```xml
<w:p><w:r><w:t>封面保留</w:t></w:r></w:p>
<w:p><w:pPr><w:sectPr>...rIdCoverHeader...</w:sectPr></w:pPr></w:p>
<w:p><w:r><w:t>摘要</w:t></w:r></w:p>
<w:p><w:r><w:t>模板摘要占位</w:t></w:r></w:p>
<w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
<w:p><w:r><w:t>模板正文占位</w:t></w:r></w:p>
<w:p><w:pPr><w:sectPr>...rIdBodyHeader...</w:sectPr></w:pPr></w:p>
<w:p><w:r><w:t>格式说明保留</w:t></w:r></w:p>
<w:sectPr>...rIdTailHeader...</w:sectPr>
```

Run:

```powershell
dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected before implementation: FAIL because the previous `assemble` path removes the cover and keeps only the last section.

- [x] **Step 2: Implement range replacement**

In `AssembleIntoTemplate`, call `TryReplaceTemplateThesisRange` before the fallback. The helper:

```csharp
var startIndex = FindThesisRangeStart(blocks);
var sectionBreakIndex = FindThesisRangeSectionBreak(blocks, startIndex.Value);
```

It preserves blocks before `startIndex`, appends generated thesis blocks, appends the selected body section break, and intentionally drops suffix blocks after that section break.

- [x] **Step 3: Keep old fallback**

When no thesis anchor or usable section break exists, keep the older behavior:

```csharp
var sectionProperties = body.Elements<SectionProperties>().LastOrDefault()?.CloneNode(deep: true) as SectionProperties
    ?? CreateSectionProperties(rules.PageSetup);
body.RemoveAllChildren();
AppendThesisContent(body, content, rules);
body.AppendChild(sectionProperties);
```

- [x] **Step 4: Verify targeted behavior**

Run:

```powershell
dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj
```

Expected after implementation: PASS, including the new assemble regression.

### Task 2: Docs And Skill Alignment

**Files:**
- Modify: `README.md`
- Modify: `Thesis/thesis-docx/SKILL.md`
- Modify: `docs/superpowers/plans/2026-05-13-section-aware-assemble-closure.md`

- [x] **Step 1: Replace stale assemble wording**

Change README text from “清空并重写正文主体，只保留末节页面设置” to the current behavior: copy template, preserve frontmatter before the first thesis anchor, replace the thesis body range, keep the selected body section/page setup, and discard template tail examples.

- [x] **Step 2: Document remaining production boundary**

State that WPS/Word finalization remains mandatory for fields, TOC, page count, section normalization, pagination, and manual spot checks.

- [x] **Step 3: Align the skill**

Add the same operational rule to `Thesis/thesis-docx/SKILL.md`, so future agents do not choose `generate` for formal final drafts and do not expect `assemble` to keep template instruction tails.

### Task 3: Review, Real Sample, Commit

**Files:**
- Read: `lizi/论文正文格式.docx`
- Read: `lizi/论文_信安2201_2022010082_陶与柯_工业控制系统（ICS）安全防护方案设计与验证.docx`
- Write: `.analysis/lizi-section-aware-assembled.docx`
- Write: `.analysis/lizi-section-aware-finalized.docx`

- [x] **Step 1: Run independent review**

Ask a review agent to inspect range detection, suffix discard, fallback behavior, and test assertions. Fix Critical and Important findings before commit.

- [x] **Step 2: Run full verification**

Run:

```powershell
dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj
dotnet build ThesisTool.slnx
dotnet run --no-build --project src\Thesis.Cli\Thesis.Cli.csproj -- assemble --doc "lizi\论文正文格式.docx" --content ".analysis\lizi-md-content.json" --profile ".analysis\lizi-real-final-rules.json" --out ".analysis\lizi-section-aware-assembled.docx"
dotnet run --no-build --project src\Thesis.Cli\Thesis.Cli.csproj -- validate --doc ".analysis\lizi-section-aware-assembled.docx" --profile ".analysis\lizi-real-final-rules.json"
dotnet run --no-build --project src\Thesis.Cli\Thesis.Cli.csproj -- finalize apply --doc ".analysis\lizi-section-aware-assembled.docx" --out ".analysis\lizi-section-aware-finalized.docx" --host wps
dotnet run --no-build --project src\Thesis.Cli\Thesis.Cli.csproj -- validate --doc ".analysis\lizi-section-aware-finalized.docx" --profile ".analysis\lizi-real-final-rules.json"
```

Expected: tests and build exit 0; assembled/finalized docs validate; finalized doc no longer requires finalization.

- [ ] **Step 3: Commit and push**

Run:

```powershell
git diff --check
git status --short
git add src/Thesis.OpenXml/ThesisDocumentGenerator.cs tests/Thesis.Tests/Support/DocxFixtures.cs tests/Thesis.Tests/TestCases/CliAssembleCommandTests.cs tests/Thesis.Tests/TestCatalog.cs README.md Thesis/thesis-docx/SKILL.md docs/superpowers/plans/2026-05-13-section-aware-assemble-closure.md
git commit -m "feat: preserve template frontmatter during assemble"
git push
```
