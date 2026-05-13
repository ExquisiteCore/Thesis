# Thesis 论文 DOCX CLI

这个工具的目标不是“凭空生成一个论文文件”，而是把学校模板或成品论文变成可执行规则，然后在模板副本上按语义写入正文、表格、图片、参考文献，并继续支持老师反馈后的微调。

## 构建与运行

```powershell
dotnet build ThesisTool.slnx
dotnet run --project tests\Thesis.Tests\Thesis.Tests.csproj
```

构建后的 CLI：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe
```

后续命令都是 **exe 后面直接跟参数**，例如：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations list
```

## 推荐终稿链路

```text
模板/成品论文.docx
  -> inspect --doc                 读取正文、样式、表格、节、页眉页脚、批注、格式要求线索
  -> profile extract               提取 profile.json 基础格式画像
  -> project-rules.json            AI/人工补充模板里没有显式体现的要求
  -> rules merge                   合并为 final-rules.json
  -> assemble                      把结构化正文写入模板副本，保留前置页和正文节设置
  -> apply + writeBlock/request    老师反馈后的局部微调
  -> validate                      检查可离线验证的格式
  -> finalize plan/apply           用 Word/WPS 更新字段、目录、分页
  -> rehearsal compare             和参考成品做结构对比
```

优先级：

```text
request.json 单次操作参数 > project-rules.json / final-rules.json > profile.json > 工具默认规则
```

正式终稿优先用 `assemble --doc 模板.docx --content content.json --profile final-rules.json --out 输出.docx` 做第一版整篇装配；它复制模板后保留首个论文锚点之前的封面/前置页，从“摘要 / Abstract / 目录 / 第X章”这类锚点开始替换论文主体，并保留选中的正文节页面设置和页眉页脚关系。模板尾部的格式说明、示例参考文献等说明性内容会被丢弃。老师后续反馈、个别段落覆盖、局部格式调整再用 `apply --request request.json`。`generate --content` 仍然存在，但只适合作为空白文档草稿路径。

## 1. 检查模板正文和批注

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe inspect --doc "模板.docx"
```

`documentMap` 会包含段落、样式、编号、节、表格、批注和 `requirementHints`。模板正文或批注里写的“正文首行缩进 2 字符”“表格三线表”等要求，应整理进 `project-rules.json`。

## 2. 提取 profile.json

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile extract --doc "模板.docx" --out ".analysis\profile.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe profile explain --profile ".analysis\profile.json"
```

`profile.json` 是从模板或成品论文提取出来的基础规则，包括页面、角色格式、表格格式、格式簇和诊断信息。

## 3. 补充 project-rules.json

`project-rules.json` 用于覆盖或扩展 `profile.json`。示例：

```json
{
  "schemaVersion": "1.0",
  "rulesKind": "projectRules",
  "roleAliases": {
    "mainBody": "body"
  },
  "pageSetup": {
    "margins": {
      "topTwips": 1134,
      "rightTwips": 1134,
      "bottomTwips": 1134,
      "leftTwips": 1701
    }
  },
  "roleFormats": {
    "body": {
      "styleId": "2",
      "alignment": "both",
      "firstLineIndentTwips": 420,
      "lineSpacing": "360",
      "lineSpacingRule": "atleast",
      "fontSizeHalfPoints": "21",
      "eastAsiaFont": "宋体"
    }
  },
  "tableDefault": {
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

合并：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rules merge --profile ".analysis\profile.json" --project ".analysis\project-rules.json" --out ".analysis\final-rules.json"
```

## 4. 用 assemble 装配整篇正文

