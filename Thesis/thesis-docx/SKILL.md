---
name: thesis-docx
description: Use when using Thesis DOCX CLI to extract thesis template rules, merge project rule JSON, write thesis content into template DOCX files, validate formatting, or apply teacher feedback edits.
---

# thesis-docx

从仓库根目录使用：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe
```

命令写法始终是 exe 后面直接跟参数。

## 推荐终稿链路

```text
finalize-all
  = profile extract -> rules merge -> assemble -> validate -> finalize -> validate -> rehearsal compare -> final audit
```

正式终稿优先先用 `content extract` 把成品论文或已有正文稿抽成 `content.json`，审核后再用 `finalize-all` 生成终稿候选和审计报告。`assemble`、`validate`、`finalize apply`、`rehearsal compare` 仍可单独使用，但主要作为拆解调试路径。

## 规则优先级

```text
request.json 单次操作参数 > project-rules.json / final-rules.json > profile.json > 工具默认规则
```

## 核心命令

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe inspect --doc "模板.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile extract --doc "模板.docx" --out ".analysis\profile.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe content extract --doc "成品论文.docx" --out ".analysis\content.json" --report ".analysis\content-extract-report.json" --profile ".analysis\profile.json" --project-rules ".analysis\project-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rules merge --profile ".analysis\profile.json" --project ".analysis\project-rules.json" --out ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize-all --template "模板.docx" --content ".analysis\content.json" --project-rules ".analysis\project-rules.json" --reference "成品论文.docx" --out ".analysis\final.docx" --workdir ".analysis\final-run"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe assemble --doc "模板.docx" --content ".analysis\content.json" --profile ".analysis\final-rules.json" --out ".analysis\thesis.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe apply --doc ".analysis\thesis.docx" --profile ".analysis\final-rules.json" --request ".analysis\request.json" --out ".analysis\thesis-revised.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\thesis-revised.docx" --profile ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\thesis-revised.docx" --out ".analysis\final.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rehearsal compare --candidate ".analysis\final.docx" --reference "成品论文.docx" --profile ".analysis\final-rules.json" --out ".analysis\rehearsal-report.json"
```

`finalize-all` 的 `--workdir` 会产出 `profile.json`、`final-rules.json`、`assembled.docx`、`candidate.docx`、`validate-before-finalize.json`、`host-finalization.json`、`validate-after-finalize.json`、`rehearsal-report.json`、`final-audit.json`、`repair-plan.json` 和 `manual-checklist.md`。只有 `final-audit.ready=true` 才会写入 `--out` 并进入终审；不 ready 时命令返回 error，保留既有 `--out`，候选稿留在 `--workdir\candidate.docx`。`--skip-host-finalize` 只能用于离线试跑。

## writeBlock

第一版整篇论文优先用 `assemble` 写入 `content.json`。老师反馈后的局部替换、插入和格式覆盖再用 `writeBlock`。角色格式来自 `final-rules.json`，本次 `format` 覆盖角色默认格式。`position` 支持 `before`、`after` 和 `replace`；替换模板占位段落时使用 `replace`。如果模板没有可识别论文锚点或节边界，`assemble` 会回退到整主体重写路径，这类模板要先用 `inspect` 检查结构。

```json
{
  "schemaVersion": "1.0",
  "mode": "execute",
  "options": { "createSnapshot": false, "requireSingleMatch": true },
  "operations": [
    {
      "id": "write-body",
      "op": "writeBlock",
      "role": "body",
      "target": { "type": "paragraphText", "text": "第一章 绪论", "match": "exact" },
      "text": "工业控制系统是关键基础设施中的重要组成部分。",
      "format": {
        "position": "after",
        "fontSizeHalfPoints": "21",
        "eastAsiaFont": "宋体"
      }
    }
  ]
}
```

查看样例：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations sample --op writeBlock
```

## 草稿生成

`generate --content` 会从空白文档生成并插入目录字段，只适合结构化草稿，不作为正式终稿主路径。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe generate --content ".analysis\content.json" --rules ".analysis\final-rules.json" --out ".analysis\draft.docx"
```

正式交付前必须用 Word/WPS 最终化，并用 `rehearsal compare` 对照成品论文或参考稿检查标题覆盖、段落/表格/节差异和 profile 校验结果。重点看 `contentCoverage.gaps`，它会列出参考稿中候选稿没有覆盖的正文段落和表格，包含章节上下文和内容预览；该列表从摘要或第一章等正文起点之后开始比较，会过滤封面/任务书/授权页等前置表单、目录行和 Word/WPS 域代码。最后仍要人工抽查真实分页、目录页码、孤行、跨页表格、节状态和续表标题。
