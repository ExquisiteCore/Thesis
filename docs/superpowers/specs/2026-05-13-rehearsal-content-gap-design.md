# Rehearsal Content Gap Design

## Goal

让 `rehearsal compare` 从“数量差异提示”升级为“可定位的内容缺口报告”，用于判断候选终稿是否漏掉参考稿中的正文段落或表格。

## Approach

沿用现有 `rehearsal compare` 命令，不新增入口。报告继续输出候选稿/参考稿摘要、标题覆盖、校验结果和 diagnostics，同时在 `contentCoverage` 下新增 `missingReferenceParagraphCount`、`missingReferenceTableCount` 和 `gaps`。

`gaps` 是面向修补的结构化列表。每个缺口包含：

- `gapType`: `paragraph` 或 `table`
- `severity`: 当前统一为 `warning`
- `referenceIndex`: 参考稿中的段落或表格索引
- `referenceContext`: 最近的上级标题，用于定位章节
- `referenceTextPreview`: 缺失内容预览
- `message`: 人可读说明

正文缺口通过参考稿正文段落与候选稿正文段落进行归一化比较和字符 n-gram 相似度比较。比较范围从论文正文起点开始，正文起点取第一个摘要标题或章节标题，因此封面、任务书、原创性声明、授权页等前置表单不进入 `gaps`。标题、目录行、Word/WPS 域代码、空段落、过短段落和表题不作为正文缺口。表格缺口通过表格文本预览归一化比较；正文起点之后候选稿没有对应表格时输出表格缺口。

## Boundaries

本阶段不自动修补论文，只报告缺口；修补仍由后续 `writeBlock`、表格插入或内容 JSON 修订完成。本阶段也不做语义级 AI 判断，避免引入不稳定依赖。相似度阈值采用保守值，宁愿少报明显重复内容，也要避免把同一段拆分差异误报成大量缺失。

## Validation

新增 CLI 测试覆盖：

- 参考稿比候选稿多一个正文段落时，`contentCoverage.gaps` 输出 `paragraph` 缺口并包含章节上下文。
- 参考稿比候选稿多一个表格时，`contentCoverage.gaps` 输出 `table` 缺口并包含表格预览。
- Word/WPS 的 TOC/REF/PAGEREF 域代码和目录页码不制造正文缺口。
- 封面和任务书等正文起点之前的段落、表格不制造正文缺口。
- 现有标题覆盖和最终化诊断行为保持不变。
