# Thesis 论文 DOCX CLI

这个工具的目标不是“凭空生成一个论文文件”，而是把学校模板或成品论文变成可执行规则，然后在模板副本上按语义写入正文、表格、图片、参考文献，并继续支持老师反馈后的微调。

## 构建与运行

```powershell
dotnet build ThesisTool.slnx
dotnet test tests\Thesis.Tests\Thesis.Tests.csproj
```

构建后的 CLI：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe
```

如果使用 GitHub Release 里的二进制包，解压目录下的入口是：

```powershell
.\Thesis.Cli.exe
```

后续命令都是 **exe 后面直接跟参数**，例如：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe operations list
```

## 下载二进制

GitHub Actions 会在两种情况下自动编译二进制包：

- 推送 `v*` tag 时：构建、测试、发布 GitHub Release。
- 手动运行 `build-release` workflow 时：构建、测试、上传 artifacts。

产物名：

- `thesis-docx-win-x64.zip`
- `thesis-docx-linux-x64.tar.gz`
- `thesis-docx-osx-x64.tar.gz`

发布包包含 CLI、`README.md`、`LICENSE`、`profile-viewer/` 和 `Thesis/thesis-docx/SKILL.md`。Windows 下解压后直接用 exe 后面跟参数：

```powershell
.\Thesis.Cli.exe operations list
.\Thesis.Cli.exe compose --template "模板.docx" --content ".analysis\content.json" --out ".analysis\candidate.docx" --report ".analysis\candidate.json" --render-config render.json --pdf ".analysis\candidate.pdf" --workdir ".analysis\run" --require-ready false
```

## 已有初稿套入模板

统一入口是 `compose`：DOCX 初稿和 `content.json` 都进入 `Thesis.Application` 的同一套版、渲染、验收及事务输出服务。`apply-template` 使用同一服务，默认允许输出待审候选稿；`compose` 默认要求交付门禁通过。旧 `finalize-all` 的 Profile/参考稿模式保留兼容，COM 最终化共用同一服务；带 `--plan` 时转入统一套版链路。

学校正式模板和学长的合格论文均可作为版式样本。先分析区域，再生成映射草案；样式按实际段落格式与继承关系提取，支持数字和自定义样式 ID。草案中的 `pendingReviews` 必须逐项处理，尤其是封面个人信息、区域边界、节页码起始值和学校内容要求。算法建议不等于已审核模板。

```text
CLI 参数适配
  → Thesis.Application：内容准备、统一套版、渲染收敛、交付审核
    → Thesis.OpenXml：模板分析、格式与原生对象迁移、结构和内容校验
    → IDocumentLayoutEngine：WPS / Word / LibreOffice
      → Thesis.Host：进程隔离、超时、COM 自动化
    → Thesis.Core：文件事务和输出互斥
  ↔ Thesis.Schema：输入计划、配置、报告
```

草案区分中英文摘要、章标题、结论、参考文献和致谢，按常见格式样本取样，并识别“第%1章”等自动编号。模板前置页的内部节边界单独保留；正文按章节映射模板节，页眉 STYLEREF 同步到迁移后的标题样式。区域独立起页属于草案策略，仍须对照学校样例审核。

目录仅收录本次迁移的语义标题，避免封面自带的大纲级别混入目录。封面文本框保留，身份信息可通过显式文字替换更新。

```powershell
.\Thesis.Cli.exe document-convert --doc accepted.doc --out accepted.docx --host wps
.\Thesis.Cli.exe template-plan analyze --template accepted.docx --out .analysis/template-analysis.json
.\Thesis.Cli.exe template-plan draft --doc source.docx --template accepted.docx --out .analysis/draft-plan.json
```

`apply-template` 按经过审阅的方案迁移原生 DOCX 段落、表格和图片，并保留模板的封面、声明及节设置。方案记录章节重组、格式和明确的文字修订。

