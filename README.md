# Thesis 论文 DOCX CLI

用于从论文模板提取格式画像、按 JSON 请求排版/微调 DOCX、校验格式结果，并规划 Word/WPS 最终化步骤的命令行工具。

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

## 常用流程

从学校模板或已排好版的论文中提取 `profile.json`：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile extract --doc "论文正文格式.docx" --out ".analysis\profile.json"
```

把 `request.json` 应用到论文副本。`apply` 不会覆盖源文件：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe apply --doc "论文正文格式.docx" --profile ".analysis\profile.json" --request ".analysis\request.json" --out ".analysis\output.docx"
```

校验生成或修改后的文档是否符合模板画像：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\output.docx" --profile ".analysis\profile.json"
```

检查是否还需要 Word/WPS 更新字段、目录页码和真实分页：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize plan --doc ".analysis\output.docx"
```

建议最终化到新文件：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\output.docx" --out ".analysis\final.docx"
```

只有明确要让 Word/WPS 原地保存时，才使用 `--in-place`：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\output.docx" --in-place
```

## Request JSON

从当前操作目录生成示例：

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
      "id": "replace-abstract-title",
      "op": "replaceText",
      "target": { "type": "paragraphText", "text": "摘   要", "match": "exact" },
      "text": "摘   要（修订）",
      "format": { "find": "摘   要" }
    }
  ]
}
```

常见定位方式：

```json
{ "type": "paragraphIndex", "index": 0 }
{ "type": "paragraphText", "text": "摘要", "match": "exact" }
{ "type": "paragraphText", "text": "第一章", "match": "contains" }
{ "type": "tableIndex", "index": 0 }
{ "type": "tableCell", "tableIndex": 0, "rowIndex": 0, "cellIndex": 0 }
{ "type": "sectionRange", "start": { "type": "paragraphText", "text": "参考文献", "match": "exact" }, "includeStart": false }
```

## 能力定位

核心链路是：

1. 用模板 `.docx` 提取 `profile.json`。
2. 用内容 JSON、`request.json` 和 `profile.json` 生成或整理论文 DOCX。
3. 用微调请求继续修改段落、表格、标题、参考文献、目录字段等。
4. 用高优先级覆盖参数修正 profile 默认值。
5. 最后用 Word/WPS 更新字段、目录页码和分页。

优先级建议：

```text
request.json 中的单次操作参数 > profileOverrides/覆盖 JSON > profile.json > 工具默认规则
```

## 注意事项

- 纯 OpenXML 修改无法计算真实页面布局。
- `validate` 通过，不代表目录页码、分页和跨页效果已经最终正确。
- 真实论文应先处理副本或 workspace，确认无误后再用 Word/WPS 最终化。
- 表格三线表、首行缩进、标题样式等可以离线处理；续表标题、孤行、目录页码等页面级问题需要 Word/WPS 参与。
