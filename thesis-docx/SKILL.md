---
name: thesis-docx
description: Use when using Thesis DOCX CLI to extract thesis template rules, merge project rule JSON, generate thesis DOCX files, validate formatting, or apply teacher feedback edits.
---

# thesis-docx

这是 Thesis DOCX CLI 的封装 skill。它的目标是把学校模板、项目级补充规则、论文内容和老师反馈都转成可重复执行的 JSON 流程。

## 基本原则

- 默认只处理副本，不覆盖用户原始 `.docx`。
- 先检查模板和规则，再生成或修改，再校验，最后最终化。
- 命令写法始终是 exe 后面直接跟参数。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe
```

如果 exe 不存在，先运行：

```powershell
dotnet build ThesisTool.slnx
```

## 参数优先级

```text
request.json 中的单次操作参数 > project-rules.json / final-rules.json > profile.json > 工具默认规则
```

- `profile.json`：从学校模板或成品论文提取的基础格式画像。
- `project-rules.json`：AI/人工补充的项目级规则，用来覆盖或扩展模板画像。
- `final-rules.json`：`profile.json` 和 `project-rules.json` 合并后的最终规则，可作为 `--profile` 或 `generate --rules` 输入。
- `request.json`：本次微调操作，例如改首行缩进、表格改三线表、替换参考文献。

## 标准流程

### 1. 检查模板正文和批注

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe inspect --doc "模板.docx"
```

读取 `documentMap.requirementHints` 和 `documentMap.comments`，把模板正文/批注里写到的格式要求整理为 `project-rules.json`。

### 2. 提取 profile.json

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile extract --doc "模板.docx" --out ".analysis\profile.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile explain --profile ".analysis\profile.json"
```

### 3. 编写 project-rules.json

最小结构：

```json
{
  "schemaVersion": "1.0",
  "rulesKind": "projectRules",
  "roleAliases": { "mainBody": "body" },
  "pageSetup": {
    "margins": { "leftTwips": 1701, "rightTwips": 1701 }
  },
  "roleFormats": {
    "body": {
      "firstLineIndentTwips": 480,
      "lineSpacing": "360",
      "fontSizeHalfPoints": "24",
      "eastAsiaFont": "宋体"
    }
  }
}
```

### 4. 合并 final-rules.json

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rules merge --profile ".analysis\profile.json" --project ".analysis\project-rules.json" --out ".analysis\final-rules.json"
```

### 5. 用 content.json 生成论文

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe generate --content ".analysis\content.json" --rules ".analysis\final-rules.json" --out ".analysis\thesis.docx"
```

`content.json` 支持标题、作者、中英文摘要、关键词、章节、小节、表格、参考文献和致谢。

### 6. 校验和最终化

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\thesis.docx" --profile ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize plan --doc ".analysis\thesis.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\thesis.docx" --out ".analysis\final.docx"
```

### 7. 老师反馈后微调

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe apply --doc ".analysis\final.docx" --profile ".analysis\final-rules.json" --request ".analysis\request.json" --out ".analysis\revised.docx"
```

## 常用操作

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations list
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations sample --op setParagraphFormat
```

- `setParagraphFormat`：段落格式、首行缩进、段前段后、行距、对齐、字体字号。
- `applyProfileRole`：按最终规则中的角色套格式。
- `applyProfileTable`：按最终规则套表格格式。
- `applyThreeLineTable`：把表格改为论文三线表。
- `setTableRowHeader`：设置跨页重复表头。
- `insertCaption`：插入图题或表题。
- `insertTocField`：插入目录字段。
- `replaceReferences` / `applyReferenceFormat` / `normalizeReferences`：处理参考文献。

## Skill 放置和命名

推荐把 skill 放到项目的 `Thesis` 目录：

```text
Thesis/thesis-docx/SKILL.md
```

技能名称只能包含小写字母、数字和连字符。正确示例：`thesis-docx`。不要使用大写、下划线、空格或中文名称。

## 判断标准

- 可以离线保证：DOCX 结构、样式引用、段落格式、表格边框、重复表头、参考文献编号、目录字段标记。
- 不能纯离线保证：真实分页、目录页码、孤行、跨页显示、自动“续表”标题。
- 正式论文交付必须经过 Word/WPS 最终化和人工抽查。
