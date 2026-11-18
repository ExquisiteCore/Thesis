using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

/// <summary>Editable design is separate from a manuscript-bound assembly plan.</summary>
public static class TemplateDesigner
{
    public static TemplateDesign Inspect(string path)
    {
        var plan = TemplatePlanDraftBuilder.Build(path, path);
        if (!OpenXmlDocumentInspector.TryInspect(path, out var map, out var diagnostic) || map is null)
            throw new InvalidDataException(diagnostic?.Message ?? "模板解析失败。");
        using var document = WordprocessingDocument.Open(path, false);
        var main = document.MainDocumentPart!;
        var design = new TemplateDesign { Name = Path.GetFileNameWithoutExtension(path), SourcePath = Path.GetFullPath(path), SourceSha256 = TemplatePlanPreflight.Fingerprint(path), ReviewNotes = plan.PendingReviews };
        var margin = main.Document!.Body!.Descendants<PageMargin>().LastOrDefault();
        if (margin is not null)
        {
            design.MarginTopMm = (margin.Top?.Value ?? 1440) / 1440d * 25.4;
            design.MarginBottomMm = (margin.Bottom?.Value ?? 1440) / 1440d * 25.4;
            design.MarginLeftMm = (margin.Left?.Value ?? 1701) / 1440d * 25.4;
            design.MarginRightMm = (margin.Right?.Value ?? 1417) / 1440d * 25.4;
        }
        foreach (var (role, format) in plan.Roles)
        {
            if (role == "cover") continue;
            var sample = map.Paragraphs.FirstOrDefault(p => p.BodyElementIndex == format.TemplateParagraphIndex);
            // Resolve the actual inheritance chain rather than displaying only direct formatting.
            var resolved = TemplateRoleStyleBuilder.Build(main, "Preview_" + role, format);
            var run = resolved.StyleRunProperties;
            var paragraph = resolved.StyleParagraphProperties;
            var spacing = paragraph?.GetFirstChild<SpacingBetweenLines>();
            var indentation = paragraph?.GetFirstChild<Indentation>();
            design.Roles.Add(new()
            {
                Role = role, Name = RoleName(role), SampleIndex = format.TemplateParagraphIndex,
                SampleText = format.TemplateParagraphIndex is int index && main.Document!.Body!.ChildElements[index] is Paragraph sampleParagraph
                    ? TemplateSampleText.FlowText(sampleParagraph) : sample?.Text ?? "",
                Confidence = plan.PendingReviews.Any(r => r.Contains("'" + role + "'", StringComparison.Ordinal)) ? 0.35 : 0.85,
                Font = run?.GetFirstChild<RunFonts>()?.EastAsia?.Value ?? "宋体",
                LatinFont = run?.GetFirstChild<RunFonts>()?.Ascii?.Value ?? "Times New Roman",
                Size = double.TryParse(run?.GetFirstChild<FontSize>()?.Val?.Value, CultureInfo.InvariantCulture, out var size) ? size / 2 : 12,
                Bold = run?.GetFirstChild<Bold>() is { } bold && (bold.Val?.Value ?? true),
                Alignment = paragraph?.GetFirstChild<Justification>()?.Val?.InnerText ?? "left",
                LineSpacing = spacing?.LineRule?.Value == LineSpacingRuleValues.Auto && double.TryParse(spacing.Line?.Value, out var line) ? line / 240 : 1.5,
                FirstLineCharacters = indentation?.FirstLineChars?.Value is int chars ? chars / 100d : 0
            });
        }
        if (map.RequirementHints.Count > 0) design.ReviewNotes.Add("模板含文字要求，请在规则表中填写已核对的字数、关键词或参考文献门槛。");
        return design;
    }

