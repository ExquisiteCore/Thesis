---
name: thesis-docx
description: Use when using Thesis DOCX CLI to extract thesis template rules, merge project rule JSON, write thesis content into template DOCX files, validate formatting, or apply teacher feedback edits.
---

# thesis-docx

这是 Thesis DOCX CLI 的封装 skill。核心原则：正式论文优先在模板副本上装配或增量写入，而不是从空白文档重新生成。

## CLI

从仓库根目录使用：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe
```

如果是在 GitHub Release 二进制包里使用，入口通常是解压目录下的：

```powershell
.\Thesis.Cli.exe
```

如果 exe 不存在：

```powershell
dotnet build ThesisTool.slnx
```

命令写法始终是 exe 后面直接跟参数。

下方示例使用源码构建路径；在发布包内执行时，把 `.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe` 替换为 `.\Thesis.Cli.exe`。

## 规则优先级

```text
request.json 单次操作参数 > project-rules.json / final-rules.json > profile.json > 工具默认规则
```

- `profile.json`：从模板或成品论文提取的基础格式画像。
- `project-rules.json`：从模板正文、批注、学校要求或 AI 分析补充出的项目级规则。
- `final-rules.json`：`profile.json` 和 `project-rules.json` 合并后的最终规则，可作为 `--profile` 输入。
- `request.json`：本次写入或微调操作，参数可覆盖最终规则。

## 输入职责

- `--template`：模板结构来源，也是 `profile.json` 的提取来源。
- `--project-rules`：只传项目覆盖规则；不要把合并后的 `final-rules.json` 传给 `finalize-all --project-rules`。
- `--content`：正文结构，包含摘要、章节、段落、图片、表格、参考文献和致谢。
- `--front-matter-doc`：任务书、开题报告、承诺书等额外前置 DOCX；可重复传参，顺序即插入顺序。
- `--reference`：参考成品用于 rehearsal/audit，不会被复制进候选稿；正式终稿生产线必须带。

禁止事项：

- 不要用 `generate --content` 作为正式终稿主路径。
- 不要把 `--skip-host-finalize` 的输出当正式终稿。
- 不要把 `content-extract-report.ready=true` 当成终稿 ready。
- 不要交付 `--workdir\candidate.docx`，除非 `final-audit.ready=true` 后它已经被提升到 `--out`。

## 推荐终稿流程

```text
finalize-all
  = profile extract -> rules merge -> assemble -> validate -> finalize -> validate -> rehearsal compare -> final audit
```

正式终稿默认用 `finalize-all`。散命令仍保留给规则调试、内容抽取、局部微调和失败排查。

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

### 3. 提取 content.json

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe content extract --doc "成品论文.docx" --out ".analysis\content.json" --report ".analysis\content-extract-report.json" --profile ".analysis\profile.json" --project-rules ".analysis\project-rules.json"
```

抽取后必须审核 `content.json` 和 `content-extract-report.json`，重点看章节层级、表格行列、参考文献和 `ready`。

最小 `content.json`：

```json
{
  "schemaVersion": "1.0",
  "documentKind": "thesisContent",
  "title": "论文题目",
  "abstractZh": "中文摘要。",
  "keywordsZh": ["关键词一", "关键词二"],
  "abstractEn": "English abstract.",
  "keywordsEn": ["keyword"],
  "chapters": [
    {
      "title": "第一章 绪论",
      "blocks": [
        { "type": "paragraph", "text": "正文段落。" },
        { "type": "image", "path": "figures/result.png", "caption": "图1-1 实验结果", "altText": "实验结果" },
        {
          "type": "table",
          "table": {
            "caption": "表1-1 指标对比",
            "headers": ["指标", "值"],
            "rows": [["平均时延", "14.89 s"]]
          }
        }
      ]
    }
  ],
  "references": ["张三. 论文题名[J]. 期刊, 2025, 1(1): 1-10."],
  "acknowledgements": "致谢正文。"
}
```

图片路径按 CLI 当前工作目录或绝对路径解析。`widthEmu`、`heightEmu` 可选，单位是 EMU。表格格式来自 `final-rules.json`，尤其是 `tableDefault` 和表格角色规则。

### 4. 合并 project-rules.json

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rules merge --profile ".analysis\profile.json" --project ".analysis\project-rules.json" --out ".analysis\final-rules.json"
```

### 5. 用 assemble 装配整篇正文

`assemble` 是整篇论文第一版主入口。它把 `content.json` 里的标题、摘要、关键词、章节、段落、表格、参考文献和致谢写入模板副本，格式来自 `final-rules.json`。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe assemble --doc "模板.docx" --content ".analysis\content.json" --profile ".analysis\final-rules.json" --out ".analysis\thesis.docx"
```