`content.json` 用来表达论文内容结构：标题、作者、中英文摘要、关键词、章节、段落、表格、参考文献和致谢。格式来自 `final-rules.json`，表格默认使用规则里的 `tableDefault`，参考文献会避免重复 `[1]` 编号。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe assemble --doc "模板.docx" --content ".analysis\content.json" --profile ".analysis\final-rules.json" --out ".analysis\thesis.docx"
```

这条路径是当前最接近“正式工具”的整篇写作入口：它不是从空白文件开始，而是在模板副本上写入内容。当前版本会保留首个论文锚点之前的模板前置内容，替换论文主体范围，保留正文节设置，并丢弃模板尾部的格式说明/示例内容；如果模板里找不到可识别的论文锚点或节边界，工具会回退到旧的整主体重写路径。它还不是完整的终稿排版引擎：目录、字段、分页、节状态归一化仍必须经过 Word/WPS `finalize apply`。

## 5. 用 writeBlock 微调正文

`writeBlock` 是局部微调操作：给 CLI 一段文字、一个语义角色和一个写入方式。工具从 `final-rules.json` 找该角色的默认格式，再用本次 `format` 覆盖默认值。`position` 支持 `before`、`after` 和 `replace`；模板占位段落改成正式内容时用 `replace`。

```json
{
  "schemaVersion": "1.0",
  "requestId": "write-demo",
  "mode": "execute",
  "options": {
    "createSnapshot": false,
    "stopOnError": true,
    "requireSingleMatch": true
  },
  "operations": [
    {
      "id": "chapter-1-title",
      "op": "writeBlock",
      "role": "heading1",
      "target": { "type": "paragraphText", "text": "目    录", "match": "exact" },
      "text": "第一章 绪论",
      "format": { "position": "replace" }
    },
    {
      "id": "chapter-1-body",
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

执行。这里以已经装配出的 `.analysis\thesis.docx` 为输入，输出微调后的 `.analysis\thesis-revised.docx`：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe apply --doc ".analysis\thesis.docx" --profile ".analysis\final-rules.json" --request ".analysis\request.json" --out ".analysis\thesis-revised.docx"
```

查看更多操作和样例：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations list
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations sample --op writeBlock
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations sample --op applyThreeLineTable
```

## 6. 校验和最终化

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\thesis-revised.docx" --profile ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize plan --doc ".analysis\thesis-revised.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\thesis-revised.docx" --out ".analysis\final.docx"
```

如果没有执行微调步骤，就把上面的 `.analysis\thesis-revised.docx` 换成 `assemble` 生成的 `.analysis\thesis.docx`。

离线能检查和处理：DOCX 结构、段落格式、角色格式、三线表边框、重复表头、参考文献编号、目录字段标记。

必须用 Word/WPS 或人工确认：真实分页、目录页码、孤行、跨页表格显示、自动续表标题。

## 7. 和成品论文做实战对比

如果目录里有一份老师认可或人工写好的成品论文，可以用 `rehearsal compare` 检查候选稿是否接近可终审状态：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe rehearsal compare --candidate ".analysis\final.docx" --reference "成品论文.docx" --profile ".analysis\final-rules.json" --out ".analysis\rehearsal-report.json"
```

报告会输出：

- 候选稿和参考稿的段落数、非空段落数、字符数、表格数、节数。
- 标题覆盖率 `headingCoverage`，用于发现章节标题缺失或重复编号。
- `contentCoverage.gaps`，用于定位参考稿中候选稿没有覆盖的正文段落和表格，包含缺口类型、参考索引、章节上下文和内容预览。该列表从摘要或第一章等正文起点之后开始比较，会过滤封面/任务书/授权页等前置表单、目录行和 Word/WPS 域代码。
- 候选稿是否还需要 Word/WPS 最终化。
- `validate` 的规则校验结果。
- 段落、表格、节数量不足等诊断。

`readyForFinalReview=true` 只代表离线结构、标题覆盖和 profile 校验没有发现警告；正式交付仍需经过 Word/WPS 打开后的分页、目录页码、跨页表格和续表标题抽查。

## 8. 结构化草稿路径

需要快速从内容 JSON 出一个草稿时，可以使用：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe generate --content ".analysis\content.json" --rules ".analysis\final-rules.json" --out ".analysis\draft.docx"
```

这条路径会自动插入目录字段，生成后仍需执行 `finalize apply` 更新目录和分页。它不会完整保留模板原有页眉页脚、节、字段和复杂结构，不应作为正式终稿主路径。

## 9. 查看规则 JSON

打开：

```text
profile-viewer/index.html
```

可拖入 `profile.json`、`project-rules.json` 或 `final-rules.json`，本地查看规则概览和原始 JSON。

## Skill 目录

给 Codex/Agent 封装时，建议创建 `Thesis` 目录，把 skill 放在：

```text
Thesis/thesis-docx/SKILL.md
```

技能名称只能包含小写字母、数字和连字符。正确示例：`thesis-docx`。不要使用大写字母、下划线、空格或中文技能名。
