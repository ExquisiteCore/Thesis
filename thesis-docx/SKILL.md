---
name: thesis-docx
description: Use when using Thesis DOCX CLI to extract thesis template rules, merge project rule JSON, write thesis content into template DOCX files, validate formatting, or apply teacher feedback edits.
---

# thesis-docx

这是 Thesis DOCX CLI 的封装 skill。核心原则：正式论文优先在模板副本上增量写入，而不是从空白文档重新生成。

## CLI

从仓库根目录使用：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe
```

如果 exe 不存在：

```powershell
dotnet build ThesisTool.slnx
```

命令写法始终是 exe 后面直接跟参数。

## 规则优先级

```text
request.json 单次操作参数 > project-rules.json / final-rules.json > profile.json > 工具默认规则
```

- `profile.json`：从模板或成品论文提取的基础格式画像。
- `project-rules.json`：从模板正文、批注、学校要求或 AI 分析补充出的项目级规则。
- `final-rules.json`：`profile.json` 和 `project-rules.json` 合并后的最终规则，可作为 `--profile` 输入。
- `request.json`：本次写入或微调操作，参数可覆盖最终规则。

## 推荐终稿流程

```text
inspect --doc -> profile extract -> project-rules.json -> rules merge -> apply/writeBlock -> validate -> finalize -> request.json 微调
```

### 1. 检查模板正文和批注

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe inspect --doc "模板.docx"
```

读取 `documentMap.requirementHints` 和 `documentMap.comments`，整理正文中写明但样式无法体现的要求。

### 2. 提取 profile.json

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile extract --doc "模板.docx" --out ".analysis\profile.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile explain --profile ".analysis\profile.json"
```

### 3. 合并 project-rules.json

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rules merge --profile ".analysis\profile.json" --project ".analysis\project-rules.json" --out ".analysis\final-rules.json"
```

### 4. 用 writeBlock 写正文

`writeBlock` 接收 `text + role + target + format`。角色格式来自 `final-rules.json`，本次 `format` 覆盖角色默认格式。`position` 支持 `before`、`after` 和 `replace`；替换模板占位段落时使用 `replace`。

```json
{
  "schemaVersion": "1.0",
  "mode": "execute",
  "options": {
    "createSnapshot": false,
    "requireSingleMatch": true
  },
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

执行：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe apply --doc "模板.docx" --profile ".analysis\final-rules.json" --request ".analysis\request.json" --out ".analysis\thesis.docx"
```

### 5. 校验和最终化

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\thesis.docx" --profile ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize plan --doc ".analysis\thesis.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\thesis.docx" --out ".analysis\final.docx"
```

### 6. 和参考成品做 rehearsal 对比

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rehearsal compare --candidate ".analysis\final.docx" --reference "成品论文.docx" --profile ".analysis\final-rules.json" --out ".analysis\rehearsal-report.json"
```

重点看 `headingCoverage`、`readyForFinalReview`、`requiresFinalization`、段落/表格/节数量差异，以及 `validation.compliant`。这个命令适合在正式终审前发现候选稿和参考稿的结构缺口。

## 常用操作

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations list
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations sample --op writeBlock
```

- `writeBlock`：按语义角色写入标题、正文、摘要等文本块。
- `applyProfileRole`：对已有段落套用某个角色格式。
- `setParagraphFormat`：直接设置首行缩进、行距、字体字号等。
- `insertTable` / `applyProfileTable` / `applyThreeLineTable`：插入和格式化表格。
- `setTableRowHeader`：设置跨页重复表头。
- `insertCaption`：插入图题或表题。
- `insertTocField`：插入目录字段。
- `replaceReferences` / `insertReferenceItem` / `applyReferenceFormat`：处理参考文献。

## 草稿生成路径

`generate --content` 会插入目录字段，但只能作为结构化草稿路径，不作为正式终稿主路径：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe generate --content ".analysis\content.json" --rules ".analysis\final-rules.json" --out ".analysis\draft.docx"
```

原因：它从新文档生成，不能完整保留模板中的节、页眉页脚、字段和复杂结构。

## 判断标准

- 可以离线保证：DOCX 结构、样式引用、段落格式、表格边框、重复表头、参考文献编号、目录字段标记。
- 不能纯离线保证：真实分页、目录页码、孤行、跨页显示、自动续表标题。
- 正式论文交付必须经过 Word/WPS 最终化和人工抽查。

## Skill 放置和命名

推荐把 skill 放到项目的 `Thesis` 目录：

```text
Thesis/thesis-docx/SKILL.md
```

技能名称只能包含小写字母、数字和连字符。正确示例：`thesis-docx`。不要使用大写、下划线、空格或中文名称。
