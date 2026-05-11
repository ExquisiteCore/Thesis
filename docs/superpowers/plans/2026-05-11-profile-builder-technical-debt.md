# Profile Builder Technical Debt Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `Thesis.Profile` profile construction internals so direct-format inference and diagnostics are focused components while preserving current behavior.

**Architecture:** `TemplateProfileBuilder` remains the public composition entry point. Direct-format policy inference moves to an internal builder, diagnostics move to an internal builder, and small shared profile predicates/helpers are extracted only where needed to prevent duplication.

**Tech Stack:** C#/.NET 10, DocumentFormat.OpenXml via existing `Thesis.OpenXml`, existing custom `Thesis.Tests` harness.

---

### Task 1: Baseline Contract

**Files:**
- Read: `src/Thesis.Profile/TemplateProfileBuilder.cs`
- Read: `tests/Thesis.Tests/Program.cs`

- [ ] **Step 1: Confirm clean workspace**

Run: `git status -sb`

Expected: no modified tracked source files except the plan/spec commits already made.

- [ ] **Step 2: Run profile-related test harness**

Run: `dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj`

Expected: all tests print `PASS`, especially:

- `Template profile builder infers role policies`
- `Template profile builder infers direct format roles without semantic styles`
- `Template profile builder reports weak profile diagnostics`
- `CLI profile extract writes template profile from DOCX`

- [ ] **Step 3: Do not add tests yet**

This is a behavior-preserving refactor. The existing tests already lock the intended contract. Add tests only if the refactor exposes an untested helper through existing behavior.

---

### Task 2: Extract Direct-Format Role Policy Inference

**Files:**
- Create: `src/Thesis.Profile/DirectFormatRolePolicyBuilder.cs`
- Modify: `src/Thesis.Profile/TemplateProfileBuilder.cs`

- [ ] **Step 1: Create `DirectFormatRolePolicyBuilder`**

Add this internal class in `src/Thesis.Profile/DirectFormatRolePolicyBuilder.cs`:

```csharp
using System.Text.RegularExpressions;
using Thesis.Schema;

namespace Thesis.Profile;

internal static class DirectFormatRolePolicyBuilder
{
    public static void AddDirectFormatRolePolicies(List<ProfileRolePolicy> policies, DocumentMap map)
    {
        AddDirectFormatRolePolicy(
            policies,
            map,
            "heading1",
            105,
            0.76,
            ProfileTextHeuristics.IsDirectHeading1,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))第[一二三四五六七八九十百千万零〇两0-9Xx]+章(?![\d.．、])\s*.*$");
        AddDirectFormatRolePolicy(
            policies,
            map,
            "heading2",
            85,
            0.74,
            ProfileTextHeuristics.IsDirectHeading2,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))\d{1,2}\.\d{1,2}(?!\.)\s+.*$");
        AddDirectFormatRolePolicy(
            policies,
            map,
            "heading3",
            75,
            0.72,
            ProfileTextHeuristics.IsDirectHeading3,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))\d{1,2}\.\d{1,2}\.\d{1,2}(?!\.)\s+.*$");
        AddDirectFormatRolePolicy(
            policies,
            map,
            "body",
            15,
            0.68,
            ProfileTextHeuristics.IsDirectBody,
            @"^(?!.*(?:\t|…|\.{3,}|[.．·]{3,}))(?!\s*(?:第[一二三四五六七八九十百千万零〇两0-9Xx]+章\b|\d{1,2}\.\d{1,2}|摘要\b|Abstract\b|目录\b|参考文献\b|注：|\d+、|\[序号\])).{8,}$");
    }

    private static void AddDirectFormatRolePolicy(
        List<ProfileRolePolicy> policies,
        DocumentMap map,
        string role,
        int priority,
        double confidence,
        Func<DocumentParagraph, bool> predicate,
        string textPattern)
    {
        var paragraph = map.Paragraphs.FirstOrDefault(predicate);
        if (paragraph is null)
        {
            return;
        }

        if (policies.Any(policy =>
            string.Equals(policy.Role, role, StringComparison.Ordinal)
            && policy.Format is not null
            && ProfileFormatComparison.IsSamePolicyFormat(policy.Format, paragraph.Format)))
        {
            return;
        }

        policies.Add(new ProfileRolePolicy
        {
            Role = role,
            AppliesTo = "paragraph",
            Priority = priority,
            Confidence = confidence,
            Match = new ProfileRoleMatch
            {
                TextPatterns = [textPattern],
                OutlineLevels = paragraph.OutlineLevel.HasValue ? [paragraph.OutlineLevel.Value] : []
            },
            Format = ProfileSampleCloner.Clone(ProfileFormatComparison.NormalizePolicyFormat(paragraph.Format))
        });
    }
}
```

