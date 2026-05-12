# Thesis DOCX CLI

Command-line tools for extracting thesis DOCX formatting profiles, applying OpenXML edits, validating results, and planning final Word/WPS finalization.

## Build And Test

```powershell
dotnet build ThesisTool.slnx
dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj
```

## Common Workflow

Extract a profile from a template or formatted thesis:

```powershell
dotnet run --project src\Thesis.Cli\Thesis.Cli.csproj -- profile extract --doc "论文正文格式.docx" --out ".analysis\profile.json"
```

Apply a request to a copy. The source DOCX is never overwritten by `apply`:

```powershell
dotnet run --project src\Thesis.Cli\Thesis.Cli.csproj -- apply --doc "论文正文格式.docx" --profile ".analysis\profile.json" --request ".analysis\request.json" --out ".analysis\output.docx"
```

Validate the edited document:

```powershell
dotnet run --project src\Thesis.Cli\Thesis.Cli.csproj -- validate --doc ".analysis\output.docx" --profile ".analysis\profile.json"
```

Check whether Word/WPS is still needed for fields, TOC page numbers, or pagination:

```powershell
dotnet run --project src\Thesis.Cli\Thesis.Cli.csproj -- finalize plan --doc ".analysis\output.docx"
```

Prefer finalizing to a copy:

```powershell
dotnet run --project src\Thesis.Cli\Thesis.Cli.csproj -- finalize apply --doc ".analysis\output.docx" --out ".analysis\final.docx"
```

Use `--in-place` only when you intentionally want Word/WPS to save the supplied file path:

```powershell
dotnet run --project src\Thesis.Cli\Thesis.Cli.csproj -- finalize apply --doc ".analysis\output.docx" --in-place
```

## Request JSON

Generate operation examples from the live catalog:

```powershell
dotnet run --project src\Thesis.Cli\Thesis.Cli.csproj -- operations list
dotnet run --project src\Thesis.Cli\Thesis.Cli.csproj -- operations sample --op replaceText
```

Minimal request example:

```json
{
  "schemaVersion": "1.0",
  "requestId": "example",
  "mode": "execute",
  "options": {
    "createSnapshot": false,
    "stopOnError": true
  },
  "operations": [
    {
      "id": "replace-abstract-title",
      "op": "replaceText",
      "target": { "type": "paragraphText", "text": "摘   要", "match": "exact" },
      "text": "摘   要（修订）",
      "format": { "find": "摘   要" }
    }
  ]
}
```

Common target shapes:

```json
{ "type": "paragraphIndex", "index": 0 }
{ "type": "paragraphText", "text": "摘要", "match": "exact" }
{ "type": "paragraphText", "text": "第一章", "match": "contains" }
{ "type": "tableIndex", "index": 0 }
{ "type": "tableCell", "tableIndex": 0, "rowIndex": 0, "cellIndex": 0 }
{ "type": "sectionRange", "start": { "type": "paragraphText", "text": "参考文献", "match": "exact" }, "includeStart": false }
```

## Notes

- Pure OpenXML edits do not calculate true page layout.
- `validate` can pass while `finalize plan` still reports required host finalization.
- For real thesis documents, work on copies or workspaces first, then finalize with Word/WPS.
