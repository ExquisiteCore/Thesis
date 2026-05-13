# Finalize-All Audit Design

## Goal

Turn the thesis DOCX tool from a collection of document operations into an auditable final-draft production line.

The tool should not promise that AI can create a perfect thesis from nothing. Its formal target is narrower and more useful: given a school template or accepted thesis sample, structured thesis content, extracted profile rules, project-level rule supplements, and an optional reference document, it should produce a final-draft candidate plus evidence that explains whether the candidate is ready for final review.

## Current Fit

The project already has the main building blocks:

- `inspect` reads paragraphs, styles, numbering, sections, tables, comments, and requirement hints.
- `profile extract` creates `profile.json` from a template or sample.
- `rules merge` applies `project-rules.json` over the extracted profile.
- `assemble` writes structured thesis content into a template copy while preserving front matter and body section settings.
- `apply` and `writeBlock` support local edits where request parameters override rules.
- `validate` checks profile compliance and finalization state that can be seen offline.
- `finalize plan/apply` uses the host application for field and layout finalization.
- `rehearsal compare` compares a candidate with a reference thesis and reports content coverage gaps.

The missing product boundary is a single command and audit report that turn those pieces into a repeatable decision: can this generated document be treated as a final-draft candidate, and what still blocks delivery?

## Proposed Command

Add a high-level command:

```powershell
Thesis.Cli.exe finalize-all `
  --template "论文正文格式.docx" `
  --content ".analysis/content.json" `
  --project-rules ".analysis/project-rules.json" `
  --reference "成品论文.docx" `
  --out ".analysis/final.docx" `
  --workdir ".analysis/final-run"
```

`--reference` is optional, but when present it enables stronger coverage checks through `rehearsal compare`.

The command should write all intermediate artifacts into `--workdir` and refuse unsafe paths that overwrite inputs.

## Pipeline

`finalize-all` orchestrates existing capabilities rather than replacing them:

```text
inspect
-> profile extract
-> rules merge
-> assemble
-> validate
-> finalize apply
-> validate again
-> rehearsal compare
-> final audit
-> repair plan
```

The command should stop only on unrecoverable failures such as invalid input, unsafe output, malformed JSON, missing template, or a host finalization failure that leaves no valid output. Rule warnings and content gaps should be captured in the audit instead of being hidden in console text.

## Rule Priority

The final-draft flow preserves the existing priority model:

```text
single request parameters > project/final rules > profile.json > tool defaults
```

`profile.json` represents facts extracted from the template or accepted thesis sample. `project-rules.json` covers requirements that are present in template body text, comments, school instructions, or user policy but not visible as reusable styles. Per-operation request JSON remains the top priority for local overrides such as one special paragraph, title, table, or teacher feedback edit.

## Artifacts

Each successful run should produce:

- `final.docx`: the final-draft candidate.
- `profile.json`: extracted template or sample profile.
- `final-rules.json`: merged rules used to build the document.
- `validate-before-finalize.json`: offline validation after assembly.
- `host-finalization.json`: host application finalization report.
- `validate-after-finalize.json`: offline validation after host finalization.
- `rehearsal-report.json`: candidate/reference comparison when `--reference` is provided.
- `final-audit.json`: machine-readable readiness decision.
- `repair-plan.json`: structured next actions for auto-fixable or manual items.
- `manual-checklist.md`: human-readable checks that cannot be safely automated in v1.

## Final Audit Model

`final-audit.json` should be a first-class result, not a log dump. It should classify findings into four buckets:

- `blocking`: the document cannot be treated as a final-draft candidate until fixed.
- `autoFixable`: the tool can produce a concrete follow-up operation or repair action.
- `requiresWps`: the issue depends on Word/WPS layout state or host-only behavior.
- `requiresHuman`: the issue needs human review because v1 cannot verify it reliably.

The report should also include:

