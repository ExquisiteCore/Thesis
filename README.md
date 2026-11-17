# Thesis 论文工作台与 CLI

这个工具的目标不是“凭空生成一个论文文件”，而是把学校模板或成品论文变成可执行规则，然后在模板副本上按语义写入正文、表格、图片、参考文献，并继续支持老师反馈后的微调。

## Windows 论文工作台（WinUI 3）

GUI 面向写作者，CLI + Skill 面向 Agent，是两个独立入口。它们共享 `.thesis.json`、模板设计和 `Thesis.Application`，不要求同时运行。Agent 更新项目后，处于已保存状态的 GUI 会自动刷新；本地有未保存内容时通过指纹保护，保留双方版本。

```powershell
dotnet build Thesis.Desktop.slnx --configuration Release
.\src\Thesis.Gui\bin\Release\net10.0-windows10.0.19041.0\win-x64\Thesis.Gui.exe
# 打开指定项目
.\src\Thesis.Gui\bin\Release\net10.0-windows10.0.19041.0\win-x64\Thesis.Gui.exe --project "我的论文.thesis.json"
# 生成无需另装 .NET 的便携包与校验清单
.\tools\publish-desktop.ps1 -Archive
# Windows 原生界面回归（独立测试构建，生产包不包含测试入口）
.\tools\test-desktop.ps1
```

桌面端支持 Windows 10 1809 或更新版本。源码构建需要 Windows 和 .NET 10 SDK；发布包包含 .NET 和 Windows App SDK，解压后运行 `Thesis.Gui.exe`。可选执行包内 `install-desktop.ps1` 创建桌面快捷方式；`-RemoveShortcut` 仅移除快捷方式。包内附文件校验清单及第三方许可。跨平台 CLI 使用 `ThesisTool.slnx`，桌面构建使用 `Thesis.Desktop.slnx`。GitHub Release 分别提供 CLI 和 Windows GUI 包。

工作流程：新建项目 → 导入或设计模板 → 导入 / 编辑正文 → 快速预览 → 检查 → 生成 DOCX；最终分页与 PDF 导出按需使用 WPS/Word。

| 工作区 | 已实现能力 |
| --- | --- |
| 首页 | 最近项目、新建向导、打开项目、导入便携项目包 |
| 写作 | 章节/小节增删排序，摘要与关键词，原生富文本、格式工具栏、撤销/重做、源码视图、分屏预览、查找、本地图片，Ctrl+N/O/S |
| 模板 | 通用 A4 模板、学校模板/合格论文识别、角色置信度和样本重选、字体/段落调整、页边距、封面替换、内容要求、可复用模板库 |
| 快速预览 | 内置 Open XML 内容预览，文字/图表/主要样式/链接；24 个内容块分段呈现，避免一次创建长文全部控件；导出的 PDF 使用 Windows 原生 PDF API 显示 |
| 检查 | 空章节/缺失素材/待确认模板规则/学校内容要求，点击内容问题定位章节；正式导出继续执行结构、内容、页码与越界检查 |
| 历史 | 自动恢复记录、每五分钟合并一次自动保存检查点、导入与恢复前快照，历史/当前内容并列查看，恢复前保留当前版本 |

编辑停止约一秒后自动保存。项目使用串行写入、指纹校验、文件事务；冲突可另存副本。模板修改须点击“保存并应用”。格式识别的确认不等于整篇论文已验收；候选文件保留 `ready=false` 及具体待审理由。任务取消在阶段边界生效，正在执行的 Office 调用结束或超时后停止。

“新建论文”在同一窗口填写题目、作者和保存位置，默认存到文档目录的 `Thesis/题目/题目.thesis.json`；可直接编辑路径或点击“浏览”。取消选择位置会保留已填内容，创建失败会在原窗口说明原因。左侧导航切换工作区，页面上方统一显示操作错误，模板页的保存按钮固定在表单下方。

项目资源在 `assets/`，模板、历史、恢复和任务证据在 `.thesis/`。移动项目推荐“导出项目包”：它携带正文/模板/素材，导入到新目录；本机排版软件配置与已绑定旧方案不跨机复用。仅“另存项目副本”仍会引用原素材。删除项目目录前请先导出项目包；程序不自动清理历史或用户素材。

### Markdown 范围与预览边界

