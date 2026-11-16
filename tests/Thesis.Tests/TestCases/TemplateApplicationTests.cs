using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

internal static partial class Program
{
    static void TemplateApplicationPreservesNativeBlocksAndInheritedHeaders()
    {
        using var temp = new TempDirectory();
        var template = Path.Combine(temp.Path, "template.docx");
        var source = Path.Combine(temp.Path, "source.docx");
        var output = Path.Combine(temp.Path, "output.docx");
        WriteFixtureDocx(template);
        using (var doc = WordprocessingDocument.Open(template, true))
        {
            var main = doc.MainDocumentPart!;
            var body = main.Document!.Body!;
            var originalSection = body.Elements<SectionProperties>().Single();
            var footer = main.AddNewPart<FooterPart>();
            footer.Footer = new Footer(new Paragraph(new Run(new FieldCode(" PAGE "))));
            originalSection.InsertAfter(new FooterReference { Id = main.GetIdOfPart(footer), Type = HeaderFooterValues.Default }, originalSection.GetFirstChild<HeaderReference>()!);
            body.InsertBefore(new Paragraph(new ParagraphProperties((SectionProperties)originalSection.CloneNode(true))), originalSection);
            originalSection.RemoveAllChildren<HeaderReference>(); // The second section inherits the first header.
            originalSection.RemoveAllChildren<FooterReference>();
        }
        var imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        using (var doc = WordprocessingDocument.Create(source, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var image = main.AddImagePart(ImagePartType.Png);
            using (var stream = new MemoryStream(imageBytes)) image.FeedData(stream);
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("before"))),
                new Table(new TableProperties(), new TableGrid(new GridColumn { Width = "1000" }), new TableRow(new TableCell(new Paragraph(new Run(new Text("native table")))))),
                new Paragraph(new Run(new Drawing($$"""
                    <w:drawing xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                      <wp:inline><wp:extent cx="914400" cy="914400"/><wp:docPr id="1" name="fixture"/>
                        <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic>
                          <pic:nvPicPr><pic:cNvPr id="0" name="pixel"/><pic:cNvPicPr/></pic:nvPicPr>
                          <pic:blipFill><a:blip r:embed="{{main.GetIdOfPart(image)}}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                          <pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                        </pic:pic></a:graphicData></a:graphic>
                      </wp:inline>
                    </w:drawing>
                    """))),
                new Paragraph(new Run(new Text("after"))), new SectionProperties()));
        }
        var sourceBefore = File.ReadAllBytes(source);
        var templateBefore = File.ReadAllBytes(template);
        var additionalImage = Path.Combine(temp.Path, "additional.png");
        File.WriteAllBytes(additionalImage, imageBytes);
        var plan = NativeApplicationPlan();
        plan.NormalizePageFooters = true;
        plan.Blocks = [new() { Type = "sourceRange", Start = 0, End = 4 }, new() { Type = "image", ImagePath = additionalImage, ImageWidthEmu = 914400, ImageHeightEmu = 914400, Text = "Added diagram" }, new() { Type = "section", SectionTemplateIndex = 1, FinalSection = true, PageNumberFormat = "decimal", PageNumberStart = 1 }];
        var report = OpenXmlTemplateApplicator.Apply(source, template, plan, output);
        AssertEqual(2, report.Drawings);
        AssertEqual(1, report.Tables);
        using var result = WordprocessingDocument.Open(output, false);
        var outputBody = result.MainDocumentPart!.Document!.Body!;
        AssertEqual("before", outputBody.ChildElements[0].InnerText);
        AssertEqual(true, outputBody.ChildElements[1] is Table);
        AssertEqual(true, outputBody.ChildElements[2].Descendants<Drawing>().Any());
        AssertEqual("after", outputBody.ChildElements[3].InnerText);
        var section = outputBody.Elements<SectionProperties>().Single();
        AssertEqual("rIdHeader1", section.GetFirstChild<HeaderReference>()!.Id!.Value);
        var footerId = section.GetFirstChild<FooterReference>()!.Id!.Value!;
        AssertEqual(true, ((FooterPart)result.MainDocumentPart.GetPartById(footerId)).Footer!.Descendants<FieldCode>().Any(f => f.Text.Contains("PAGE")));
        var blip = outputBody.Descendants<A.Blip>().First();
        AssertEqual("Added diagram", outputBody.ChildElements[4].Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().Single().Description!.Value);
        using var copiedImage = result.MainDocumentPart.GetPartById(blip.Embed!.Value!).GetStream();
        using var buffer = new MemoryStream(); copiedImage.CopyTo(buffer);
        AssertBytesEqual(imageBytes, buffer.ToArray());
        AssertBytesEqual(sourceBefore, File.ReadAllBytes(source));
        AssertBytesEqual(templateBefore, File.ReadAllBytes(template));
    }

    static void TemplateApplicationFailurePreservesExistingOutput()
    {
        using var temp = new TempDirectory();
        var template = Path.Combine(temp.Path, "template.docx");
        var output = Path.Combine(temp.Path, "output.docx");
        var planPath = Path.Combine(temp.Path, "plan.json");
        WriteFixtureDocx(template);
        File.WriteAllText(output, "existing deliverable");
        var plan = NativeApplicationPlan();
        plan.Blocks = [new() { Type = "sourceRange", Start = 0, End = 1 }, new() { Type = "sourceRange", Start = 0, End = 1 }, new() { Type = "section", FinalSection = true }];
        File.WriteAllText(planPath, ThesisJson.Serialize(plan));
        var (code, result) = RunCli(["apply-template", "--doc", template, "--template", template, "--plan", planPath, "--out", output]);
        AssertEqual(1, code);
        AssertEqual("template_application_failed", result.Diagnostics.Single().Code);
        AssertContains(result.Diagnostics.Single().Message, "more than once");
        AssertEqual("existing deliverable", File.ReadAllText(output));
        AssertEqual(0, Directory.GetFiles(temp.Path, "*.tmp").Length);
    }

    static void TemplateApplicationTocRetainsSectionAndPageCaches()
    {
        using var temp = new TempDirectory();
        var template = Path.Combine(temp.Path, "template.docx");
        var output = Path.Combine(temp.Path, "output.docx");
        WriteFixtureDocx(template);
        var plan = NativeApplicationPlan();
        plan.Roles["heading1"] = new() { OutlineLevel = 0 };
        plan.Roles["toc1"] = new();
        plan.TocPages["Chapter 1"] = "7";
        plan.Blocks = [new() { Type = "toc" }, new() { Type = "section", PageNumberFormat = "upperRoman", PageNumberStart = 1 }, new() { Text = "Chapter 1", Role = "heading1" }, new() { Type = "section", FinalSection = true, PageNumberFormat = "decimal", PageNumberStart = 1 }];
        OpenXmlTemplateApplicator.Apply(template, template, plan, output);
        using var result = WordprocessingDocument.Open(output, false);
        var body = result.MainDocumentPart!.Document!.Body!;
        AssertEqual(2, body.Descendants<SectionProperties>().Count());
        var toc = body.Elements<SdtBlock>().Single();
        AssertEqual(true, toc.Descendants<SectionProperties>().Any());
        AssertContains(toc.InnerText, "7");
        AssertEqual(true, toc.Descendants<FieldCode>().Any(f => f.Text.Contains("PAGEREF thesis_heading_0")));
        AssertEqual("thesis_heading_0", body.Elements<Paragraph>().Single().GetFirstChild<BookmarkStart>()!.Name!.Value);
    }

    static TemplateApplicationPlan NativeApplicationPlan() => new()
    {
        Roles = new[] { "body", "image", "table" }.ToDictionary(x => x, _ => new TemplateRoleFormat())
    };
}
