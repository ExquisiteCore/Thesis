---
name: thesis-docx
description: 使用 Thesis DOCX CLI 根据学校论文模板提取 profile.json，并用内容 JSON、request JSON、profileOverrides 生成、检查、套格式和微调论文 DOCX。适用于论文生成、学校模板套用、格式检查、首行缩进、三线表、重复表头、参考文献、目录字段、页眉页脚和 Word/WPS 最终化。
---

# thesis-docx

这是 Thesis DOCX CLI 的封装 skill。核心目标是让 agent 用学校模板和 JSON 请求稳定处理论文 DOCX，而不是手工改 XML。

## 基本原则

- 默认只处理副本，不覆盖用户原始 `.docx`。
- 先检查输入，再生成或修改，再校验，最后规划最终化。
- 优先使用构建后的 exe：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe
```

- 如果 exe 不存在，先运行：

```powershell
dotnet build ThesisTool.slnx
```

## 参数优先级

生成或套格式时按下面优先级解释配置：

```text
request.json 中的单次操作参数 > profileOverrides/覆盖 JSON > profile.json > 工具默认规则
```

含义：

- `profile.json`：从学校模板或已排版论文提取的默认格式画像。
- `profileOverrides`：用户或上层系统提供的全局覆盖项，优先级高于模板画像。
- `request.json`：本次具体操作，例如改某段首行缩进、某张表改三线表、替换参考文献。

## 标准流程

### 1. 提取模板画像

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile extract --doc "模板.docx" --out ".analysis\profile.json"
```

随后查看画像说明：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile explain --profile ".analysis\profile.json"
```

### 2. 生成或修改论文副本

已有论文时，用 `apply` 把 `request.json` 应用到副本：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe apply --doc "source.docx" --profile ".analysis\profile.json" --request ".analysis\request.json" --out ".analysis\output.docx"
```

如果需要交互式多步处理，先建立 workspace：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe session init --doc "source.docx" --profile ".analysis\profile.json" --workspace ".analysis\workspace"
```

然后执行请求：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe run --workspace ".analysis\workspace" --request ".analysis\request.json"
```

### 3. 校验格式

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\output.docx" --profile ".analysis\profile.json"
```

若校验报告给出 `suggestedOperations`，优先把这些操作整理进下一轮 `request.json`。

### 4. 最终化

先规划：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize plan --doc ".analysis\output.docx"
```

需要更新目录、字段、页码或真实分页时，最终化到副本：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\output.docx" --out ".analysis\final.docx"
```

## 常用操作

先查看支持的操作：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations list
```

生成某个操作的 JSON 示例：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations sample --op setParagraphFormat
```

常用操作包括：

- `setParagraphFormat`：段落格式、首行缩进、段前段后、行距、对齐、字体字号。
- `applyProfileRole`：按模板画像中的角色套格式。
- `applyProfileTable`：按模板画像套表格格式。
- `applyThreeLineTable`：把表格改为论文常用三线表。
- `setTableRowHeader`：设置跨页重复表头。
- `insertCaption`：插入图题或表题。
- `insertTocField`：插入目录字段。
- `replaceReferences` / `applyReferenceFormat` / `normalizeReferences`：处理参考文献。

## 判断标准

- 可以离线保证：DOCX 结构、样式引用、段落格式、表格边框、重复表头、参考文献编号、目录字段标记。
- 不能纯离线保证：真实分页、目录页码、孤行、跨页显示、自动“续表”标题。
- 真实论文交付必须经过 Word/WPS 最终化和人工抽查。