- [ ] **Step 2: Extract the helper dependencies**

Create or update internal helpers in `src/Thesis.Profile`:

`ProfileTextHeuristics` should contain:

- `IsChineseAbstractHeading(string text)`
- `IsEnglishAbstractHeading(string text)`
- `IsTocHeading(string text)`
- `IsReferencesHeading(string text)`
- `IsSpecialSemanticHeading(string text)`
- `IsDirectHeading1(DocumentParagraph paragraph)`
- `IsDirectHeading2(DocumentParagraph paragraph)`
- `IsDirectHeading3(DocumentParagraph paragraph)`
- `IsDirectBody(DocumentParagraph paragraph)`
- `IsLikelyTocLine(string text)`
- `NormalizeHeading(string text)`
- `CreateExactTextPattern(string text)`

`ProfileFormatComparison` should contain:

- `NormalizePolicyFormat(ParagraphFormatSample format)`
- `IsSamePolicyFormat(ParagraphFormatSample left, ParagraphFormatSample right)`
- `IsSameRunFormat(RunFormatSample? left, RunFormatSample? right)`

`ProfileSampleCloner` should contain the existing `Clone(...)` overloads needed by both `TemplateProfileBuilder` and the new classes.

Keep method bodies byte-for-byte equivalent where practical.

- [ ] **Step 3: Replace the old call site**

In `src/Thesis.Profile/TemplateProfileBuilder.cs`, replace:

```csharp
AddDirectFormatRolePolicies(policies, map);
```

with:

```csharp
DirectFormatRolePolicyBuilder.AddDirectFormatRolePolicies(policies, map);
```

- [ ] **Step 4: Remove moved private methods**

Remove the direct-format policy methods and moved helper methods from `TemplateProfileBuilder`. Update remaining calls:

- `Clone(...)` becomes `ProfileSampleCloner.Clone(...)`
- `IsChineseAbstractHeading(...)` becomes `ProfileTextHeuristics.IsChineseAbstractHeading(...)`
- `IsEnglishAbstractHeading(...)` becomes `ProfileTextHeuristics.IsEnglishAbstractHeading(...)`
- `IsTocHeading(...)` becomes `ProfileTextHeuristics.IsTocHeading(...)`
- `IsReferencesHeading(...)` becomes `ProfileTextHeuristics.IsReferencesHeading(...)`
- `CreateExactTextPattern(...)` becomes `ProfileTextHeuristics.CreateExactTextPattern(...)`

- [ ] **Step 5: Run tests**

Run: `dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj`

Expected: all tests pass.

---

### Task 3: Extract Template Profile Diagnostics

**Files:**
- Create: `src/Thesis.Profile/TemplateProfileDiagnosticsBuilder.cs`
- Modify: `src/Thesis.Profile/TemplateProfileBuilder.cs`

- [ ] **Step 1: Create `TemplateProfileDiagnosticsBuilder`**

Move diagnostics construction into `src/Thesis.Profile/TemplateProfileDiagnosticsBuilder.cs`:

