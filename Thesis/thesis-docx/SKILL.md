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
inspect --doc -> profile extract -> project-rules.json -> rules merge -> apply/writeBlock -> validate -> finalize -> request.json 微调
```

正式终稿优先在模板副本上增量写入，避免从空白文档重建时丢失节、页眉页脚、字段和复杂样式。

## 规则优先级

```text
request.json 单次操作参数 > project-rules.json / final-rules.json > profile.json > 工具默认规则
```

## 核心命令

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe inspect --doc "模板.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile extract --doc "模板.docx" --out ".analysis\profile.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rules merge --profile ".analysis\profile.json" --project ".analysis\project-rules.json" --out ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe apply --doc "模板.docx" --profile ".analysis\final-rules.json" --request ".analysis\request.json" --out ".analysis\thesis.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\thesis.docx" --profile ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\thesis.docx" --out ".analysis\final.docx"
```

## writeBlock

推荐用 `writeBlock` 写标题、正文、摘要等文本块。角色格式来自 `final-rules.json`，本次 `format` 覆盖角色默认格式。`position` 支持 `before`、`after` 和 `replace`；替换模板占位段落时使用 `replace`。

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

`generate --content` 只适合结构化草稿，不作为正式终稿主路径。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe generate --content ".analysis\content.json" --rules ".analysis\final-rules.json" --out ".analysis\draft.docx"
```

正式交付前必须用 Word/WPS 最终化并人工抽查真实分页、目录页码、孤行、跨页表格和续表标题。