    public static string RoleName(string role) => role switch
    {
        "body" => "正文", "heading1" => "一级标题", "heading2" => "二级标题", "heading3" => "三级标题",
        "abstractHeading" => "中文摘要标题", "abstract" => "中文摘要", "abstractEnglishHeading" => "英文摘要标题",
        "abstractEnglish" => "英文摘要", "referencesHeading" => "参考文献标题", "reference" => "参考文献",
        "backMatterHeading" => "致谢标题", "conclusionHeading" => "结论标题", "table" => "表格文字", "image" => "插图",
        "toc1" => "目录一级", "toc2" => "目录二级", "toc3" => "目录三级",
        "toc" => "目录内容", "tocHeading" => "目录标题", "keywords" => "中文关键词", "keywordsEnglish" => "英文关键词",
        "englishTitle" => "英文题目", "figureCaption" => "图题", "tableCaption" => "表题", "conclusion" => "结论正文",
        "backMatter" => "致谢正文", "cover" => "封面", _ => role
    };

    public static void Apply(TemplateDesign design, TemplateApplicationPlan plan)
    {
        Validate(design);
        foreach (var role in design.Roles)
        {
            if (!plan.Roles.TryGetValue(role.Role, out var format)) continue;
            if (role.Confirmed && role.SampleIndex.HasValue)
            {
                format.TemplateParagraphIndex = role.SampleIndex;
                format.ApplyTemplateInstructions = false;
            }
            if (role.OverrideFormat)
            {
                format.TemplateParagraphIndex = null;
                format.EastAsiaFont = role.Font; format.LatinFont = role.LatinFont;
                format.SizeHalfPoints = (int)Math.Round(role.Size * 2); format.Bold = role.Bold;
                format.Alignment = role.Alignment; format.Line = (int)Math.Round(role.LineSpacing * 240); format.LineRule = "auto";
                format.FirstLineChars = (int)Math.Round(role.FirstLineCharacters * 100);
            }
            if (role.Confirmed) plan.PendingReviews.RemoveAll(r => r.StartsWith("Confirm template format for role '" + role.Role + "':", StringComparison.Ordinal));
        }
        plan.Requirements = ThesisJson.Deserialize<List<ContentRequirement>>(ThesisJson.Serialize(design.Requirements));
    }

    public static void Validate(TemplateDesign design)
    {
        if (design.SchemaVersion != "1.0" || !Guid.TryParseExact(design.Id, "N", out _) || string.IsNullOrWhiteSpace(design.Name)) throw new InvalidDataException("模板设计格式不完整。");
        if (new[] { design.MarginTopMm, design.MarginBottomMm, design.MarginLeftMm, design.MarginRightMm }.Any(v => !double.IsFinite(v) || v < 0 || v > 80)) throw new InvalidDataException("页边距应为 0–80 毫米。");
        if (design.Roles.Select(r => r.Role).Distinct().Count() != design.Roles.Count) throw new InvalidDataException("模板角色重复。");
        var ids = new HashSet<string>();
        foreach (var requirement in design.Requirements)
            if (requirement is null || string.IsNullOrWhiteSpace(requirement.Id) || !ids.Add(requirement.Id) || string.IsNullOrWhiteSpace(requirement.Evidence)
                || requirement.Kind is not ("keywords" or "sectionCharacters" or "journalReferences" or "requiredText")
                || requirement.Minimum is < 0 || requirement.Maximum is < 0 || requirement.Minimum > requirement.Maximum)
                throw new InvalidDataException("内容要求需要唯一名称、有效类型与学校要求依据。");
        foreach (var r in design.Roles)
            if (!double.IsFinite(r.Size) || r.Size < 5 || r.Size > 72 || !double.IsFinite(r.LineSpacing) || r.LineSpacing < .5 || r.LineSpacing > 5
                || !double.IsFinite(r.FirstLineCharacters) || r.FirstLineCharacters < 0 || r.FirstLineCharacters > 10
                || string.IsNullOrWhiteSpace(r.Font) || string.IsNullOrWhiteSpace(r.LatinFont)
                || r.Alignment is not ("left" or "center" or "right" or "both" or "distribute")) throw new InvalidDataException("模板字体、字号、对齐或段落参数超出范围：" + r.Name);
    }