```csharp
using Thesis.Schema;

namespace Thesis.Profile;

internal static class TemplateProfileDiagnosticsBuilder
{
    public static List<ProfileDiagnostic> Build(DocumentMap map)
    {
        var diagnostics = new List<ProfileDiagnostic>();

        if (!map.Paragraphs.Any(paragraph => ProfileTextHeuristics.IsChineseAbstractHeading(paragraph.Text)))
        {
            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "warning",
                Code = "profile_role_missing",
                Message = "Chinese abstract heading was not found.",
                Evidence = ["role:abstract.zh"]
            });
        }

        if (!map.Paragraphs.Any(paragraph => ProfileTextHeuristics.IsReferencesHeading(paragraph.Text)))
        {
            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "warning",
                Code = "profile_role_missing",
                Message = "References heading was not found.",
                Evidence = ["role:references"]
            });
        }

        if (map.Tables.Count == 0)
        {
            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "info",
                Code = "profile_table_missing",
                Message = "No table samples were found in the source document.",
                Evidence = ["tables:0"]
            });
        }

        AddDirectFormatDiagnostics(diagnostics, map);
        AddAmbiguousStyleDiagnostics(diagnostics, map);
        return diagnostics;
    }

    private static void AddDirectFormatDiagnostics(List<ProfileDiagnostic> diagnostics, DocumentMap map)
    {
        AddDirectFormatDiagnostic(diagnostics, map, "heading1", ProfileTextHeuristics.IsDirectHeading1);
        AddDirectFormatDiagnostic(diagnostics, map, "heading2", ProfileTextHeuristics.IsDirectHeading2);
        AddDirectFormatDiagnostic(diagnostics, map, "heading3", ProfileTextHeuristics.IsDirectHeading3);
        AddDirectFormatDiagnostic(diagnostics, map, "body", ProfileTextHeuristics.IsDirectBody);
    }

    private static void AddDirectFormatDiagnostic(
        List<ProfileDiagnostic> diagnostics,
        DocumentMap map,
        string role,
        Func<DocumentParagraph, bool> predicate)
    {
        var paragraph = map.Paragraphs.FirstOrDefault(predicate);
        if (paragraph is null)
        {
            return;
        }

        diagnostics.Add(new ProfileDiagnostic
        {
            Severity = "info",
            Code = "profile_role_inferred",
            Message = $"{role} policy inferred from paragraph text and direct formatting.",
            Evidence =
            [
                $"role:{role}",
                $"paragraph:{paragraph.Index}",
                $"fontSize:{paragraph.Format.RunFormat?.FontSizeHalfPoints}"
            ]
        });
    }

    private static void AddAmbiguousStyleDiagnostics(List<ProfileDiagnostic> diagnostics, DocumentMap map)
    {
        foreach (var group in map.Paragraphs
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.StyleId))
            .GroupBy(paragraph => paragraph.StyleId!, StringComparer.OrdinalIgnoreCase))
        {
            var detectedRoles = new List<string>();
            if (group.Any(ProfileTextHeuristics.IsDirectHeading1))
            {
                detectedRoles.Add("heading1");
            }

            if (group.Any(ProfileTextHeuristics.IsDirectHeading2))
            {
                detectedRoles.Add("heading2");
            }

            if (group.Any(ProfileTextHeuristics.IsDirectHeading3))
            {
                detectedRoles.Add("heading3");
            }

            if (group.Any(ProfileTextHeuristics.IsDirectBody))
            {
                detectedRoles.Add("body");
            }

            if (detectedRoles.Distinct(StringComparer.Ordinal).Count() < 2)
            {
                continue;
            }

            diagnostics.Add(new ProfileDiagnostic
            {
                Severity = "info",
                Code = "profile_style_ambiguous",
                Message = "A single paragraph style appears to carry multiple semantic roles; direct-format policies were used instead of style-only matching.",
                Evidence =
                [
                    $"style:{group.Key}",
                    "roles:" + string.Join(",", detectedRoles.Distinct(StringComparer.Ordinal))
                ]
            });
        }
    }
}
```

- [ ] **Step 2: Replace the old diagnostic call site**

In `TemplateProfileBuilder.Build(...)`, replace:

```csharp
Diagnostics = BuildDiagnostics(map),
```

with:

```csharp
Diagnostics = TemplateProfileDiagnosticsBuilder.Build(map),
```

- [ ] **Step 3: Remove old diagnostic private methods**

Remove `BuildDiagnostics`, `AddDirectFormatDiagnostics`, `AddDirectFormatDiagnostic`, and `AddAmbiguousStyleDiagnostics` from `TemplateProfileBuilder`.

- [ ] **Step 4: Run tests**

Run: `dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj`

Expected: all tests pass.

---

### Task 4: Final Regression and Commit

**Files:**
- Modify: only files touched by Tasks 2 and 3.

- [ ] **Step 1: Build solution**

Run: `dotnet build ThesisTool.slnx`

Expected: `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 2: Run full tests**

Run: `dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj`

Expected: all tests print `PASS`.

- [ ] **Step 3: Extract real sample profile**

Run: `dotnet run --project src\Thesis.Cli\Thesis.Cli.csproj -- profile extract --doc "论文正文格式.docx" --out ".analysis\论文正文格式.profile.json"`

Expected: JSON success output and `.analysis\论文正文格式.profile.json` exists.

- [ ] **Step 4: Check whitespace**

Run: `git diff --check`

Expected: no output.

- [ ] **Step 5: Review changed files**

Run: `git diff --stat`

Expected: new focused files under `src\Thesis.Profile`, reduced `TemplateProfileBuilder.cs`, no schema or OpenXML execution changes.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src\Thesis.Profile
git commit -m "refactor: split profile builder responsibilities"
```

Expected: commit succeeds.

- [ ] **Step 7: Push**

Run: `git push`

Expected: remote `main` receives the refactor commit.