```powershell
.\Thesis.Cli.exe template-plan inspect --doc source.docx --template template.docx --plan reviewed-plan.json --out inspection.json
.\Thesis.Cli.exe template-plan bind --doc source.docx --template template.docx --plan reviewed-plan.json --out bound-plan.json
.\Thesis.Cli.exe apply-template --doc source.docx --template template.docx --plan bound-plan.json --out final.docx --report final.json
```

结构化文本使用 `compose --content content.json --template template.docx --project-rules project-rules.json ...`；未传 `--plan` 时自动生成待审草案。调试候选稿可显式使用 `--require-ready false`，并用 `--workdir .analysis/run` 保留草案和失败现场。

`inspect` 输出原稿块清单、内容预览、指纹、遗漏范围与需迁移的特性；`bind` 固定原稿和模板的 SHA-256。输入文件变动后应重新审阅方案再绑定。

### 套版方案

`schemaVersion` 为 `1.0`。`roles` 定义语义角色的字体、半磅字号、行段间距、缩进、大纲级别与分页策略。`blocks` 按最终阅读顺序排列，索引对应 `word/document.xml` 中 `w:body` 的直接子元素，范围为零基、左闭右开：

| type | 用途 |
| --- | --- |
| `templateRange` | 保留模板 `[start,end)`；`paragraphTexts` 按元素索引替换段落，`cellTexts` 按零基 `行,列` 填表格 |
| `sourceRange` | 迁移原稿 `[start,end)`，保留原生内容及包关系；重复复制同一元素会报错 |
| `paragraph` | 按 `text`、`role` 生成段落，正文 `[n]` 转为上标引文 |
| `table` | 用 `rows` 和 `columnWidths`（twips）生成表格 |
| `image` | 用 `imagePath`、`imageWidthEmu`、`imageHeightEmu` 嵌入图片，`text` 用作替代说明 |
| `toc` | 生成目录域、标题书签与页码缓存 |
| `section` | 克隆 `sectionTemplateIndex` 指定的模板节，设置页码格式和起始值；唯一 `finalSection:true` 位于方案末尾 |

范围可使用唯一完整文本 `startAnchor`、`endAnchor` 定位，零匹配或多个匹配均会报错。`requireSourceCoverage:true` 要求每个非节属性原稿块被复制，或写入带 `reason` 的 `skippedSourceRanges`。例如：

```json
{"start":0,"end":4,"reason":"封面由目标模板替换"}
```

`sourceTextReplacements` 和 `templateTextReplacements` 记录文字替换；`contentWidthTwips` 控制图表和目录宽度；`normalizePageFooters` 将包含 PAGE 域的浮动页脚转为居中普通段落。图片路径以方案文件所在目录为基准。

角色可用 `templateParagraphIndex` 指向模板正文直接子段落，复用其生效格式；`preserveEmphasis` 控制行内加粗、斜体和下划线，默认保留。`templateTableIndex` 指定模板表格样本；图表宽高限制按各自节的可用区域计算，保留合并单元格。节的分栏、方向和分节类型默认沿用模板，`columnCount`、`sectionBreakType` 可显式覆盖。

范围块的 `pageBreakBefore:true` 使首段另起一页；节块的 `continuePageNumbering:true` 清除克隆节中的页码重置值，适用于复用同一章版式。两者都应在草案审阅时确认。

`requirements` 声明带模板依据的内容门禁，支持 `keywords`、`sectionCharacters`、`journalReferences` 和 `requiredText`。例如：

```json
{"id":"conclusion-length","kind":"sectionCharacters","startAnchor":"结论","endAnchor":"参考文献","minimum":700,"evidence":"学校模板：结论不少于700字"}
```

关键词按分号、逗号或顿号分隔；字数按去空白后的字符数统计；期刊条目按 `[J]`/`[J/OL]` 标记计数。条目计数不验证文献真实性。缺失、重复的要求锚点会产生失败结果。

### 渲染与分页验收

推荐选择最终阅读环境的 WPS/Office 完成更新、保存和 PDF 导出；也保留 LibreOffice 引擎。准备包含 PyMuPDF 的 Python 环境：