- input paths and output paths,
- command steps with status,
- rule files used,
- validation summary,
- host finalization summary,
- rehearsal summary when available,
- readiness boolean,
- short readiness explanation.

## Readiness Rules

`ready` should be true only when all of these hold:

- after-finalization validation is compliant,
- host finalization is current,
- no `blocking` findings exist,
- no unresolved `autoFixable` findings exist,
- if a reference is provided, `contentCoverage.gaps` is empty,
- if a reference is provided, `headingCoverage` is `1`,
- if a reference is provided, missing reference paragraph and table counts are zero,
- WPS-detectable checks have passed or are explicitly represented as non-blocking manual checks.

When no reference document is provided, the audit must say that content coverage confidence is reduced. It may still produce a final-draft candidate, but it should not imply the same confidence as a reference-backed rehearsal.

## WPS Layout Audit V1

WPS/COM checks should be intentionally conservative in v1. The host audit may assert only stable facts:

- page count,
- paragraph count,
- table count,
- field count,
- table of contents count,
- whether fields and table of contents were updated,
- section count or section anomalies when available,
- header/footer relationship anomalies when available,
- table count changes after finalization,
- page break and blank-page risk markers when detectable.

The v1 design must not claim perfect detection of visual typography problems such as every orphan line, widow line, continued-table title, or subtle pagination issue. Those checks should be listed in `manual-checklist.md` unless a stable WPS/COM signal is implemented and verified.

## Repair Plan

`repair-plan.json` should turn findings into action, not prose only. Each repair item should contain:

- issue id,
- severity,
- source step,
- target artifact,
- suggested command or operation when available,
- whether the repair can run automatically,
- whether WPS is required,
- human-readable explanation.

Examples:

- rerun `finalize apply` when host finalization metadata is stale,
- apply profile role formatting when validation finds a direct-format mismatch,
- apply table formatting when a table lacks three-line borders,
- add missing content when rehearsal finds reference gaps,
- inspect WPS manually when pagination risks remain.

## Error Handling

The command should fail fast for invalid or unsafe input:

- missing template,
- missing content JSON,
- malformed project rules,
- unsafe output path,
- output path equal to an input path,
- workdir path inside an input file path or otherwise invalid,
- host finalization requested but no usable host output was produced.

For non-fatal quality problems, the command should continue to produce the audit and repair plan. The process exit code may remain non-zero when `ready=false`, but the artifacts must still be written when enough information exists.

## Testing Strategy

Coverage should include:

- unit tests for final audit readiness decisions,
- CLI tests for argument validation and unsafe path refusal,
- orchestration tests that verify step order and expected artifacts,
- tests that convert validation warnings into audit findings,
- tests that convert rehearsal content gaps into blocking findings,
- tests that mark no-reference runs as reduced confidence,
- tests for repair plan item generation,
- a real `lizi/` regression that runs the full flow against the template, generated content, and reference thesis.

Host-dependent tests should remain isolated. If WPS/COM is unavailable in CI, CLI tests should verify graceful failure and artifact cleanup. Real WPS layout audit can be covered by local/manual regression until a reliable automated host environment exists.

## Non-Goals For V1

V1 does not need to:

- generate thesis prose with AI,
- infer every school rule automatically from natural-language instructions,
- perfectly validate visual pagination,
- fully automate continued-table captions,
- replace human review of academic content quality,
- build a GUI.

Those are future layers. The v1 target is a reliable final-draft candidate pipeline with honest audit evidence.

## Success Criteria

The feature is successful when a user can run one CLI command with a template, content JSON, project rules, and optional reference thesis, then receive:

- a DOCX candidate generated from the template,
- merged rules used to create it,
- a clear readiness decision,
- concrete blocking issues if it is not ready,
- follow-up repair actions where automation is possible,
- a manual checklist for remaining WPS or human-only checks.

This should make the project usable as a serious thesis-finalization tool, while still being honest about what must be checked in WPS or by a person.
