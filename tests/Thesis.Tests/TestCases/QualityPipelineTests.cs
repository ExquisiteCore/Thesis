using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Application;
using Thesis.Cli;
using Thesis.OpenXml;
using Thesis.Schema;
using Xunit;

public sealed class QualityPipelineTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "thesis-quality-" + Guid.NewGuid().ToString("N"));
    public QualityPipelineTests() => Directory.CreateDirectory(directory);
    private string PathFor(string name) => Path.Combine(directory, name);
    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string Create(string name, params OpenXmlElement[] blocks)
    {
        var path = PathFor(name);
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(blocks.Append(new SectionProperties(new PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440 }))));
        return path;
    }

    private static Paragraph P(string text) => new(new Run(new Text(text)));
    private static TemplateApplicationPlan Plan(int count) => new()
    {
        Roles = new[] { "body", "table", "image" }.ToDictionary(r => r, _ => new TemplateRoleFormat()),
        Blocks = [new() { Type = "sourceRange", Start = 0, End = count, Role = "body" }, new() { Type = "section", FinalSection = true }]
    };

    [Fact]
    public void CrossBlockBookmarksSplitReferenceFieldsAndLinksRemainConnected()
    {
        var source = Create("source.docx", new Paragraph(new BookmarkStart { Id = "1", Name = "original" }, new Run(new Text("Target"))),
            new Paragraph(new Run(new Text("End")), new BookmarkEnd { Id = "1" }),
            new Paragraph(new Hyperlink(new Run(new Text("Jump"))) { Anchor = "original" },
                new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }), new Run(new FieldCode(" PAGE")), new Run(new FieldCode("REF original ")),
                new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }), new Run(new Text("1")), new Run(new FieldChar { FieldCharType = FieldCharValues.End })));
        var template = Create("template.docx", P("Template"));
        var output = PathFor("out.docx");
        OpenXmlTemplateApplicator.Apply(source, template, Plan(3), output);
        Assert.Empty(DocumentIntegrityAudit.Check(output));
        using var result = WordprocessingDocument.Open(output, false);
        var root = result.MainDocumentPart!.Document!;
        var name = root.Descendants<BookmarkStart>().Single().Name!.Value;
        Assert.Equal(name, root.Descendants<Hyperlink>().Single().Anchor!.Value);
        Assert.Contains(name!, string.Concat(root.Descendants<FieldCode>().Select(f => f.Text)));
    }

    [Fact]
    public void MissingReferencedTargetFailsStructureGate()
    {
        var source = Create("source.docx", new Paragraph(new Hyperlink(new Run(new Text("Jump"))) { Anchor = "missing" }));
        var output = PathFor("out.docx");
        var report = OpenXmlTemplateApplicator.Apply(source, source, Plan(1), output);
        TemplateStructureAudit.Check(output, report);
        Assert.False(report.StructurePassed);
        Assert.Contains(report.StructuralErrors, e => e.Contains("target is missing"));
    }

    [Fact]
    public void FieldInstructionsSplitAcrossBodyBlocksKeepDocumentContext()
    {
        var source = Create("source.docx", new Paragraph(new BookmarkStart { Id = "1", Name = "target" }, new Run(new Text("Target")), new BookmarkEnd { Id = "1" }),
            new Paragraph(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }), new Run(new FieldCode(" PAGE"))),
            new Paragraph(new Run(new FieldCode("REF target ")), new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }), new Run(new Text("1")), new Run(new FieldChar { FieldCharType = FieldCharValues.End })));
        var output = PathFor("out.docx");
        OpenXmlTemplateApplicator.Apply(source, source, Plan(3), output);
        Assert.Empty(DocumentIntegrityAudit.Check(output));
        using var result = WordprocessingDocument.Open(output, false);
        var name = result.MainDocumentPart!.Document!.Descendants<BookmarkStart>().Single().Name!.Value!;
        Assert.Contains(name, string.Concat(result.MainDocumentPart.Document.Descendants<FieldCode>().Select(f => f.Text)));
    }

    [Fact]
    public void InternalHyperlinkFieldsAreRemappedAndValidated()
    {
        var source = Create("source.docx", new Paragraph(new BookmarkStart { Id = "1", Name = "target" }, new Run(new Text("Target")), new BookmarkEnd { Id = "1" }),
            new Paragraph(new SimpleField(new Run(new Text("Jump"))) { Instruction = " HYPERLINK \\l \"target\" " }));
        var output = PathFor("out.docx");
        OpenXmlTemplateApplicator.Apply(source, source, Plan(2), output);
        using (var result = WordprocessingDocument.Open(output, true))
        {
            var root = result.MainDocumentPart!.Document!;
            var name = root.Descendants<BookmarkStart>().Single().Name!.Value!;
            Assert.Contains("\"" + name + "\"", root.Descendants<SimpleField>().Single().Instruction!.Value!);
            root.Descendants<SimpleField>().Single().Instruction = " HYPERLINK \\l \"missing\" ";
        }
        Assert.Contains(DocumentIntegrityAudit.Check(output), e => e.Contains("missing"));
    }

    [Fact]
    public void AuthoredTextIncludesEquationContent()
    {
        var source = Create("source.docx", new Paragraph(new DocumentFormat.OpenXml.Math.OfficeMath(
            new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Text("x=42")))));
        Assert.Equal("x=42", DocumentIntegrityAudit.AuthoredText(source));
    }

    [Fact]
    public void RoleFormattingPreservesInlineSemanticsButReplacesFonts()
    {
        var source = Create("source.docx", new Paragraph(new Run(new RunProperties(new RunFonts { Ascii = "Arial" }, new Bold(), new Italic(),
            new Underline { Val = UnderlineValues.Single }, new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }), new Text("Emphasis"))));
        var output = PathFor("out.docx");
        OpenXmlTemplateApplicator.Apply(source, source, Plan(1), output);
        using var result = WordprocessingDocument.Open(output, false);
        var properties = result.MainDocumentPart!.Document!.Descendants<Run>().Single().RunProperties!;
        Assert.NotNull(properties.Bold);
        Assert.NotNull(properties.Italic);
        Assert.NotNull(properties.Underline);
        Assert.Equal(VerticalPositionValues.Superscript, properties.VerticalTextAlignment!.Val!.Value);
        Assert.Equal("Times New Roman", properties.RunFonts!.Ascii!.Value);
        Assert.Empty(DocumentIntegrityAudit.Check(output));
    }

    [Fact]
    public void NumberingDefinitionsAndInheritedListReferencesAreMerged()
    {
        var source = Create("source.docx", new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "list" }), new Run(new Text("List item"))));
        using (var doc = WordprocessingDocument.Open(source, true))
        {
            var main = doc.MainDocumentPart!;
            main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles(new Style(new StyleName { Val = "list" },
                new StyleParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }))) { Type = StyleValues.Paragraph, StyleId = "list" });
            main.AddNewPart<NumberingDefinitionsPart>().Numbering = new Numbering(new AbstractNum(new Level(new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1." }) { LevelIndex = 0 }) { AbstractNumberId = 0 },
                new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 });
        }
        var template = Create("template.docx", P("Template"));
        var output = PathFor("out.docx");
        OpenXmlTemplateApplicator.Apply(source, template, Plan(1), output);
        using var result = WordprocessingDocument.Open(output, false);
        var id = result.MainDocumentPart!.Document!.Descendants<NumberingId>().Single().Val!.Value;
        Assert.Contains(result.MainDocumentPart.NumberingDefinitionsPart!.Numbering!.Elements<NumberingInstance>(), n => n.NumberID!.Value == id);
        Assert.Empty(DocumentIntegrityAudit.Check(output));
    }

    [Fact]
    public void TemplateSamplesResolveInheritanceAndPreserveColumns()
    {
        var template = Create("template.docx", new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "custom" }), new Run(new Text("Body sample"))));
        using (var doc = WordprocessingDocument.Open(template, true))
        {
            var main = doc.MainDocumentPart!;
            main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles(new Style(new StyleName { Val = "base" }, new StyleRunProperties(new RunFonts { EastAsia = "黑体" }, new FontSize { Val = "28" }))
                { Type = StyleValues.Paragraph, StyleId = "base" }, new Style(new StyleName { Val = "custom" }, new BasedOn { Val = "base" }, new StyleParagraphProperties(new SpacingBetweenLines { Line = "360", LineRule = LineSpacingRuleValues.Auto }))
                { Type = StyleValues.Paragraph, StyleId = "custom" });
            main.Document!.Body!.Elements<SectionProperties>().Single().Append(new Columns { ColumnCount = 2, Space = "720" }, new SectionType { Val = SectionMarkValues.OddPage });
        }
        var source = Create("source.docx", P("New body"));
        var plan = Plan(1);
        plan.Roles["body"].TemplateParagraphIndex = 0;
        var output = PathFor("out.docx");
        OpenXmlTemplateApplicator.Apply(source, template, plan, output);
        using var result = WordprocessingDocument.Open(output, false);
        var style = result.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().Single(s => s.StyleId == "Thesis_body");
        Assert.Equal("黑体", style.StyleRunProperties!.RunFonts!.EastAsia!.Value);
        Assert.Equal("28", style.StyleRunProperties.FontSize!.Val!.Value);
        Assert.Equal((short)2, result.MainDocumentPart.Document!.Descendants<Columns>().Single().ColumnCount!.Value);
        Assert.Equal(SectionMarkValues.OddPage, result.MainDocumentPart.Document.Descendants<SectionType>().Single().Val!.Value);
    }

    [Fact]
    public void RequirementsReportCountsAndMissingAnchors()
    {
        var source = Create("source.docx", P("摘要"), P("关键词：甲；乙；丙"), P("结论"), P("一二三四五六七八九十"), P("参考文献"), P("[1] Author. Title[J]. Journal."));
        var results = ContentRequirementAudit.Check(source,
        [new() { Id = "keywords", Kind = "keywords", StartAnchor = "摘要", EndAnchor = "结论", Minimum = 3, Maximum = 5, Evidence = "Template keyword instruction" },
         new() { Id = "conclusion", Kind = "sectionCharacters", StartAnchor = "结论", EndAnchor = "参考文献", Minimum = 11, Evidence = "Template conclusion instruction" },
         new() { Id = "journal", Kind = "journalReferences", StartAnchor = "参考文献", Minimum = 1, Evidence = "Template reference instruction" },
         new() { Id = "missing", Kind = "sectionCharacters", StartAnchor = "Missing", Minimum = 1, Evidence = "Template instruction" }]);
        Assert.True(results[0].Passed);
        Assert.Equal(3, results[0].Actual);
        Assert.False(results[1].Passed);
        Assert.Equal(10, results[1].Actual);
        Assert.True(results[2].Passed);
        Assert.False(results[3].Passed);
    }

    [Fact]
    public void ComposeRequiresDeliveryGatesAndPreservesExistingOutput()
    {
        var source = Create("source.docx", P("Body"));
        var plan = Plan(1);
        plan.SourceSha256 = plan.TemplateSha256 = TemplatePlanPreflight.Fingerprint(source);
        var planPath = PathFor("plan.json");
        File.WriteAllText(planPath, ThesisJson.Serialize(plan));
        var output = PathFor("out.docx"); File.WriteAllText(output, "existing");
        using var writer = new StringWriter();
        var code = ThesisCli.Run(["compose", "--doc", source, "--template", source, "--plan", planPath, "--out", output, "--workdir", PathFor("work")], writer, TextWriter.Null);
        Assert.Equal(1, code);
        Assert.Equal("existing", File.ReadAllText(output));
        Assert.Single(Directory.GetFiles(PathFor("work"), "candidate.docx", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(directory, "*.thesis-lock"));
    }

    [Theory]
    [InlineData("{\"blockz\":[]}")]
    [InlineData("{\"engine\":\"wps\",\"pythnoPath\":\"python.exe\"}")]
    public void UnknownConfigurationFieldsAreRejected(string json) => Assert.Throws<System.Text.Json.JsonException>(() => ThesisJson.DeserializeStrict<LayoutRenderConfig>(json));

    [Fact]
    public void ReadOnlyTemplateDoesNotMakeOutputReadOnly()
    {
        var source = Create("source.docx", P("Body"));
        var template = Create("template.docx", P("Template"));
        File.SetAttributes(template, FileAttributes.ReadOnly);
        try
        {
            var output = PathFor("out.docx");
            OpenXmlTemplateApplicator.Apply(source, template, Plan(1), output);
            Assert.False(File.GetAttributes(output).HasFlag(FileAttributes.ReadOnly));
        }
        finally { File.SetAttributes(template, FileAttributes.Normal); }
    }

    [Fact]
    public void PendingHumanChecksPreventReadyEvenAfterAutomatedChecksPass()
    {
        var report = new TemplateApplicationReport { AssemblyPassed = true, StructurePassed = true, LayoutExecuted = true, LayoutPassed = true,
            Preflight = new() { FingerprintsPinned = true }, PendingReviews = ["Review cover"] };
        Assert.True(report.AutomatedChecksPassed);
        Assert.False(report.Ready);
    }

    [Fact]
    public void DeliveryReviewIsBoundToExactCandidateAndEveryPendingCheck()
    {
        var source = Create("source.docx", P("Body"));
        var pdf = PathFor("candidate.pdf"); File.WriteAllText(pdf, "%PDF-fixture");
        var reportPath = PathFor("report.json");
        var report = new TemplateApplicationReport { AssemblyPassed = true, StructurePassed = true, LayoutExecuted = true, LayoutPassed = true,
            Preflight = new() { FingerprintsPinned = true }, PendingReviews = ["Cover", "Tables"] };
        File.WriteAllText(reportPath, ThesisJson.Serialize(report));
        var review = new DeliveryReview { DocumentSha256 = TemplatePlanPreflight.Fingerprint(source), PdfSha256 = TemplatePlanPreflight.Fingerprint(pdf),
            ReportSha256 = TemplatePlanPreflight.Fingerprint(reportPath), Reviewer = "Test reviewer", Notes = "Verified exact artifacts", CompletedChecks = ["Cover"] };
        Assert.Throws<InvalidDataException>(() => DeliveryReviewService.Approve(source, pdf, reportPath, review, PathFor("final.docx"), PathFor("final.pdf"), PathFor("final.json")));
        review.CompletedChecks.Add("Tables");
        DeliveryReviewService.Approve(source, pdf, reportPath, review, PathFor("final.docx"), PathFor("final.pdf"), PathFor("final.json"));
        var delivered = ThesisJson.Deserialize<TemplateApplicationReport>(File.ReadAllText(PathFor("final.json")));
        Assert.True(delivered.Ready);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(PathFor("final.docx")));
        File.AppendAllText(pdf, "changed");
        Assert.Throws<InvalidDataException>(() => DeliveryReviewService.Approve(source, pdf, reportPath, review, PathFor("other.docx"), PathFor("other.pdf"), PathFor("other.json")));
    }

    [Fact]
    public void StructuredTextEntersTheSameTemplateService()
    {
        var template = Create("template.docx", P("摘要"), P("摘要样本"), P("第1章 绪论"), P("这是用于学习正文格式的模板内容。"));
        var contentPath = PathFor("content.json");
        File.WriteAllText(contentPath, ThesisJson.Serialize(new ThesisContent { Title = "Title", Chapters = [new() { Title = "第1章 绪论", Paragraphs = ["New authored body."] }] }));
        var output = PathFor("out.docx");
        using var writer = new StringWriter();
        var code = ThesisCli.Run(["compose", "--content", contentPath, "--template", template, "--out", output, "--workdir", PathFor("work"), "--require-ready", "false"], writer, TextWriter.Null);
        Assert.True(code == 0, writer.ToString());
        Assert.Contains("Newauthoredbody.", DocumentIntegrityAudit.AuthoredText(output));
        Assert.Empty(DocumentIntegrityAudit.Check(output));
    }

    [Fact]
    public void DraftRetainsCoverSectionsAndSeparatesLanguageHeadingSamples()
    {
        var source = Create("source.docx", P("Cover"), P("Declaration"), P("摘要"), P("中文摘要正文"), P("Abstract"), P("English abstract body."), P("第1章 绪论"), P("正文样本内容。"));
        using (var doc = WordprocessingDocument.Open(source, true))
        {
            var paragraphs = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToArray();
            paragraphs[0].ParagraphProperties = new ParagraphProperties(new SectionProperties(new SectionType { Val = SectionMarkValues.NextPage }));
            paragraphs[2].ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center });
            paragraphs[4].ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Left });
        }
        var plan = TemplatePlanDraftBuilder.Build(source, source);
        Assert.Equal(2, plan.Roles["abstractHeading"].TemplateParagraphIndex);
        Assert.Equal(4, plan.Roles["abstractEnglishHeading"].TemplateParagraphIndex);
        Assert.True(plan.Blocks.Single(b => b.Type == "sourceRange" && b.Start == 2).PageBreakBefore);
        Assert.Contains(plan.Blocks, b => b.Type == "section" && b.SectionTemplateIndex == 0 && !b.FinalSection);
        var output = PathFor("out.docx");
        OpenXmlTemplateApplicator.Apply(source, source, plan, output);
        using var result = WordprocessingDocument.Open(output, false);
        Assert.Equal(2, result.MainDocumentPart!.Document!.Descendants<SectionProperties>().Count());
    }

    [Fact]
    public void DestinationLeasesPreventSharedPdfWritesAndReleasePartialReservations()
    {
        var pdf = PathFor("shared.pdf");
        var output = PathFor("a.docx");
        using (Thesis.Core.OutputPathLease.Acquire([pdf]))
        {
            Assert.Throws<IOException>(() => Thesis.Core.OutputPathLease.Acquire([output, pdf]));
            using var retry = Thesis.Core.OutputPathLease.Acquire([output]);
        }
        Assert.Empty(Directory.GetFiles(directory, "*.thesis-lock"));
    }

    [Fact]
    public void DraftRecognizesChapterNumberingWithoutPrintedPrefix()
    {
        var source = Create("source.docx", P("摘要"), P("摘要正文"),
            new Paragraph(new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 })), new Run(new Text("绪论"))), P("正文样本内容。"));
        using (var doc = WordprocessingDocument.Open(source, true))
            doc.MainDocumentPart!.AddNewPart<NumberingDefinitionsPart>().Numbering = new Numbering(
                new AbstractNum(new Level(new StartNumberingValue { Val = 1 }, new NumberingFormat { Val = NumberFormatValues.ChineseCounting },
                    new LevelText { Val = "第%1章" }) { LevelIndex = 0 }) { AbstractNumberId = 0 }, new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 });
        var plan = TemplatePlanDraftBuilder.Build(source, source);
        Assert.Equal("heading1", plan.Blocks.Single(b => b.Type == "sourceRange" && b.Start == 2).Role);
        var output = PathFor("out.docx");
        var report = OpenXmlTemplateApplicator.Apply(source, source, plan, output);
        Assert.Contains("绪论", report.Headings);
    }

    [Fact]
    public void EnglishTitleAndAbstractStayInOneNewZone()
    {
        var source = Create("source.docx", P("摘要"), P("中文摘要正文"), P("Title  A new thesis"), P("Abstract"),
            P("English abstract body."), P("第1章 绪论"), P("正文样本内容。"));
        var plan = TemplatePlanDraftBuilder.Build(source, source);
        var title = plan.Blocks.Single(b => b.Type == "sourceRange" && b.Start == 2);
        var heading = plan.Blocks.Single(b => b.Type == "sourceRange" && b.Start == 3);
        Assert.Equal("englishTitle", title.Role);
        Assert.True(title.PageBreakBefore);
        Assert.False(heading.PageBreakBefore);
    }

    [Fact]
    public void OfficePropertyOrderRepairRetainsRepeatedSettingsAndIsIdempotent()
    {
        var source = Create("source.docx", P("Body"));
        using (var doc = WordprocessingDocument.Open(source, true))
            doc.MainDocumentPart!.AddNewPart<DocumentSettingsPart>().Settings = new Settings(new UpdateFieldsOnOpen { Val = true },
                new Zoom { Percent = "100" }, new AttachedSchema { Val = "first" }, new AttachedSchema { Val = "second" });
        Assert.True(OpenXmlCompatibilityRepair.NormalizeStyleOrder(source));
        Assert.False(OpenXmlCompatibilityRepair.NormalizeStyleOrder(source));
        using var result = WordprocessingDocument.Open(source, false);
        Assert.Equal(new[] { "first", "second" }, result.MainDocumentPart!.DocumentSettingsPart!.Settings!.Elements<AttachedSchema>().Select(s => s.Val!.Value));
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(result));
    }

    [Fact]
    public void TemplateTextBoxesAreRetainedAndTheirIdentityTextCanBeReplaced()
    {
        var cover = new Paragraph(new AlternateContent(new AlternateContentChoice(new Run(new Picture(
            new DocumentFormat.OpenXml.Vml.Shape(new DocumentFormat.OpenXml.Vml.TextBox(new TextBoxContent(P("Old author")))) { Id = "cover" }))) { Requires = "v" },
            new AlternateContentFallback(new Run(new Text("Fallback author")))));
        cover.AddNamespaceDeclaration("v", "urn:schemas-microsoft-com:vml");
        var template = Create("template.docx", cover, P("Template body"));
        var source = Create("source.docx", P("New body"));
        var plan = Plan(1);
        plan.Blocks.Insert(0, new() { Type = "templateRange", Start = 0, End = 1 });
        plan.TemplateTextReplacements["Old author"] = "New author";
        var output = PathFor("out.docx");
        OpenXmlTemplateApplicator.Apply(source, template, plan, output);
        using var result = WordprocessingDocument.Open(output, false);
        Assert.Equal("New author", result.MainDocumentPart!.Document!.Descendants<TextBoxContent>().Single().InnerText);
        Assert.Contains("Fallback author", result.MainDocumentPart.Document.InnerText);
    }

    [Fact]
    public void TemplateAssemblyRepairsDuplicateVmlShapeIdsWithoutChangingTemplate()
    {
        var duplicateId = "_x0000_s1026";
        var first = new Paragraph(new Run(new Picture(new DocumentFormat.OpenXml.Vml.Shape { Id = duplicateId })));
        var second = new Paragraph(new Run(new Picture(new DocumentFormat.OpenXml.Vml.Shape { Id = duplicateId })));
        var template = Create("template-with-duplicate-vml.docx", first, second, P("Template body"));
        var source = Create("vml-source.docx", P("Body"));
        var plan = Plan(1);
        plan.Blocks.Insert(0, new() { Type = "templateRange", Start = 0, End = 2 });
        var originalIds = File.ReadAllBytes(template);
        var output = PathFor("vml-output.docx");

        OpenXmlTemplateApplicator.Apply(source, template, plan, output);

        using var result = WordprocessingDocument.Open(output, false);
        var ids = result.MainDocumentPart!.Document!.Descendants()
            .Where(element => element.LocalName == "shape" && element.NamespaceUri == "urn:schemas-microsoft-com:vml")
            .Select(element => element.GetAttributes().First(attribute => attribute.LocalName == "id" && attribute.NamespaceUri.Length == 0).Value)
            .ToArray();
        Assert.Equal(2, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(originalIds, File.ReadAllBytes(template));
        Assert.Empty(DocumentIntegrityAudit.Check(output));
        var audit = new TemplateApplicationReport();
        TemplateStructureAudit.Check(output, audit);
        Assert.True(audit.StructurePassed, string.Join("; ", audit.StructuralErrors));
    }

    [Fact]
    public void HeaderStyleReferencesFollowMigratedHeadingStyles()
    {
        var source = Create("source.docx", P("第1章 绪论"), P("正文"));
        using (var doc = WordprocessingDocument.Open(source, true))
        {
            var header = doc.MainDocumentPart!.AddNewPart<HeaderPart>();
            header.Header = new Header(new Paragraph(new SimpleField(new Run(new Text("Old heading"))) { Instruction = " STYLEREF 1 " }));
            doc.MainDocumentPart.Document!.Body!.Elements<SectionProperties>().Single().PrependChild(new HeaderReference { Type = HeaderFooterValues.Default, Id = doc.MainDocumentPart.GetIdOfPart(header) });
        }
        var plan = Plan(2);
        plan.Roles["heading1"] = new() { OutlineLevel = 0 };
        plan.Blocks[0].Role = "heading1";
        var output = PathFor("out.docx");
        OpenXmlTemplateApplicator.Apply(source, source, plan, output);
        using var result = WordprocessingDocument.Open(output, false);
        Assert.Contains("\"Thesis_heading1\"", result.MainDocumentPart!.HeaderParts.Single().Header!.Descendants<SimpleField>().Single().Instruction!.Value!);
    }
}