```powershell
uv venv .venv
uv pip install --python .venv/Scripts/python.exe PyMuPDF==2.0
```

把以下配置保存为项目根目录的 `render.json`，按本机安装位置填写可执行文件路径；相对路径以配置文件所在目录解析：

```json
{
  "engine": "wps",
  "pythonPath": ".venv/Scripts/python.exe",
  "timeoutSeconds": 120,
  "maxPasses": 4
}
```

`engine` 支持 `wps`、`word`、`auto` 和 `libreoffice`。选择 LibreOffice 时另设 `libreOfficePath`。Office COM 通过 STA 工作进程运行，受 `timeoutSeconds` 限制；同一个引擎完成 DOCX 最终化和 PDF 导出。`doctor --render-config render.json` 检查基础依赖。WPS 目录刷新可能重建目录引用书签，因此页码验证使用独立目标书签，并对照 PDF 的真实页面。

```powershell
.\Thesis.Cli.exe apply-template --doc source.docx --template template.docx --plan bound-plan.json --out final.docx --report final.json --render-config render.json --pdf final.pdf --workdir render-evidence
```

生成后执行 Open XML 结构校验与逐节可用宽度检查，再读取 PDF 的真实页数、标题位置、页码和越界文字/图片。目录缓存变化时重新生成并渲染，直到缓存和页数稳定。DOCX、PDF、报告使用输出互斥、暂存和失败回滚；检查通过后统一提升，失败时保留原有输出。COM 任务跨进程串行执行，完成后保留用户已打开的文档。`--workdir` 保留各轮渲染证据。

报告分别给出 `assemblyPassed`、`structurePassed`、`contentPassed`、`layoutExecuted`、`layoutPassed`、`planReviewPassed` 和 `automatedChecksPassed`。`ready` 还要求 `pendingReviews` 已处理；未渲染、未绑定方案或存在待审事项时保持 false。字体、公式、跨页表格、图题位置和学术内容列为复核事项。渲染成功不等于学术内容或全部视觉细节已验收。

完成候选稿复核后，`delivery-review` 校验该份 DOCX、PDF、报告的 SHA-256，并要求 `review.json` 的 `completedChecks` 覆盖报告中全部 `pendingReviews`。审核记录包含 `documentSha256`、`pdfSha256`、`reportSha256`、`reviewer`、`completedChecks`、`notes`，然后原子提升这份已检查的候选稿，避免审核后重排产生新版本。

```powershell
.\Thesis.Cli.exe delivery-review --doc candidate.docx --pdf candidate.pdf --report candidate.json --review review.json --out final.docx --out-pdf final.pdf --out-report final.json
```

有序内容抽取保存带指纹的原稿快照，并保留段落、图表及行内内容顺序；旧 `paragraphs`、`sections`、`tables` JSON 仍可读取。原生迁移统一处理书签、内部链接、跨块交叉引用、自动编号及带预览的嵌入对象。脚注尾注、图片项目符号、无预览嵌入对象及 altChunk 仍需专门适配，预检会报告。正文文本在 Office 最终化前后做一致性校验，域的缓存结果允许更新。

## 兼容 Profile 链路

```text
模板/成品论文.docx
  -> inspect --doc                 读取正文、样式、表格、节、页眉页脚、批注、格式要求线索
  -> profile extract               提取 profile.json 基础格式画像
  -> content extract               从成品论文或已有正文稿提取 content.json
  -> project-rules.json            AI/人工补充模板里没有显式体现的要求
  -> rules merge                   合并为 final-rules.json
  -> assemble                      把结构化正文写入模板副本，保留前置页和正文节设置
  -> apply + writeBlock/request    老师反馈后的局部微调
  -> validate                      检查可离线验证的格式
  -> finalize plan/apply           用 Word/WPS 更新字段、目录、分页
  -> rehearsal compare             和参考成品做结构对比
```

