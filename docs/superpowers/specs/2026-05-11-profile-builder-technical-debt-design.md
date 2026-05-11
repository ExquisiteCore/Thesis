# Profile Builder Technical Debt Design

## Goal

Reduce technical debt in `Thesis.Profile` without changing CLI behavior, JSON schema, or extracted profile semantics.

The current `TemplateProfileBuilder` mixes profile orchestration, style role inference, direct-format role inference, diagnostics, table policy inference, evidence projection, and deep-copy helpers. That makes the next profile extraction work harder to review and increases the chance that heuristic changes accidentally affect unrelated output.

## Scope

This refactor is intentionally behavior-preserving.

In scope:

- Split direct-format role policy inference out of `TemplateProfileBuilder`.
- Split template profile diagnostics out of `TemplateProfileBuilder`.
- Move shared profile helper logic only when it directly supports those splits.
- Keep `TemplateProfileBuilder.Build(...)` as the public entry point.
- Keep current tests as the regression contract.

Out of scope:

- No `TemplateProfile` schema changes.
- No `ProfileRoleMatch` format-aware matching changes.
- No resolver or micro-editor behavior changes.
- No broad split of `OpenXmlMicroEditor`.
- No test harness rewrite.

## Design

`TemplateProfileBuilder` stays as the composition layer. It should read like a high-level profile assembly pipeline:

- page setup
- style roles
- role policies
- numbering policy
- table policy and archetypes
- diagnostics
- source evidence

Direct-format inference moves to a focused internal component, tentatively `DirectFormatRolePolicyBuilder`. It owns the heading/body heuristics and returns additional `ProfileRolePolicy` entries for `heading1`, `heading2`, `heading3`, and `body`.

Diagnostics move to a focused internal component, tentatively `TemplateProfileDiagnosticsBuilder`. It owns missing-role warnings, missing-table diagnostics, direct-format inference notes, and ambiguous-style diagnostics.

Any shared predicates or cloning helpers may move into small internal helpers if needed, but the first pass should prefer minimal movement over a full utility layer.

## Compatibility

The extracted profile JSON should remain compatible with the current tests and real sample document behavior.

Expected stable behavior:

- The same public CLI commands keep working.
- Existing profile role policy priorities and confidences remain unchanged.
- Direct-format policies still avoid broad `styleId=2` matching.
- Diagnostics keep the same codes and severity values.
- Existing tests remain green.

## Verification

Before implementation:

- `dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj`

After implementation:

- `dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj`
- `dotnet build ThesisTool.slnx`
- `dotnet run --project src\Thesis.Cli\Thesis.Cli.csproj -- profile extract --doc "论文正文格式.docx" --out ".analysis\论文正文格式.profile.json"`
- `git diff --check`

## Residual Debt

This does not fix deeper schema debt:

- `ProfileRoleMatch` still cannot match by formatting directly.
- Style outline inheritance through `basedOn` remains a separate OpenXML inspection improvement.
- The single-file custom test harness remains large.
- `OpenXmlMicroEditor` still needs a separate decomposition pass.
