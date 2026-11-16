using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

internal static partial class Program
{
    static void OutputTransactionKeepsExistingFileWhenStagingIsIncomplete()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "output.bin");
        File.WriteAllText(output, "old");
        using (var transaction = new FileOutputTransaction())
        {
            _ = transaction.Stage(output);
            try { transaction.Commit(); throw new UnreachableException("Expected missing staged output failure."); }
            catch (FileNotFoundException) { }
        }
        AssertEqual("old", File.ReadAllText(output));
    }

    static void TemplatePreflightRejectsChangedFingerprint()
    {
        using var temp = new TempDirectory();
        var template = Path.Combine(temp.Path, "template.docx");
        var source = Path.Combine(temp.Path, "source.docx");
        WriteFixtureDocx(template);
        WriteFixtureDocx(source);
        var plan = NativeApplicationPlan();
        plan.SourceSha256 = TemplatePlanPreflight.Fingerprint(source);
        plan.TemplateSha256 = TemplatePlanPreflight.Fingerprint(template);
        File.AppendAllText(source, "changed");
        try { _ = TemplatePlanPreflight.Check(source, template, plan); throw new UnreachableException("Expected fingerprint mismatch."); }
        catch (InvalidDataException ex) { AssertContains(ex.Message, "fingerprint changed"); }
    }

    static void OrderedContentRoundtripPreservesMixedBlocksAndRelativeAssets()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "source.docx");
        var contentPath = Path.Combine(temp.Path, "content.json");
        var rulesPath = Path.Combine(temp.Path, "rules.json");
        var output = Path.Combine(temp.Path, "roundtrip.docx");
        WriteSimpleDocx(source, """
            <w:p><w:r><w:t>论文题目</w:t></w:r></w:p>
            <w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:bookmarkStart w:id="7" w:name="old_heading"/><w:r><w:t>before</w:t></w:r><w:r><w:rPr><w:vertAlign w:val="superscript"/></w:rPr><w:t>[1]</w:t></w:r><w:bookmarkEnd w:id="7"/></w:p>
            <w:tbl><w:tblGrid><w:gridCol w:w="1000"/><w:gridCol w:w="1000"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:gridSpan w:val="2"/></w:tcPr><w:p><w:r><w:t>merged</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            <w:p><w:r><w:t>middle</w:t></w:r></w:p>
            <w:p><w:r><w:t>1.1 研究背景</w:t></w:r></w:p>
            <w:p><w:r><w:t>after</w:t></w:r></w:p>
            """);
        using (var doc = WordprocessingDocument.Open(source, true))
        {
            var main = doc.MainDocumentPart!;
            var image = main.AddImagePart(ImagePartType.Png);
            using var bytes = new MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
            image.FeedData(bytes);
            var picture = new Paragraph($$"""
                <w:p xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
                  <w:r><w:drawing><wp:inline><wp:extent cx="914400" cy="914400"/><wp:docPr id="1" name="pixel"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="pixel"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="{{main.GetIdOfPart(image)}}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>
                </w:p>
                """);
            var middle = main.Document!.Body!.Elements<Paragraph>().Single(p => p.InnerText == "middle");
            middle.InsertAfterSelf(picture);
        }
        var original = File.ReadAllBytes(source);
        AssertEqual(0, RunCli(["content", "extract", "--doc", source, "--out", contentPath]).ExitCode);
        var extracted = ThesisJson.Deserialize<ThesisContent>(File.ReadAllText(contentPath));
        AssertEqual(true, extracted.Chapters[0].OrderedContentIncludesSections);
        AssertEqual(false, Path.IsPathRooted(extracted.Chapters[0].Blocks[0].SourcePath!));
        File.WriteAllText(rulesPath, ThesisJson.Serialize(new TemplateProfile()));
        AssertEqual(0, RunCli(["generate", "--content", contentPath, "--rules", rulesPath, "--out", output]).ExitCode);
        using var result = WordprocessingDocument.Open(output, false);
        var body = result.MainDocumentPart!.Document!.Body!;
        var blocks = body.ChildElements.ToList();
        var before = blocks.FindIndex(e => e.InnerText == "before[1]");
        AssertEqual(true, blocks[before + 1] is Table);
        AssertEqual("middle", blocks[before + 2].InnerText);
        AssertEqual(1, blocks[before + 3].Descendants<Drawing>().Count());
        AssertEqual("1.1 研究背景", blocks[before + 4].InnerText);
        AssertEqual("after", blocks[before + 5].InnerText);
        AssertEqual(1, body.Elements<Paragraph>().Count(p => p.InnerText == "1.1 研究背景"));
        AssertEqual(2, body.Descendants<GridSpan>().Single().Val!.Value);
        AssertEqual(VerticalPositionValues.Superscript, body.Descendants<VerticalTextAlignment>().Single().Val!.Value);
        AssertEqual(1, result.MainDocumentPart.ImageParts.Count());
        AssertEqual(1, body.Descendants<BookmarkStart>().Count());
        AssertEqual(false, body.Descendants<BookmarkStart>().Single().Name!.Value == "old_heading");
        AssertEqual(body.Descendants<BookmarkStart>().Single().Id!.Value, body.Descendants<BookmarkEnd>().Single().Id!.Value);
        AssertBytesEqual(original, File.ReadAllBytes(source));
        var asset = Path.GetFullPath(extracted.Chapters[0].Blocks[0].SourcePath!, temp.Path);
        File.AppendAllText(asset, "changed");
        AssertEqual(1, RunCli(["generate", "--content", contentPath, "--rules", rulesPath, "--out", Path.Combine(temp.Path, "changed.docx")]).ExitCode);
    }

    static void LegacyChapterBlocksRetainSeparatelySuppliedSections()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "legacy.docx");
        var content = new ThesisContent { Title = "title", Chapters = [new() { Title = "chapter", Blocks = [new() { Text = "first" }], Sections = [new() { Title = "section", Paragraphs = ["second"] }] }] };
        ThesisDocumentGenerator.Generate(content, new TemplateProfile(), output);
        using var result = WordprocessingDocument.Open(output, false);
        var paragraphs = result.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Select(p => p.InnerText).ToList();
        var first = paragraphs.IndexOf("first");
        AssertEqual(true, paragraphs[first + 1].EndsWith("section", StringComparison.Ordinal));
        AssertEqual("second", paragraphs[first + 2]);
    }

    static void TemplatePreflightValidatesCoverageAndUniqueAnchors()
    {
        using var temp = new TempDirectory();
        var doc = Path.Combine(temp.Path, "source.docx");
        WriteSimpleDocx(doc, "<w:p><w:r><w:t>start</w:t></w:r></w:p><w:p><w:r><w:t>end</w:t></w:r></w:p>");
        var plan = NativeApplicationPlan();
        plan.RequireSourceCoverage = true;
        plan.Blocks = [new() { Type = "sourceRange", StartAnchor = "start", EndAnchor = "end" }, new() { Type = "section", FinalSection = true }];
        try { _ = TemplatePlanPreflight.Check(doc, doc, plan); throw new UnreachableException("Expected omitted block failure."); }
        catch (ArgumentException ex) { AssertContains(ex.Message, "coverage"); }
        plan.SkippedSourceRanges = [new() { Start = 1, End = 2, Reason = "Replaced with reviewed content" }];
        var (resolved, report) = TemplatePlanPreflight.Check(doc, doc, plan);
        AssertEqual(0, report.UnmappedSourceBlocks.Count);
        AssertEqual(1, resolved.Blocks[0].End);
        AssertEqual(0, plan.Blocks[0].End); // Preflight does not mutate callers.
        plan.Blocks[0].StartAnchor = "missing";
        try { _ = TemplatePlanPreflight.Check(doc, doc, plan); throw new UnreachableException("Expected missing anchor failure."); }
        catch (ArgumentException ex) { AssertContains(ex.Message, "exactly one"); }
    }

    static void FinalizeHostFailurePreservesPreviousDeliverable()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "source.docx");
        var output = Path.Combine(temp.Path, "final.docx");
        WriteFixtureDocx(source);
        var bytes = File.ReadAllBytes(source);
        File.WriteAllText(output, "old deliverable");
        AssertEqual(1, RunCli(["finalize", "apply", "--doc", source, "--out", output, "--prog-id", "Thesis.Tests.MissingComHost"]).ExitCode);
        AssertEqual("old deliverable", File.ReadAllText(output));
        AssertBytesEqual(bytes, File.ReadAllBytes(source));
    }

    static void OutputTransactionRollsBackAnEarlierPromotion()
    {
        using var temp = new TempDirectory();
        var first = Path.Combine(temp.Path, "first.txt");
        var second = Path.Combine(temp.Path, "second.txt");
        File.WriteAllText(first, "first old");
        using (var transaction = new FileOutputTransaction())
        {
            File.WriteAllText(transaction.Stage(first), "first new");
            File.WriteAllText(transaction.Stage(second), "second new");
            Directory.CreateDirectory(second); // Promotion 2 fails after promotion 1.
            try { transaction.Commit(); throw new UnreachableException("Expected promotion failure."); }
            catch (IOException) { }
        }
        AssertEqual("first old", File.ReadAllText(first));
        AssertEqual(true, Directory.Exists(second));
    }

    static void ApplyTemplateReportFailurePreservesDeliverable()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "source.docx");
        var planPath = Path.Combine(temp.Path, "plan.json");
        var output = Path.Combine(temp.Path, "final.docx");
        WriteFixtureDocx(source);
        var plan = NativeApplicationPlan();
        plan.Blocks = [new() { Type = "sourceRange", Start = 0, End = 1 }, new() { Type = "section", FinalSection = true }];
        File.WriteAllText(planPath, ThesisJson.Serialize(plan));
        File.WriteAllText(output, "old deliverable");
        AssertEqual(1, RunCli(["apply-template", "--doc", source, "--template", source, "--plan", planPath, "--out", output, "--report", temp.Path]).ExitCode);
        AssertEqual("old deliverable", File.ReadAllText(output));
    }

    static void ContentExtractionSkipsInstitutionOnUnlabelledCover()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "source.docx");
        var content = Path.Combine(temp.Path, "content.json");
        WriteSimpleDocx(source, """
            <w:p><w:r><w:t>示例信息学院</w:t></w:r></w:p>
            <w:p><w:r><w:t>本科毕业设计（论文）</w:t></w:r></w:p>
            <w:p><w:r><w:t>基于数据仓库的风险分析系统</w:t></w:r></w:p>
            <w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:r><w:t>正文。</w:t></w:r></w:p>
            """);
        AssertEqual(0, RunCli(["content", "extract", "--doc", source, "--out", content]).ExitCode);
        AssertEqual("基于数据仓库的风险分析系统", ThesisJson.Deserialize<ThesisContent>(File.ReadAllText(content)).Title);
    }

    static void StructureAuditRespectsCoverAndBodySectionWidths()
    {
        using var temp = new TempDirectory();
        var doc = Path.Combine(temp.Path, "sections.docx");
        using (var package = WordprocessingDocument.Create(doc, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            Table MakeTable(int width) => new(new TableProperties(), new TableGrid(new GridColumn { Width = width.ToString() }),
                new TableRow(new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width.ToString() }), new Paragraph(new Run(new Text("cell"))))));
            SectionProperties Section(int margin) => new(new PageSize { Width = 12000, Height = 16000 },
                new PageMargin { Top = margin, Bottom = margin, Left = (uint)margin, Right = (uint)margin, Header = 0, Footer = 0, Gutter = 0 });
            main.Document = new Document(new Body(MakeTable(11000), new Paragraph(new ParagraphProperties(Section(0))), MakeTable(8000), Section(2000)));
        }
        var report = new TemplateApplicationReport();
        TemplateStructureAudit.Check(doc, report);
        AssertEqual(true, report.StructurePassed);
        using (var package = WordprocessingDocument.Open(doc, true))
            package.MainDocumentPart!.Document!.Body!.Elements<Table>().Last().GetFirstChild<TableGrid>()!.GetFirstChild<GridColumn>()!.Width = "9000";
        TemplateStructureAudit.Check(doc, report);
        AssertEqual(false, report.StructurePassed);
        AssertEqual(true, report.StructuralErrors.Any(e => e.Contains("exceeds section")));
    }

    static void UnsupportedNativeReferencesAreReportedBeforeGeneration()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "source.docx");
        var content = Path.Combine(temp.Path, "content.json");
        var report = Path.Combine(temp.Path, "report.json");
        WriteSimpleDocx(source, """
            <w:p><w:r><w:t>论文题目</w:t></w:r></w:p>
            <w:p><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
            <w:p><w:r><w:footnoteReference w:id="1"/></w:r></w:p>
            """);
        AssertEqual(0, RunCli(["content", "extract", "--doc", source, "--out", content, "--report", report]).ExitCode);
        var facts = ThesisJson.Deserialize<ContentExtractReport>(File.ReadAllText(report));
        AssertEqual(false, facts.Ready);
        AssertEqual(true, facts.Findings.Any(f => f.Code == "native_feature_unsupported"));
        var plan = NativeApplicationPlan();
        plan.Blocks = [new() { Type = "sourceRange", Start = 2, End = 3 }, new() { Type = "section", FinalSection = true }];
        try { _ = TemplatePlanPreflight.Check(source, source, plan); throw new UnreachableException("Expected migration diagnostic."); }
        catch (InvalidDataException ex) { AssertContains(ex.Message, "footnoteReference"); }
        plan.Blocks = [new() { Type = "sourceRange", Start = 0, End = 1 }, new() { Type = "section", FinalSection = true }];
        File.WriteAllText(Path.Combine(temp.Path, "content.json"), ThesisJson.Serialize(new ThesisContent
        {
            Title = "title",
            Chapters = [new() { Title = "chapter", Blocks = [new() { Type = "native", SourcePath = source, SourceSha256 = TemplatePlanPreflight.Fingerprint(source), SourceBodyElementIndex = 0 }] }]
        }));
        File.WriteAllText(Path.Combine(temp.Path, "rules.json"), ThesisJson.Serialize(new TemplateProfile()));
        var output = Path.Combine(temp.Path, "native.docx");
        AssertEqual(0, RunCli(["generate", "--content", Path.Combine(temp.Path, "content.json"), "--rules", Path.Combine(temp.Path, "rules.json"), "--out", output]).ExitCode);
        using var migrated = WordprocessingDocument.Open(output, false);
        AssertEqual(0, migrated.MainDocumentPart!.Document!.Descendants<BookmarkStart>().Count());
    }
}