带任务书/开题报告：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe assemble --doc "模板.docx" --front-matter-doc "任务书.docx" --front-matter-doc "开题报告.docx" --content ".analysis\content.json" --profile ".analysis\final-rules.json" --out ".analysis\thesis.docx"
```

如果模板没有可识别论文锚点或节边界，`assemble` 会回退到整主体重写路径。复杂多节前置页或必须原位替换的模板，要先用 `inspect` 检查结构，再用 `apply/writeBlock` 补齐或微调。`--front-matter-doc` 会复制常见段落、表格和图片关系；真实分页、节边界和页眉页脚仍要通过 WPS/Word 最终化确认。

### 6. 用 finalize-all 生成终稿候选

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize-all --template "模板.docx" --content ".analysis\content.json" --project-rules ".analysis\project-rules.json" --reference "成品论文.docx" --out ".analysis\final.docx" --workdir ".analysis\final-run"
```

带任务书/开题报告：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize-all --template "模板.docx" --front-matter-doc "任务书.docx" --front-matter-doc "开题报告.docx" --content ".analysis\content.json" --project-rules ".analysis\project-rules.json" --reference "成品论文.docx" --out ".analysis\final.docx" --workdir ".analysis\final-run"
```

`finalize-all` 的 `--workdir` 会产出 `profile.json`、`final-rules.json`、`assembled.docx`、`candidate.docx`、`validate-before-finalize.json`、`host-finalization.json`、`validate-after-finalize.json`、`rehearsal-report.json`、`final-audit.json`、`repair-plan.json` 和 `manual-checklist.md`。只有 `final-audit.ready=true` 才会写入 `--out` 并进入终审；不 ready 时命令返回 error，保留既有 `--out`，候选稿留在 `--workdir\candidate.docx`。`--skip-host-finalize` 只能用于离线试跑。

`final-audit.ready=false` 处理顺序：

1. 读 `final-audit.json` 的 `blocking`、`requiresWps`、`requiresHuman`。
2. 读 `repair-plan.json`，先修 `automatic=true` 或明确指向 `content.json`、`project-rules.json` 的项。
3. 打开 `candidate.docx` 只做检查，不作为交付件。
4. 按 `manual-checklist.md` 检查目录页码、真实分页、跨页表格、续表标题、孤行标题。
5. 修完后重跑同一条 `finalize-all`，直到 `final-audit.ready=true`。

### 7. 用 writeBlock 微调正文

`writeBlock` 接收 `text + role + target + format`，用于老师反馈后的局部替换、插入和格式覆盖。角色格式来自 `final-rules.json`，本次 `format` 覆盖角色默认格式。`position` 支持 `before`、`after` 和 `replace`；替换模板占位段落时使用 `replace`。

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
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe apply --doc ".analysis\thesis.docx" --profile ".analysis\final-rules.json" --request ".analysis\request.json" --out ".analysis\thesis-revised.docx"
```

查看样例：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations sample --op writeBlock
```

### 8. 校验和最终化

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\thesis-revised.docx" --profile ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize plan --doc ".analysis\thesis-revised.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\thesis-revised.docx" --out ".analysis\final.docx"
```

如果没有微调步骤，直接对 `assemble` 生成的 `.analysis\thesis.docx` 执行校验和最终化。

### 9. 和参考成品做 rehearsal 对比

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rehearsal compare --candidate ".analysis\final.docx" --reference "成品论文.docx" --profile ".analysis\final-rules.json" --out ".analysis\rehearsal-report.json"
```

重点看 `headingCoverage`、`readyForFinalReview`、`requiresFinalization`、段落/表格/节数量差异，以及 `validation.compliant`。同时检查 `contentCoverage.gaps`，它会列出参考稿中候选稿没有覆盖的正文段落和表格，包含章节上下文和内容预览；该列表从摘要或第一章等正文起点之后开始比较，会过滤封面/任务书/授权页等前置表单、目录行和 Word/WPS 域代码。

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

`generate --content` 会从空白文档生成并插入目录字段，只能作为结构化草稿路径，不作为正式终稿主路径：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe generate --content ".analysis\content.json" --rules ".analysis\final-rules.json" --out ".analysis\draft.docx"
```

原因：它从新文档生成，不能完整保留模板中的节、页眉页脚、字段和复杂结构。

## 判断标准

- 可以离线保证：DOCX 结构、样式引用、段落格式、表格边框、重复表头、参考文献编号、目录字段标记。
- 不能纯离线保证：真实分页、目录页码、孤行、跨页显示、自动续表标题。
- 正式论文交付必须经过 Word/WPS 最终化和人工抽查。
