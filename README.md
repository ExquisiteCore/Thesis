# Thesis 论文 DOCX CLI

用于从学校论文模板或成品论文提取格式规则，合并项目级补充规则，用内容 JSON 生成论文 DOCX，并用 request JSON 做后续微调。

## 构建与测试

```powershell
dotnet build ThesisTool.slnx
dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj
```

构建后，CLI 可执行文件位置：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe
```

后续命令都是 **exe 后面直接跟参数**，不再写 `dotnet run --project ... --`。

## 完整流程

```text
template.docx / sample.docx
  -> inspect --doc
  -> profile extract
  -> AI/人工生成 project-rules.json
  -> rules merge 得到 final-rules.json
  -> generate 得到 thesis.docx
  -> validate
  -> finalize plan/apply
  -> 老师反馈后用 request.json 微调
```

### 1. 检查模板正文和批注

`inspect --doc` 会输出 `documentMap`，包含段落、样式、编号、节、表格、批注，以及从正文/批注中识别出的规则线索。规则线索只供 AI 和人工审阅，不会自动变成强规则。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe inspect --doc "模板.docx"
```

### 2. 提取 profile.json

`profile.json` 是从模板或成品论文中提取出的基础格式画像。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile extract --doc "模板.docx" --out ".analysis\profile.json"
```

可查看画像说明：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile explain --profile ".analysis\profile.json"
```

### 3. 补充 project-rules.json

模板正文、批注或学校说明里写到但模板样式无法体现的要求，写入 `project-rules.json`。它用于扩展或覆盖 `profile.json`，例如正文首行缩进、三线表、页边距、角色别名。

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
  },
  "tableDefault": {
    "widthTwips": 8307,
    "borders": {
      "top": { "value": "single", "size": "12", "color": "000000" },
      "bottom": { "value": "single", "size": "12", "color": "000000" },
      "left": { "value": "nil" },
      "right": { "value": "nil" },
      "insideHorizontal": { "value": "single", "size": "4", "color": "000000" },
      "insideVertical": { "value": "nil" }
    }
  }
}
```

### 4. 合并为 final-rules.json

`final-rules.json` 是最终规则，仍是可被 `--profile` 读取的 TemplateProfile，同时也可作为 `generate --rules` 输入。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rules merge --profile ".analysis\profile.json" --project ".analysis\project-rules.json" --out ".analysis\final-rules.json"
```

优先级：

```text
request.json 中的单次操作参数 > project-rules.json / final-rules.json > profile.json > 工具默认规则
```

### 5. 用 content.json 生成 thesis.docx

`content.json` 描述论文正文内容。生成器按 `final-rules.json` 写入页面设置、标题、摘要、关键词、章节、正文、表格、参考文献和致谢。

```json
{
  "schemaVersion": "1.0",
  "documentKind": "thesisContent",
  "title": "论文题目",
  "author": "学生姓名",
  "abstractZh": "中文摘要正文",
  "keywordsZh": ["关键词1", "关键词2"],
  "abstractEn": "English abstract",
  "keywordsEn": ["keyword1"],
  "chapters": [
    {
      "title": "绪论",
      "paragraphs": ["正文段落"],
      "sections": [
        { "title": "研究背景", "paragraphs": ["正文段落"] }
      ]
    }
  ],
  "references": ["作者. 题名[J]. 期刊, 2024."],
  "acknowledgements": "致谢正文"
}
```

生成命令：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe generate --content ".analysis\content.json" --rules ".analysis\final-rules.json" --out ".analysis\thesis.docx"
```

### 6. 校验和最终化

校验生成或修改后的文档：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\thesis.docx" --profile ".analysis\final-rules.json"
```

检查是否还需要 Word/WPS 更新字段、目录页码和真实分页：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize plan --doc ".analysis\thesis.docx"
```

最终化到新文件：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\thesis.docx" --out ".analysis\final.docx"
```

### 7. 老师反馈后的微调

老师提出新要求时，整理成 `request.json`，用 `final-rules.json` 作为 profile 执行。`apply` 不会覆盖源文件。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe apply --doc ".analysis\final.docx" --profile ".analysis\final-rules.json" --request ".analysis\request.json" --out ".analysis\revised.docx"
```

## Request JSON

查看支持的操作：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations list
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations sample --op replaceText
```

最小请求示例：

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
      "id": "body-indent",
      "op": "setParagraphFormat",
      "target": { "type": "paragraphText", "text": "正文段落", "match": "contains" },
      "format": { "firstLineIndentTwips": 480, "lineSpacing": "360" }
    }
  ]
}
```

常见定位方式：

```json
{ "type": "paragraphIndex", "index": 0 }
{ "type": "paragraphText", "text": "摘要", "match": "exact" }
{ "type": "paragraphText", "text": "第一章", "match": "contains" }
{ "type": "role", "role": "body", "position": "self" }
{ "type": "tableIndex", "index": 0 }
{ "type": "tableCell", "tableIndex": 0, "rowIndex": 0, "cellIndex": 0 }
```

## 查看规则 JSON

项目内置静态查看页面：

```text
profile-viewer/index.html
```

直接用浏览器打开，选择或拖入 `profile.json`、`project-rules.json` 或 `final-rules.json`，即可查看规则概览、页面设置、样式角色、角色规则、格式簇、表格策略、诊断信息和原始 JSON。页面只在浏览器本地解析文件，不上传数据。

## Skill 目录

给 Codex/Agent 封装时，建议创建 `Thesis` 目录，把 skill 放在：

```text
Thesis/thesis-docx/SKILL.md
```

技能名称只能包含小写字母、数字和连字符，例如 `thesis-docx`。不要使用大写字母、下划线、空格或中文技能名。

## 注意事项

- 纯 OpenXML 修改无法计算真实页面布局。
- `generate` 不承诺真实分页、目录页码或自动续表标题。
- `validate` 通过，不代表目录页码、分页和跨页显示已经最终正确。
- 表格三线表、首行缩进、标题样式、重复表头等可以离线处理。
- 续表标题、孤行、目录页码、真实分页等页面级问题需要 Word/WPS 最终化和人工抽查。
