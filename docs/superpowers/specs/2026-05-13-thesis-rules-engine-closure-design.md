# Thesis Rules Engine Closure Design

## Goal

把当前 Thesis DOCX CLI 收束成论文规则引擎：从模板或成品论文抽取 `profile.json`，从模板正文/批注和 AI 补充规则形成 `project-rules.json`，合并为 `final-rules.json`，再用内容 JSON 生成论文，并允许老师反馈后继续用 request JSON 微调。

## P0：规则闭环

新增直接文档检查命令：

```powershell
Thesis.Cli.exe inspect --doc "template.docx"
```

输出 `documentMap`，包含段落、样式、编号、节、表格、批注和从正文/批注中识别出的规则线索。规则线索不是最终判断，只给 AI 和用户审阅，例如包含“格式、要求、应、须、字体、字号、行距、三线表、首行缩进、页边距、目录、参考文献”等关键词的文本。

新增项目级规则文件 `project-rules.json`：

```json
{
  "schemaVersion": "1.0",
  "rulesKind": "projectRules",
  "roleAliases": { "zhAbstract": "abstract.zh" },
  "pageSetup": { "margins": { "leftTwips": 1701 } },
  "roleFormats": {
    "body": { "firstLineIndentTwips": 480, "lineSpacing": "360" }
  },
  "rolePolicies": [],
  "tableDefault": null,
  "tableArchetypes": [],
  "diagnostics": []
}
```

新增合并命令：

```powershell
Thesis.Cli.exe rules merge --profile ".analysis/profile.json" --project ".analysis/project-rules.json" --out ".analysis/final-rules.json"
```

合并结果仍是可被现有 `--profile` 参数读取的 `TemplateProfile`，同时保留 `roleAliases`。优先级为：`request.json` 单次操作参数 > `project-rules.json` > `profile.json` > 默认规则。

## P1：论文内容生成

新增内容 JSON：

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

新增生成命令：

```powershell
Thesis.Cli.exe generate --content ".analysis/content.json" --rules ".analysis/final-rules.json" --out ".analysis/thesis.docx"
```

生成器按 `final-rules.json` 的页面设置、角色格式、表格默认样式创建 DOCX。生成结果不承诺真实分页，后续仍需 `validate` 和 `finalize plan/apply`。

## P2：交付工作流

更新 `README.md` 和 `thesis-docx/SKILL.md`，把完整链路固定为：

```text
template.docx / sample.docx
  -> inspect --doc
  -> profile extract
  -> AI 生成 project-rules.json
  -> rules merge 得到 final-rules.json
  -> generate 得到 thesis.docx
  -> validate
  -> finalize plan/apply
  -> 老师反馈后用 request.json 微调
```

`profile-viewer` 扩展为可查看 `profile.json`、`final-rules.json` 和 `project-rules.json`，继续保持本地浏览器解析，不上传文件。

## Non-Goals

- 不做论文内容质量、查重、AI 检测保证。
- 不在纯 OpenXML 中计算真实分页、目录页码、孤行或自动续表标题。
- 不把批注中的自然语言自动判定为强规则；只提取线索，最终由 AI 生成结构化 `project-rules.json`。