    public static void ApplyMargins(string path, TemplateDesign design)
    {
        if (!design.ApplyMarginsToAllSections) return;
        Validate(design);
        using var doc = WordprocessingDocument.Open(path, true);
        foreach (var section in doc.MainDocumentPart!.Document!.Body!.Descendants<SectionProperties>())
        {
            var margin = section.GetFirstChild<PageMargin>() ?? section.AppendChild(new PageMargin());
            margin.Top = (int)Math.Round(design.MarginTopMm / 25.4 * 1440); margin.Bottom = (int)Math.Round(design.MarginBottomMm / 25.4 * 1440);
            margin.Left = (uint)Math.Round(design.MarginLeftMm / 25.4 * 1440); margin.Right = (uint)Math.Round(design.MarginRightMm / 25.4 * 1440);
        }
        doc.MainDocumentPart!.Document.Save();
    }

    public static void CreateDefault(string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles();
        styles.Styles.Append(new Style(new StyleName { Val = "Normal" }, new StyleParagraphProperties(new SpacingBetweenLines { Line = "360", LineRule = LineSpacingRuleValues.Auto }, new Indentation { FirstLineChars = 200 }),
            new StyleRunProperties(new RunFonts { EastAsia = "宋体", Ascii = "Times New Roman", HighAnsi = "Times New Roman" }, new FontSize { Val = "24" })) { StyleId = "Normal", Type = StyleValues.Paragraph, Default = true });
        for (var level = 1; level <= 3; level++)
            styles.Styles.Append(new Style(new StyleName { Val = "heading " + level }, new BasedOn { Val = "Normal" },
                new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "240", After = "120" }, new Indentation { FirstLineChars = 0 }, new OutlineLevel { Val = level - 1 }),
                new StyleRunProperties(new RunFonts { EastAsia = "黑体" }, new Bold(), new FontSize { Val = level == 1 ? "32" : "28" })) { StyleId = "Heading" + level, Type = StyleValues.Paragraph });
        var body = new Body(); main.Document = new Document(body);
        void P(string text, string style = "Normal") => body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = style }), new Run(new Text(text))));
        P("{{学校}}"); P("毕业论文"); P("{{题目}}"); P("作者：{{作者}}"); P("学号：{{学号}}"); P("指导教师：{{导师}}"); P("{{日期}}");
        P("摘要", "Heading1"); P("中文摘要内容。请根据学校要求修改本通用模板。"); P("关键词：模板；论文；排版");
        P("Abstract", "Heading1"); P("English abstract content."); P("Keywords: thesis; template; typesetting");
        P("目录"); body.Append(new Paragraph(new SimpleField { Instruction = " TOC \\o \"1-3\" \\h \\z \\u " }));
        P("第1章 绪论", "Heading1"); P("此处是正文示例，用于识别宋体、首行缩进及段落间距。");
        P("1.1 研究背景", "Heading2"); P("正文示例。这里介绍研究背景和研究目的。");
        P("1.1.1 研究问题", "Heading3"); P("正文示例。");
        P("结论", "Heading1"); P("总结研究工作与主要结论。"); P("参考文献", "Heading1"); P("[1] 作者. 文献题名[J]. 期刊, 2026, 1(1): 1-5."); P("致谢", "Heading1"); P("感谢指导与帮助。");
        var footer = main.AddNewPart<FooterPart>(); footer.Footer = new Footer(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }), new SimpleField { Instruction = " PAGE " }));
        body.Append(new SectionProperties(new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) }, new PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = 1440, Bottom = 1440, Left = 1701, Right = 1417, Header = 720, Footer = 720, Gutter = 0 }, new PageNumberType { Format = NumberFormatValues.Decimal, Start = 1 }));
        main.Document.Save();
    }
}