生产线优先使用一条命令跑完整闭环：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize-all --template "模板.docx" --content ".analysis\content.json" --project-rules ".analysis\project-rules.json" --reference "成品论文.docx" --out ".analysis\final.docx" --workdir ".analysis\final-run"
```

如果有任务书、开题报告等需要放在摘要/正文前面的额外 DOCX，用 `--front-matter-doc`，可重复传参，顺序就是合入顺序：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize-all --template "模板.docx" --front-matter-doc "任务书.docx" --front-matter-doc "开题报告.docx" --content ".analysis\content.json" --project-rules ".analysis\project-rules.json" --reference "成品论文.docx" --out ".analysis\final.docx" --workdir ".analysis\final-run"
```

`finalize-all` 会在 `--workdir` 输出 `profile.json`、`final-rules.json`、`assembled.docx`、`candidate.docx`、`validate-before-finalize.json`、`host-finalization.json`、`validate-after-finalize.json`、`rehearsal-report.json`、`final-audit.json`、`repair-plan.json` 和 `manual-checklist.md`。只有 `final-audit.ready=true` 才会写入 `--out`；如果审计不通过，命令返回 error，保留既有 `--out`，候选稿留在 `--workdir\candidate.docx`。`--skip-host-finalize` 只适合离线试跑，不能作为正式终稿绿灯。

优先级：

```text
request.json 单次操作参数 > project-rules.json / final-rules.json > profile.json > 工具默认规则
```

正式终稿优先用 `content extract` 把成品论文或已有正文稿转成 `content.json`，人工/AI 审核后再用 `assemble --doc 模板.docx --content content.json --profile final-rules.json --out 输出.docx` 做第一版整篇装配；它复制模板后保留首个论文锚点之前的封面/前置页，从“摘要 / Abstract / 目录 / 第X章”这类锚点开始替换论文主体，并保留选中的正文节页面设置和页眉页脚关系。模板尾部的格式说明、示例参考文献等说明性内容会被丢弃。老师后续反馈、个别段落覆盖、局部格式调整再用 `apply --request request.json`。`generate --content` 仍然存在，但只适合作为空白文档草稿路径。

输入文件职责不要混用：

- `--template`：模板结构来源，也是 `profile.json` 的提取来源。
- `--project-rules`：只传人工/AI 补充的项目覆盖规则，不要传已经合并后的 `final-rules.json`。
- `--content`：论文正文结构，包括摘要、章节、段落、图片、表格、参考文献和致谢。
- `--front-matter-doc`：任务书、开题报告等额外前置 DOCX；它是内容来源，不是规则来源。
- `--reference`：老师认可的成品或人工终稿，用于 rehearsal/audit 对比；它不会被复制进候选稿。

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

## 4. 提取 content.json

`content extract` 用于把成品论文、已有正文稿或人工整理稿转成 `ThesisContent`。它会提取标题、中英文摘要、关键词、章节、段落、表格行列、参考文献和致谢，并额外输出 `content-extract-report.json` 供审核。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe content extract --doc "成品论文.docx" --out ".analysis\content.json" --report ".analysis\content-extract-report.json" --profile ".analysis\profile.json" --project-rules ".analysis\project-rules.json"
```

`content-extract-report.json` 里的 `ready=true` 只代表结构化抽取没有发现关键缺口。正式装配前仍应人工/AI 看一遍 `content.json`，重点检查章节层级、表格标题、参考文献和模板中无法体现的特殊要求。

最小 `content.json` 示例：

```json
{
  "schemaVersion": "1.0",
  "documentKind": "thesisContent",
  "title": "基于深度强化学习的星地协同任务卸载研究与实现",
  "author": "李同林",
  "abstractZh": "本文围绕星地协同任务卸载问题展开研究。",
  "keywordsZh": ["星地协同", "任务卸载", "深度强化学习"],
  "abstractEn": "This thesis studies task offloading in satellite-terrestrial collaborative networks.",
  "keywordsEn": ["satellite-terrestrial collaboration", "task offloading"],
  "chapters": [
    {
      "title": "第一章 绪论",
      "blocks": [
        { "type": "paragraph", "text": "本章介绍研究背景、研究意义和论文结构。" },
        {
          "type": "image",
          "path": "lizi/out/reward_curve.png",
          "caption": "图1-1 奖励收敛曲线",
          "altText": "奖励收敛曲线"
        },
        {
          "type": "table",
          "table": {
            "caption": "表1-1 算法性能对比",
            "headers": ["算法", "平均时延", "任务成功率"],
            "rows": [["MADDPG", "14.89 s", "49.21%"]]
          }
        }
      ]
    }
  ],
  "references": ["张三. 星地协同任务卸载研究[J]. 通信学报, 2025, 46(1): 1-10."],
  "acknowledgements": "感谢导师和同学的帮助。"
}
```

图片路径按运行命令时的当前目录或绝对路径解析；`widthEmu`、`heightEmu` 可选，单位是 EMU，不写时工具使用默认尺寸。表格格式不在 `content.json` 里硬编码，优先来自 `final-rules.json` 的 `tableDefault` 和表格角色规则。

## 5. 用 assemble 装配整篇正文

`content.json` 用来表达论文内容结构：标题、作者、中英文摘要、关键词、章节、段落、表格、参考文献和致谢。格式来自 `final-rules.json`，表格默认使用规则里的 `tableDefault`，参考文献会避免重复 `[1]` 编号。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe assemble --doc "模板.docx" --content ".analysis\content.json" --profile ".analysis\final-rules.json" --out ".analysis\thesis.docx"
```

