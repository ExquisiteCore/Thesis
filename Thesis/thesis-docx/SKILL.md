---
name: thesis-docx
description: Use when using Thesis DOCX CLI to extract thesis template rules, merge project rule JSON, generate thesis DOCX files, validate formatting, or apply teacher feedback edits.
---

# thesis-docx

从仓库根目录使用：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe
```

标准链路：

```text
inspect --doc -> profile extract -> project-rules.json -> rules merge -> generate -> validate -> finalize -> request.json 微调
```

优先级：

```text
request.json 中的单次操作参数 > project-rules.json / final-rules.json > profile.json > 工具默认规则
```

核心命令：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe inspect --doc "模板.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile extract --doc "模板.docx" --out ".analysis\profile.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rules merge --profile ".analysis\profile.json" --project ".analysis\project-rules.json" --out ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe generate --content ".analysis\content.json" --rules ".analysis\final-rules.json" --out ".analysis\thesis.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\thesis.docx" --profile ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\thesis.docx" --out ".analysis\final.docx"
```

正式交付前必须用 Word/WPS 最终化并人工抽查真实分页、目录页码、孤行、跨页表格和续表标题。
