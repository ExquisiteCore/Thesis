using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Application;
using Thesis.OpenXml;
using Thesis.Schema;
using Xunit;

public sealed class TemplateSampleRegressionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thesis-template-sample-" + Guid.NewGuid().ToString("N"));
    public TemplateSampleRegressionTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string Create(string name, params OpenXmlElement[] blocks)
    {
        var path = Path.Combine(directory, name);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body(blocks.Append(new SectionProperties(
            new PageSize { Width = 11906, Height = 16838 }, new PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440 }))));
        return path;
    }

    private static Paragraph P(string text, int? level = null)
        => new(level is int value ? new ParagraphProperties(new OutlineLevel { Val = value }) : new ParagraphProperties(),
            new Run(new RunProperties(new Color { Val = "000000" }, new RunFonts { EastAsia = "黑体" }), new Text(text)));

    private static Run Box(string text, string id)
    {
        var content = new TextBoxContent(new Paragraph(new Run(new RunProperties(new Color { Val = "FF0000" }), new Text(text))));
        var shape = new DocumentFormat.OpenXml.Vml.Shape(new DocumentFormat.OpenXml.Vml.TextBox(content)) { Id = id };
        return new Run(new Picture(shape));
    }

    [Fact]
    public void FloatingGuidanceDoesNotPolluteOutlineInspectionOrHeadingFormat()
    {
        var chapter = P("第 1 章 引言", 0);
        chapter.PrependChild(Box("一级标题排版：黑体，二号，居中，段前0行，段后1行。", "chapter-note"));
        var section = P("1.1 研究背景", 0); // The real university template incorrectly uses Heading 1 here.
        section.Append(Box("二级标题排版：黑体，三号，段前0行，段后0行。", "section-note"));
        var path = Create("template.docx", P("摘要"), P("摘要内容。"), chapter, section, P("正文样本内容。"), P("【举例】", 0), P("毕业设计（论文）原创承诺书", 0));

        Assert.Equal(new[] { ("第 1 章 引言", 1), ("1.1 研究背景", 2) }, TemplatePlanDraftBuilder.ExtractOutline(path));
        Assert.True(OpenXmlDocumentInspector.TryInspect(path, out var map, out _));
        Assert.DoesNotContain(map!.Paragraphs, p => p.Text.Contains("排版"));
        Assert.Equal("1.1 研究背景", map.Paragraphs.Single(p => p.BodyElementIndex == 3).Text);

        var plan = TemplatePlanDraftBuilder.Build(path, path);
        var output = Path.Combine(directory, "output.docx");
        OpenXmlTemplateApplicator.Apply(path, path, plan, output);
        using var result = WordprocessingDocument.Open(output, false);
        var headingStyle = result.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().Single(s => s.StyleId == "Thesis_heading2");
        Assert.Equal("000000", headingStyle.StyleRunProperties!.GetFirstChild<Color>()!.Val!.Value);
        Assert.Equal(1, headingStyle.StyleParagraphProperties!.GetFirstChild<OutlineLevel>()!.Val!.Value);
    }

    [Fact]
    public void CoverDropsInstructionShapesAndKeepsIdentityTextBoxes()
    {
        var cover = P("毕业论文");
        cover.Append(Box("黑体，二号，段前0行，段后1行。", "guide"), Box("学生姓名：某同学", "identity"));
        var template = Create("cover.docx", cover, P("说明：请填写自己的论文内容。"), P("摘要"), P("摘要样本。"), P("第1章 绪论"), P("正文样本内容。"));
        var source = Create("source.docx", P("论文标题"), P("摘要"), P("新摘要。"), P("第1章 绪论"), P("新正文。"));
        var output = Path.Combine(directory, "cover-output.docx");
        OpenXmlTemplateApplicator.Apply(source, template, TemplatePlanDraftBuilder.Build(source, template), output);
        using var result = WordprocessingDocument.Open(output, false);
        var text = result.MainDocumentPart!.Document!.InnerText;
        Assert.Contains("学生姓名：某同学", text);
        Assert.DoesNotContain("段前", text);
        Assert.DoesNotContain("说明：", text);
        Assert.Contains("新正文。", text);
    }

    [Fact]
    public void PrintedSectionNumberTakesPrecedenceOverWrongHeadingStyleDuringImport()
    {
        var path = Create("wrong-styles.docx", P("第 1 章 引言", 0), P("1.1 研究背景", 0), P("原文保留。"), P("1.2 国内外研究现状", 0), P("第 2 章 相关技术", 0), P("2.1 技术概述", 0));
        Assert.True(OpenXmlDocumentInspector.TryInspect(path, out var map, out _));
        var (content, _) = ContentExtractBuilder.Build(map!, null, null, null, null);
        Assert.Equal(2, content.Chapters.Count);
        Assert.Equal(new[] { "1.1 研究背景", "1.2 国内外研究现状" }, content.Chapters[0].Sections.Select(s => s.Title));
        Assert.Equal("2.1 技术概述", content.Chapters[1].Sections.Single().Title);
    }

    [Fact]
    public void ExplicitFormatNotesReplaceManualIndentAndStaleAutomaticSamples()
    {
        var chapter = P("第 1 章 引言", 0);
        chapter.ParagraphProperties!.Indentation = new Indentation { Left = "3200" };
        chapter.Append(Box("一级标题排版：黑体，二号，居中，单倍行距，段前0行，段后1行。", "chapter-rule"));
        var section = P("1.1 研究背景", 0);
        section.Append(Box("二级标题排版：黑体，三号，居左，加粗，单倍行距，段前0行，段后0行。", "section-rule"));
        var path = Create("format-notes.docx", P("摘要"), P("摘要样本。"), chapter, section,
            P("这是一段足够长的正文样本，它用来识别论文正文的格式，避免把代码段或示例标签当成全篇正文的样式。"), P("【举例】", 1));
        var plan = TemplatePlanDraftBuilder.Build(path, path);
        var design = new TemplateDesign { Name = "旧的自动识别", Roles = [new() { Role = "heading2", Name = "二级标题", SampleIndex = 5 }] };
        TemplateDesigner.Apply(design, plan);
        Assert.Equal(3, plan.Roles["heading2"].TemplateParagraphIndex);
        var output = Path.Combine(directory, "format-output.docx");
        OpenXmlTemplateApplicator.Apply(path, path, plan, output);
        using var result = WordprocessingDocument.Open(output, false);
        var styles = result.MainDocumentPart!.StyleDefinitionsPart!.Styles!;
        var first = styles.Elements<Style>().Single(s => s.StyleId == "Thesis_heading1");
        Assert.Equal(JustificationValues.Center, first.StyleParagraphProperties!.GetFirstChild<Justification>()!.Val!.Value);
        Assert.Null(first.StyleParagraphProperties.GetFirstChild<Indentation>());
        Assert.Equal("240", first.StyleParagraphProperties.GetFirstChild<SpacingBetweenLines>()!.Line!.Value);
        Assert.Equal(100, first.StyleParagraphProperties.GetFirstChild<SpacingBetweenLines>()!.AfterLines!.Value);
        var second = styles.Elements<Style>().Single(s => s.StyleId == "Thesis_heading2");
        Assert.Equal("32", second.StyleRunProperties!.GetFirstChild<FontSize>()!.Val!.Value);
        Assert.Equal("000000", second.StyleRunProperties.GetFirstChild<Color>()!.Val!.Value);
        Assert.DoesNotContain(new DocumentFormat.OpenXml.Validation.OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019)
            .Validate(result), error => error.Part?.Uri.ToString() == "/word/styles.xml");

        design.Roles[0].Confirmed = true;
        TemplateDesigner.Apply(design, plan);
        Assert.Equal(5, plan.Roles["heading2"].TemplateParagraphIndex);
        Assert.False(plan.Roles["heading2"].ApplyTemplateInstructions);
    }

    [Theory]
    [InlineData("第 1 章  引言", "第1章 引言")]
    [InlineData("第    4    章  系统设计", "第4章 系统设计")]
    [InlineData("第一章绪论", "第一章 绪论")]
    public void SpacedChapterNumberIsNotPrefixedAgain(string title, string expected)
    {
        var output = Path.Combine(directory, "chapter.docx");
        ThesisDocumentGenerator.Generate(new ThesisContent { Title = "论文", Chapters = [new() { Title = title }] }, new TemplateProfile(), output, chaptersOnly: true);
        using var result = WordprocessingDocument.Open(output, false);
        Assert.Equal(expected, result.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First().InnerText);
    }

    [Fact]
    public void ChapterPageRuleAndCoverTitleApplyWithoutSeparatingAbstractTitle()
    {
        var title = P("论文题目：样例题目");
        title.ParagraphProperties!.SectionProperties = new SectionProperties();
        var chapter = P("第1章 引言", 0);
        chapter.Append(Box("每章标题需要另起一页。", "page-rule"));
        var template = Create("page-rule.docx", title, P("样例题目"), P("摘要"), P("摘要样本。"), chapter, P("正文样本。"), P("第2章 技术", 0));
        var sourceTitle = P("作者的题目");
        sourceTitle.ParagraphProperties!.ParagraphStyleId = new ParagraphStyleId { Val = "Title" };
        var source = Create("page-source.docx", sourceTitle, P("摘要"), P("新摘要。"), P("第1章 引言", 0), P("新正文。"), P("第2章 技术", 0));
        var plan = TemplatePlanDraftBuilder.Build(source, template);
        Assert.Equal("作者的题目", plan.TemplateTextReplacements["样例题目"]);
        Assert.False(plan.Blocks.Single(b => b.Type == "sourceRange" && b.Start == 1).PageBreakBefore);
        Assert.All(plan.Blocks.Where(b => b.Type == "sourceRange" && b.Role == "heading1"), block => Assert.True(block.PageBreakBefore));
    }

    [Fact]
    public void DraftContinuesRomanTocPagesAndSkipsExampleChapterSections()
    {
        var abstractBody = P("摘要样本。");
        abstractBody.ParagraphProperties!.SectionProperties = new SectionProperties(new PageNumberType { Format = NumberFormatValues.UpperRoman, Start = 1 });
        var toc = P("目录");
        toc.ParagraphProperties!.SectionProperties = new SectionProperties(new PageNumberType { Format = NumberFormatValues.UpperRoman, Start = 5 });
        var chapter = P("第1章 绪论", 0);
        var chapterBody = P("正文样本。");
        chapterBody.ParagraphProperties!.SectionProperties = new SectionProperties(new PageNumberType { Format = NumberFormatValues.Decimal, Start = 1 });
        var example = P("【举例】", 0);
        example.ParagraphProperties!.SectionProperties = new SectionProperties(new Columns { ColumnCount = 2 });
        var template = Create("sections.docx", P("摘要"), abstractBody, toc, chapter, chapterBody, example, P("第2章 技术", 0), P("第二章正文。"));
        var source = Create("section-source.docx", P("摘要"), P("新摘要。"), P("目录"), P("第1章 绪论", 0), P("新正文。"), P("第2章 技术", 0));
        var plan = TemplatePlanDraftBuilder.Build(source, template);
        Assert.True(plan.Blocks.Single(block => block.Type == "section" && block.SectionTemplateIndex == 1).ContinuePageNumbering);
        Assert.DoesNotContain(plan.Blocks, block => block.Type == "section" && block.SectionTemplateIndex == 3);
        Assert.Equal(4, plan.Blocks.Last().SectionTemplateIndex);
    }

    [Theory]
    [InlineData("Ⅱ", "upperRoman", 2, "II")]
    [InlineData("3", "decimal", 3, "3")]
    public void StaticPageFooterIsDetectedAndPromotedToPageFieldWithoutLosingLayout(string label, string format, int number, string cached)
    {
        var template = Create("static-roman-template.docx", P("摘要"), P("模板摘要内容。"),
            P("第1章 绪论", 0), P("这是一段足够长的正文模板内容，用于识别正文段落格式并验证页码转换。"));
        using (var document = WordprocessingDocument.Open(template, true))
        {
            var main = document.MainDocumentPart!;
            var footer = main.AddNewPart<FooterPart>();
            footer.Footer = new Footer(
                new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                    new Run(new RunProperties(new RunFonts { EastAsia = "宋体" }, new FontSize { Val = "20" }), new Text(label))),
                new Paragraph(new ParagraphProperties()), new Paragraph(new ParagraphProperties()));
            footer.Footer.Save();
            var section = main.Document!.Body!.GetFirstChild<SectionProperties>()!;
            section.AddChild(new PageNumberType { Start = 46 }, true);
            section.AddChild(new FooterReference { Id = main.GetIdOfPart(footer) }, true);
            main.Document.Save();
        }

        var plan = TemplatePlanDraftBuilder.Build(template, template);
        var pageSection = Assert.Single(plan.Blocks, block => block.Type == "section");
        Assert.Equal(format, pageSection.PageNumberFormat);
        Assert.Equal(number, pageSection.PageNumberStart);
        Assert.False(pageSection.ContinuePageNumbering);

        var output = Path.Combine(directory, "static-roman-output.docx");
        OpenXmlTemplateApplicator.Apply(template, template, plan, output);
        using var result = WordprocessingDocument.Open(output, false);
        var resultSection = result.MainDocumentPart!.Document!.Body!.GetFirstChild<SectionProperties>()!;
        Assert.Equal(format, resultSection.GetFirstChild<PageNumberType>()!.Format!.InnerText);
        Assert.Equal(number, resultSection.GetFirstChild<PageNumberType>()!.Start!.Value);
        var resultFooter = (FooterPart)result.MainDocumentPart.GetPartById(resultSection.Elements<FooterReference>().Single().Id!);
        Assert.Contains(resultFooter.Footer!.Descendants<FieldCode>(), field => field.Text.Contains("PAGE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(cached, resultFooter.Footer.Descendants<Text>().Single(text => !string.IsNullOrWhiteSpace(text.Text)).Text);
        Assert.Equal("20", resultFooter.Footer.Descendants<RunProperties>().First().GetFirstChild<FontSize>()?.Val?.Value);
        Assert.Equal(3, resultFooter.Footer.Elements<Paragraph>().Count());
    }

    [Fact]
    public void BackMatterInstructionDoesNotStealDeclarationHeadingFormat()
    {
        var source = Create("back-matter-source.docx", P("摘要"), P("摘要正文样本。"),
            P("第1章 绪论", 0), P("正文内容样本。"), P("致谢"),
            P("感谢导师和同学的帮助。"));
        var template = Create("back-matter-template.docx", P("摘要"), P("摘要正文模板。"),
            P("第1章 绪论", 0), P("正文内容模板，这是一段足够长的正文示例，用来识别正文样式。"),
            P("致谢"), P("说明：在致谢中填写对导师和相关人员的感谢。"), P("大连东软信息学院"),
            P("毕业设计（论文）原创承诺书"), P("本人承诺，文中的引用均已注明来源。"));

        var plan = TemplatePlanDraftBuilder.Build(source, template);

        Assert.Equal(3, plan.Roles["body"].TemplateParagraphIndex);
        Assert.Equal(plan.Roles["body"].TemplateParagraphIndex, plan.Roles["backMatter"].TemplateParagraphIndex);
        Assert.Equal(4, plan.Roles["backMatterHeading"].TemplateParagraphIndex);
        Assert.Contains(plan.PendingReviews, review => review.Contains("'backMatter'"));
    }

    [Fact]
    public void NumberingTransferPreservesCompatibilityDeclarations()
    {
        const string extensionNamespace = "http://schemas.microsoft.com/office/word/2016/wordml/cid";
        var item = P("列表正文。");
        item.ParagraphProperties!.NumberingProperties = new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 });
        var source = Create("list-source.docx", P("第1章 绪论"), item);
        using (var doc = WordprocessingDocument.Open(source, true))
        {
            var part = doc.MainDocumentPart!.AddNewPart<NumberingDefinitionsPart>();
            var instance = new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 };
            instance.SetAttribute(new OpenXmlAttribute("w16cid", "durableId", extensionNamespace, "123"));
            part.Numbering = new Numbering(
                new AbstractNum(new Level(new StartNumberingValue { Val = 1 }, new NumberingFormat { Val = NumberFormatValues.Decimal },
                    new LevelText { Val = "%1." }) { LevelIndex = 0 }) { AbstractNumberId = 0 }, instance);
            part.Numbering.AddNamespaceDeclaration("w16cid", extensionNamespace);
            part.Numbering.MCAttributes = new MarkupCompatibilityAttributes { Ignorable = "w16cid" };
        }
        var template = Create("list-template.docx", P("第1章 绪论"), P("正文样本。"));
        var output = Path.Combine(directory, "list-output.docx");
        var report = OpenXmlTemplateApplicator.Apply(source, template, TemplatePlanDraftBuilder.Build(source, template), output);
        TemplateStructureAudit.Check(output, report);
        Assert.True(report.StructurePassed, string.Join(Environment.NewLine, report.StructuralErrors));
        using var result = WordprocessingDocument.Open(output, false);
        var numbering = result.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;
        Assert.Contains("w16cid", numbering.MCAttributes!.Ignorable!.Value!);
        Assert.Equal("123", numbering.GetFirstChild<NumberingInstance>()!.GetAttribute("durableId", extensionNamespace).Value);
    }

    [Fact]
    public void BookmarkRepairPairsDuplicateIdsAndDropsOrphanMarkers()
    {
        var first = new Paragraph(new BookmarkStart { Id = "7", Name = "first" }, new Run(new Text("first")), new BookmarkEnd { Id = "7" });
        var second = new Paragraph(new BookmarkStart { Id = "7", Name = "second" }, new Run(new Text("second")), new BookmarkEnd { Id = "7" });
        var orphanStart = new Paragraph(new BookmarkStart { Id = "8", Name = "orphan" }, new Run(new Text("unmarked")));
        var orphanEnd = new Paragraph(new BookmarkEnd { Id = "9" });
        var path = Create("duplicate-bookmarks.docx", first, second, orphanStart, orphanEnd);

        Assert.True(OpenXmlCompatibilityRepair.NormalizeBookmarkIds(path));

        using var result = WordprocessingDocument.Open(path, false);
        var body = result.MainDocumentPart!.Document!.Body!;
        var starts = body.Descendants<BookmarkStart>().ToArray();
        var ends = body.Descendants<BookmarkEnd>().ToArray();
        Assert.Equal(2, starts.Length);
        Assert.Equal(2, ends.Length);
        Assert.Equal(starts.Select(start => start.Id!.Value).Order(), ends.Select(end => end.Id!.Value).Order());
        Assert.Equal(2, starts.Select(start => start.Id!.Value).Distinct().Count());
        Assert.Equal(new[] { "first", "second", "unmarked" }, body.Elements<Paragraph>().Select(p => p.InnerText).Where(text => text.Length > 0));
    }

    [Fact]
    public void CoverReadsUnstyledTitleFromUniqueCenterAlignedParagraph()
    {
        var title = P("面向高校的校园服务系统设计与实现");
        title.ParagraphProperties!.Justification = new Justification { Val = JustificationValues.Center };
        var label = P("专    业：计算机科学与技术");
        label.ParagraphProperties!.Justification = new Justification { Val = JustificationValues.Center };
        var source = Create("unstyled-cover.docx", label, title, P("摘要"), P("摘要正文。"), P("第1章 绪论"), P("正文。"));
        var template = Create("labeled-cover.docx", P("论文题目：样例题目"), P("摘要"), P("摘要样本。"), P("第1章 绪论"), P("正文样本。"));
        var plan = TemplatePlanDraftBuilder.Build(source, template);

        Assert.Equal(title.InnerText, plan.TemplateTextReplacements["样例题目"]);
    }

    [Theory]
    [InlineData("TOC1", "TOC2")]
    [InlineData("11", "12")]
    public void ExistingTocRowsAreSkippedBeforeBodyHeadings(string chapterStyle, string sectionStyle)
    {
        var tocChapter = P("第1章 绪论 3");
        tocChapter.ParagraphProperties!.ParagraphStyleId = new ParagraphStyleId { Val = chapterStyle };
        var tocSection = P("1.1 研究背景 3");
        tocSection.ParagraphProperties!.ParagraphStyleId = new ParagraphStyleId { Val = sectionStyle };
        var source = Create("source-toc.docx", P("摘要"), P("摘要正文。"), P("目录"), tocChapter, tocSection,
            P("第1章 绪论", 0), P("正文样本。"));
        using (var document = WordprocessingDocument.Open(source, true))
        {
            var part = document.MainDocumentPart!.AddNewPart<StyleDefinitionsPart>();
            part.Styles = new Styles(
                new Style(new StyleName { Val = "toc 1" }) { Type = StyleValues.Paragraph, StyleId = chapterStyle },
                new Style(new StyleName { Val = "toc 2" }) { Type = StyleValues.Paragraph, StyleId = sectionStyle });
        }
        var template = Create("toc-template.docx", P("摘要"), P("摘要正文。"), P("Abstract"), P("英文摘要。"),
            P("目录"), P("第1章 绪论", 0), P("正文样本。"), P("1.1 研究背景", 0));

        var plan = TemplatePlanDraftBuilder.Build(source, template);

        Assert.Single(plan.Blocks, block => block.Type == "toc");
        Assert.Contains(plan.SkippedSourceRanges, range => range.Start == 3);
        Assert.Contains(plan.SkippedSourceRanges, range => range.Start == 4);
        Assert.DoesNotContain(plan.Blocks, block => block.Type == "sourceRange" && block.Start is 3 or 4);
        Assert.Contains(plan.Blocks, block => block.Type == "sourceRange" && block.Role == "heading1" && block.Start == 5);
    }

    [Fact]
    public void OutlineRepairRestoresSectionsAndRetainsObjectsUnderTheirThirdLevelHeading()
    {
        var template = Create("outline.docx", P("第 1 章 引言", 0), P("1.1 背景", 0), P("正文样本。"), P("1.2 方法", 0), P("1.2.1 实现", 1), P("第 2 章 技术", 0), P("2.1 概述", 0));
        var original = new ThesisContent { Chapters = [new() { Title = "第 1 章 引言", Blocks = [new() { Text = "章正文" }] },
            new() { Title = "1.1 背景二级标题排版：黑体，三号。", Blocks = [new() { Type = "image", Path = "figure.png" }] },
            new() { Title = "1.2 方法", Sections = [new() { Title = "1.2.1 实现", Blocks = [new() { Type = "equation", Latex = "x^2" }] }] },
            new() { Title = "第 2 章 技术" }, new() { Title = "2.1 概述", Blocks = [new() { Type = "table", Table = new() { Headers = ["A"] } }] }] };
        var before = ThesisJson.Serialize(original);
        var fixedContent = TemplateOutlineBuilder.Rebuild(original, template);
        Assert.Equal(before, ThesisJson.Serialize(original));
        Assert.Equal(2, fixedContent.Chapters.Count);
        Assert.Equal("image", fixedContent.Chapters[0].Sections[0].Blocks.Single().Type);
        var blocks = fixedContent.Chapters[0].Sections[1].Blocks;
        Assert.Equal("1.2.1 实现", blocks[0].Text);
        Assert.Equal("x^2", blocks[1].Latex);
        Assert.Equal("table", fixedContent.Chapters[1].Sections.Single().Blocks.Single().Type);
        var again = TemplateOutlineBuilder.Rebuild(fixedContent, template);
        Assert.Equal(2, again.Chapters[0].Sections[1].Blocks.Count);
    }

    [Fact]
    public void OutlineRepairStopsBeforeDroppingUnmatchedAuthoredContent()
    {
        var template = Create("unmatched.docx", P("第1章 引言"), P("正文样本。"));
        var content = new ThesisContent { Chapters = [new() { Title = "自定义章节", Blocks = [new() { Text = "作者自己的正文" }] }] };
        var before = ThesisJson.Serialize(content);
        Assert.Contains("自定义章节", Assert.Throws<InvalidDataException>(() => TemplateOutlineBuilder.Rebuild(content, template)).Message);
        Assert.Equal(before, ThesisJson.Serialize(content));
    }
}