带前置材料的装配：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe assemble --doc "模板.docx" --front-matter-doc "任务书.docx" --front-matter-doc "开题报告.docx" --content ".analysis\content.json" --profile ".analysis\final-rules.json" --out ".analysis\thesis.docx"
```

`--front-matter-doc` 会把外部 DOCX 的正文块插到论文主体前，适合任务书、开题报告、承诺书等额外材料；可重复传参，顺序即插入顺序。它会复制常见段落、表格和图片关系，但真实分页、节边界和页眉页脚仍要经过 WPS/Word 最终化和抽查。

这条路径是当前最接近“正式工具”的整篇写作入口：它不是从空白文件开始，而是在模板副本上写入内容。当前版本会保留首个论文锚点之前的模板前置内容，替换论文主体范围，保留正文节设置，并丢弃模板尾部的格式说明/示例内容；如果模板里找不到可识别的论文锚点或节边界，工具会回退到旧的整主体重写路径。它还不是完整的终稿排版引擎：目录、字段、分页、节状态归一化仍必须经过 Word/WPS `finalize apply`。

## 6. 一键生成终稿候选

当 `content.json` 和 `project-rules.json` 已经准备好，优先用 `finalize-all` 跑完整生产线。它会重新从模板提取 `profile.json`，合并项目规则，装配正文，执行最终化，二次校验，对照参考成品做 rehearsal，并生成机器可读审计和人工检查清单。生产线门禁是 `final-audit.ready=true`：通过才把候选稿提升为 `--out`，不通过只保留 `--workdir\candidate.docx` 和审计产物，避免覆盖旧终稿。

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize-all --template "模板.docx" --content ".analysis\content.json" --project-rules ".analysis\project-rules.json" --reference "成品论文.docx" --out ".analysis\final.docx" --workdir ".analysis\final-run"
```

带任务书/开题报告的正式候选：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize-all --template "模板.docx" --front-matter-doc "任务书.docx" --front-matter-doc "开题报告.docx" --content ".analysis\content.json" --project-rules ".analysis\project-rules.json" --reference "成品论文.docx" --out ".analysis\final.docx" --workdir ".analysis\final-run"
```

如果本机暂时不能启动 WPS/Word，可以加 `--skip-host-finalize` 做离线试跑：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize-all --template "模板.docx" --content ".analysis\content.json" --project-rules ".analysis\project-rules.json" --reference "成品论文.docx" --out ".analysis\candidate.docx" --workdir ".analysis\candidate-run" --skip-host-finalize
```