- 普通正文默认使用 WinUI 原生富文本编辑：选中文字设置粗体、斜体、删除线，选择正文/三级标题、项目符号或编号；工具栏同步当前选择，撤销记录仅属于当前章节。明暗主题使用纸面、墨色和青绿色强调色。
- Markdown 是导入导出与内容存储格式。正文的复杂表格、链接、图片、公式、原稿保真片段、多级列表及特殊编号暂时使用源码视图；简化为受支持的正文后可切回富文本。完整的 Word 式对象编辑仍在建设中。
- 导入 `.md` / `.markdown`，一个 H1 为论文题目，H2 为章节，H3 为小节；摘要、Abstract、参考文献、致谢按名称识别。元信息可用 `thesis-metadata` JSON 代码块携带作者和关键词。
- 正文章节保存 Markdown 原文，支持粗体、斜体、删除线、上/下标、列表、引用、链接、表格、代码和本地图片。图片复制到项目资产，并按 SHA-256 命名；导出 Markdown 时同时导出 `.assets` 目录。
- Word 导入先保留原生内容，避免丢失复杂对象。选择整章后可转为 Markdown：普通段落可编辑，复杂对象保留为带源文件指纹的 `thesis-native` 片段。请勿改动片段的源路径、索引和指纹；完整富文本/公式可视编辑仍在后续路线中。
- `$...$` / `$$...$$` LaTeX 当前保留源码并产生预检提示，尚未转换成 Word 数学排版；DOCX 原生公式按原结构迁移。HTML 不执行，远程图片需先下载到本地再导入。
- 快速预览不是 Word 分页引擎：显示主体内容和主要样式，合并单元格、浮动对象、页眉页脚、自动编号、复杂公式和实际分页以最终 DOCX/PDF 为准。
- 最终 PDF 页面显示使用 Windows 自带 API，PDF 文字/页码/越界审计默认使用纯 .NET PdfPig；日常编辑和快速预览均不启动 COM，也不依赖 Python。

### 依赖与架构

`Thesis.Gui`（WinUI 界面 / ViewModel）与 `Thesis.Cli`（命令协议）→ `Thesis.Application`（项目、模板库、导入导出、历史、预览编排、套版事务）→ `Core / Schema / OpenXml / Profile / Host`。复杂功能按模板、预览、工作区拆分；两端共享同一生成和校验服务。

新增运行时依赖只有纯 .NET 的 Markdig 1.6.0（CommonMark 解析）和 PdfPig 0.1.16（PDF 读取）。继续使用既有 Open XML SDK 与 Windows App SDK，没有引入浏览器、Node、数据库或转换服务。旧 `pythonPath` 配置仍可选用 PyMuPDF 兼容审计器。

预览方案参考：[docx-preview](https://github.com/VolodymyrBaydalka/docxjs)、[Mammoth](https://github.com/mwilliamson/mammoth.js)、[ONLYOFFICE](https://api.onlyoffice.com/docs/docs-api/get-started/how-it-works/)、[DeepSeek Harness 本地 Office 预览设计](https://github.com/deepseek-ai/deepseek-harness/blob/master/.agents/notes/archived/architecture/2026-09-10-local-office-preview.zh.md)。Harness 使用 LibreOffice WASM 转 PDF，再由 PDF.js 显示；本项目按轻量原生预览要求选择 Open XML 重绘，避免内嵌完整排版引擎的启动、内存与发布成本。

### 后续路线

- [ ] GUI 内集成 AI：围绕当前章节、大纲与模板规则提出编辑建议，差异确认后应用；CLI + Skill 继续独立运行。
- [ ] 原生公式、脚注尾注、合并表格与浮动图形的逐块可视编辑，以及 LaTeX → OMML。
- [ ] 更完整的模板封面/页眉页脚可视设计与章节分页对照；快速预览继续与正式分页明确区分。
- [ ] 自动更新、签名安装包、多语言与键盘/屏幕阅读器实机验收。


Agent 可以直接接续桌面项目：

```powershell
Thesis.Cli.exe project create --out "我的论文.thesis.json" --content "content.json" --template "模板.docx"
Thesis.Cli.exe project inspect --project "我的论文.thesis.json"
Thesis.Cli.exe project update --project "我的论文.thesis.json" --expected-sha256 "inspect 返回的 projectFingerprint" --content "修改后的content.json"
Thesis.Cli.exe project compose --project "我的论文.thesis.json" --out "candidate.docx" --render false --require-ready false
```

`project update` 会检查读取时的指纹，并在替换正文后清除旧套版方案。`project compose` 默认要求完整交付门禁；预览候选请显式传 `--require-ready false`。同一正文、资产和模板规则复用经校验的源 DOCX 快照，绑定方案据此保持稳定；正文或模板改变后需重新校准。生成的报告、方案和成品通过同一事务提交。

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

推荐选择最终阅读环境的 WPS/Office 完成更新、保存和 PDF 导出；也保留 LibreOffice 引擎。默认 PDF 审计已经内置。仅使用旧 PyMuPDF 审计器时才准备以下环境：

```powershell
uv venv .venv
uv pip install --python .venv/Scripts/python.exe PyMuPDF==2.0
```

把以下配置保存为项目根目录的 `render.json`，按本机安装位置填写可执行文件路径；相对路径以配置文件所在目录解析：

```json
{
  "engine": "wps",
  "timeoutSeconds": 120,
  "maxPasses": 4
}
```

`pythonPath` 可省略，使用内置 .NET 审计；显式配置该路径时启用旧 PyMuPDF 审计器。`engine` 支持 `wps`、`word`、`auto` 和 `libreoffice`。选择 LibreOffice 时另设 `libreOfficePath`。Office COM 通过 STA 工作进程运行，受 `timeoutSeconds` 限制；同一个引擎完成 DOCX 最终化和 PDF 导出。`doctor --render-config render.json` 检查基础依赖。WPS 目录刷新可能重建目录引用书签，因此页码验证使用独立目标书签，并对照 PDF 的真实页面。

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