离线试跑会生成 `candidate.docx` 和完整审计文件，但 `final-audit.json` 会把 `host_finalization_missing` 放入 `requiresWps`，不会写入正式 `--out`。正式生产线必须保留 `--reference`，否则审计会降级为 reduced confidence。

`final-audit.ready=false` 时按这个顺序处理：

1. 先读 `--workdir\final-audit.json`，看 `blocking`、`requiresWps`、`requiresHuman`。
2. 再读 `--workdir\repair-plan.json`，优先修复 `automatic=true` 或明确指向 `content.json`、`project-rules.json` 的项。
3. 打开 `--workdir\candidate.docx` 做人工/WPS 检查，但不要把它当正式终稿交付。
4. 按 `manual-checklist.md` 检查目录页码、真实分页、跨页表格、续表标题、孤行标题。
5. 修完后重跑同一条 `finalize-all` 命令，直到 `final-audit.ready=true`，再使用 `--out`。

## 7. 用 writeBlock 微调正文

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

## 8. 校验和最终化

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe validate --doc ".analysis\thesis-revised.docx" --profile ".analysis\final-rules.json"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize plan --doc ".analysis\thesis-revised.docx"
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe finalize apply --doc ".analysis\thesis-revised.docx" --out ".analysis\final.docx"
```

如果没有执行微调步骤，就把上面的 `.analysis\thesis-revised.docx` 换成 `assemble` 生成的 `.analysis\thesis.docx`。

离线能检查和处理：DOCX 结构、段落格式、角色格式、三线表边框、重复表头、参考文献编号、目录字段标记。

必须用 Word/WPS 或人工确认：真实分页、目录页码、孤行、跨页表格显示、自动续表标题。

## 9. 和成品论文做实战对比

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

## 10. 结构化草稿路径

需要快速从内容 JSON 出一个草稿时，可以使用：

```powershell
.\src\Thesis.Cli\bin\Debug\net10.0\Thesis.Cli.exe generate --content ".analysis\content.json" --rules ".analysis\final-rules.json" --out ".analysis\draft.docx"
```

这条路径会自动插入目录字段，生成后仍需执行 `finalize apply` 更新目录和分页。它不会完整保留模板原有页眉页脚、节、字段和复杂结构，不应作为正式终稿主路径。

## 11. 查看规则 JSON

打开：

```text
profile-viewer/index.html
```

可拖入 `profile.json`、`project-rules.json` 或 `final-rules.json`，本地查看规则概览和原始 JSON。

## 能力范围

- 提取模板画像，合并项目规则，并在模板副本中装配正文和前置材料。
- 保留原生图片、表格、行内对象与普通书签，检查文档包、节、字段及样式规则。
- `finalize-all` 支持 Windows Word/WPS COM 最终化、参考稿对比和审计；COM 路径仅适用于 Windows。
- `apply-template` 支持离线 LibreOffice 渲染、目录页码回填与版面检查；分页受字体和渲染器版本影响。
- 内容质量、论证、参考文献真实性和细节排版仍需人工复核。

## Skill 目录

给 Codex/Agent 封装时，建议创建 `Thesis` 目录，把 skill 放在：

```text
Thesis/thesis-docx/SKILL.md
```

技能名称只能包含小写字母、数字和连字符。正确示例：`thesis-docx`。不要使用大写字母、下划线、空格或中文技能名。

## 开发检查

```powershell
dotnet test tests/Thesis.Tests --configuration Release --collect "XPlat Code Coverage"
node --test profile-viewer/profile-viewer.test.mjs
uv run --python .venv/Scripts/python.exe python -m unittest discover -s tests/layout -v
```

GitHub Actions 检查 Windows/Linux 构建与测试、profile viewer、PDF 审计脚本，以及真实 LibreOffice 套版导出集成测试；失败产物上传用于排查。WPS/Word 集成测试可在安装 Office 的 Windows 环境执行：`python tests/integration/test_compose.py --cli src/Thesis.Cli/bin/Release/net10.0/Thesis.Cli.dll --engine wps`。源文档、成品、分析报告、截图、环境和构建产物保留本地；测试使用运行时生成的最小样例。
