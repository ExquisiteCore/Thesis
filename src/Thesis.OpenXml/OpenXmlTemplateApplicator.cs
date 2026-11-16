using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Thesis.OpenXml;

/// <summary>
/// Applies an explicit, reviewed zone plan without flattening source DOCX content to text.
/// The template package supplies stationery; source blocks retain tables, drawings and inline content.
/// </summary>
public static class OpenXmlTemplateApplicator
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static TemplateApplicationReport Apply(string sourcePath, string templatePath, TemplateApplicationPlan plan, string outputPath)
    {
        var checkedPlan = TemplatePlanPreflight.Check(sourcePath, templatePath, plan);
        plan = checkedPlan.Plan;
        var sourceFull = Path.GetFullPath(sourcePath);
        var templateFull = Path.GetFullPath(templatePath);
        var outputFull = Path.GetFullPath(outputPath);
        if (new[] { sourceFull, templateFull }.Contains(outputFull, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Template application must not overwrite either input.");
        if (plan.SchemaVersion != "1.0" || plan.Blocks is null || plan.Blocks.Count == 0 || plan.Roles is null)
            throw new ArgumentException("A version 1.0 plan with blocks and roles is required.");
        if (plan.ContentWidthTwips <= 0 || plan.ContentWidthTwips > 20000)
            throw new ArgumentException("Content width must be between 1 and 20000 twips.");
        if (plan.Blocks.Any(x => x is null) || plan.Blocks.Count(x => x.FinalSection) != 1
            || plan.Blocks[^1].Type != "section" || !plan.Blocks[^1].FinalSection)
            throw new ArgumentException("The plan must contain exactly one final section, as its last block.");
        var temp = outputFull + ".template-" + Guid.NewGuid().ToString("N") + ".tmp";
        var report = new TemplateApplicationReport { Source = sourceFull, Template = templateFull, Output = outputFull, Preflight = checkedPlan.Report };
        try
        {
            File.Copy(templateFull, temp, overwrite: false);
            using (var source = WordprocessingDocument.Open(sourceFull, false))
            using (var output = WordprocessingDocument.Open(temp, true))
            {
                var sourceMain = source.MainDocumentPart ?? throw new InvalidDataException("Source has no main part.");
                var main = output.MainDocumentPart ?? throw new InvalidDataException("Template has no main part.");
                var sourceBlocks = sourceMain.Document?.Body?.ChildElements.ToArray() ?? throw new InvalidDataException("Source has no body.");
                var body = main.Document?.Body ?? throw new InvalidDataException("Template has no body.");
                var templateBlocks = body.ChildElements.Select(x => x.CloneNode(true)).ToArray();
                var sectionSamples = EffectiveSections(body);
                var imported = new Dictionary<string, string>(StringComparer.Ordinal);
                var usedSourceBlocks = new HashSet<int>();
                InstallStyles(main, plan);
                var sourceStyles = OpenXmlStyleTransfer.Copy(sourceMain, main, "source_");
                body.RemoveAllChildren();
                var tocMarkers = new List<Paragraph>();
                foreach (var block in plan.Blocks)
                {
                    switch (block.Type)
                    {
                        case "sourceRange":
                        case "templateRange":
                            var isSource = block.Type == "sourceRange";
                            var candidates = isSource ? sourceBlocks : templateBlocks;
                            if (block.Start < 0 || block.End <= block.Start || block.End > candidates.Length)
                                throw new ArgumentException($"Invalid {block.Type} [{block.Start}, {block.End}).");
                            for (var i = block.Start; i < block.End; i++)
                            {
                                if (candidates[i] is SectionProperties) continue;
                                if (isSource && !usedSourceBlocks.Add(i))
                                    throw new ArgumentException($"Source block {i} was included more than once.");
                                var clone = candidates[i].CloneNode(true);
                                RemoveReviewMarkup(clone, removeTextBoxes: !isSource);
                                foreach (var paragraph in Paragraphs(clone))
                                    paragraph.ParagraphProperties?.RemoveAllChildren<SectionProperties>();
                                if (block.ParagraphTexts.TryGetValue(i, out var replacement))
                                {
                                    if (clone is not Paragraph p) throw new ArgumentException("Paragraph text overrides require a paragraph block.");
                                    ReplaceParagraphText(p, replacement);
                                }
                                if (clone is Table coverTable)
                                foreach (var (address, value) in block.CellTexts)
                                {
                                    var coordinates = address.Split(',').Select(int.Parse).ToArray();
                                    if (coordinates.Length != 2) throw new ArgumentException("Cell address must be row,column.");
                                    var cell = coverTable.Elements<TableRow>().ElementAt(coordinates[0]).Elements<TableCell>().ElementAt(coordinates[1]);
                                    var paragraph = cell.Elements<Paragraph>().FirstOrDefault() ?? cell.AppendChild(new Paragraph());
                                    ReplaceParagraphText(paragraph, value);
                                }
                                ReplaceText(clone, isSource ? plan.SourceTextReplacements : plan.TemplateTextReplacements);
                                if (isSource)
                                {
                                    OpenXmlBookmarkTransfer.Remap(main, clone);
                                    OpenXmlStyleTransfer.Remap(clone, sourceStyles);
                                    CopyRelationships(sourceMain, main, clone, imported);
                                    FormatBlock(clone, block.Role, plan);
                                    report.CopiedSourceBlocks.Add(i);
                                }
                                else if (block.Role is not null) FormatBlock(clone, block.Role, plan);
                                body.Append(clone);
                            }
                            break;
                        case "paragraph":
                            body.Append(CreateParagraph(block.Text ?? "", block.Role ?? "body", plan));
                            break;
                        case "table":
                            body.Append(CreateTable(block, plan));
                            break;
                        case "image":
                            body.Append(CreateImage(main, block, plan));
                            break;
                        case "toc":
                            var marker = new Paragraph();
                            body.Append(marker);
                            tocMarkers.Add(marker);
                            break;
                        case "section":
                            if (block.SectionTemplateIndex < 0 || block.SectionTemplateIndex >= sectionSamples.Count)
                                throw new ArgumentException("Section sample index is outside the template.");
                            var section = (SectionProperties)sectionSamples[block.SectionTemplateIndex].CloneNode(true);
                            section.RemoveAllChildren<SectionType>();
                            section.AddChild(new SectionType { Val = SectionMarkValues.NextPage }, true);
                            section.RemoveAllChildren<Columns>();
                            section.AddChild(new Columns { ColumnCount = 1, Space = "720" }, true);
                            if (block.PageNumberFormat is not null)
                            {
                                section.RemoveAllChildren<PageNumberType>();
                                var numbering = new PageNumberType();
                                numbering.SetAttribute(new OpenXmlAttribute("w", "fmt", W.NamespaceName, block.PageNumberFormat));
                                if (block.PageNumberStart.HasValue) numbering.Start = block.PageNumberStart.Value;
                                section.AddChild(numbering, true);
                            }
                            if (block.NoPageNumber) section.RemoveAllChildren<FooterReference>();
                            else if (plan.NormalizePageFooters) NormalizeFooter(main, section, block.PageNumberFormat == "upperRoman");
                            if (block.FinalSection) body.Append(section);
                            else
                            {
                                var last = body.LastChild as Paragraph;
                                if (last is null) { last = new Paragraph(); body.Append(last); }
                                last.ParagraphProperties ??= new ParagraphProperties();
                                if (last.ParagraphProperties.SectionProperties is not null)
                                    throw new ArgumentException("Two consecutive section boundaries require a content block between them.");
                                last.ParagraphProperties.SectionProperties = section;
                            }
                            break;
                        default: throw new ArgumentException($"Unknown plan block type: {block.Type}");
                    }
                }
                if (body.LastChild is not SectionProperties)
                    throw new ArgumentException("Plan must end with a final section block.");
                BuildToc(body, tocMarkers, plan, report);
                NormalizeDrawingIdsAndSizes(body, plan.ContentWidthTwips);
                if (main.WordprocessingCommentsPart is not null) main.DeletePart(main.WordprocessingCommentsPart);
                var settings = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
                settings.Settings ??= new Settings();
                settings.Settings.RemoveAllChildren<UpdateFieldsOnOpen>();
                settings.Settings.AddChild(new UpdateFieldsOnOpen { Val = true }, true);
                settings.Settings.RemoveAllChildren<TrackRevisions>();
                settings.Settings.Save();
                main.Document.Save();
                report.Tables = body.Descendants<Table>().Count();
                report.Drawings = body.Descendants<Drawing>().Count();
                report.Sections = body.Descendants<SectionProperties>().Count();
                report.Notes.Add("Native source blocks are retained; explicit plan replacements are editorial changes.");
                report.Notes.Add("Assembly status is separate from structural validation, rendered pagination and scholarly review.");
            }
            // Reopen before promotion, so an unreadable package never replaces an existing output.
            using (var verification = WordprocessingDocument.Open(temp, false))
                _ = verification.MainDocumentPart?.Document?.Body ?? throw new InvalidDataException("Generated document is unreadable.");
            File.Move(temp, outputFull, overwrite: true);
            report.AssemblyPassed = true;
            return report;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static IEnumerable<Paragraph> Paragraphs(OpenXmlElement block) =>
        block is Paragraph p ? new[] { p }.Concat(block.Descendants<Paragraph>()) : block.Descendants<Paragraph>();

    private static List<SectionProperties> EffectiveSections(Body body)
    {
        var headers = new Dictionary<string, HeaderReference>();
        var footers = new Dictionary<string, FooterReference>();
        var result = new List<SectionProperties>();
        foreach (var original in body.Descendants<SectionProperties>())
        {
            foreach (var h in original.Elements<HeaderReference>()) headers[h.Type?.InnerText ?? "default"] = (HeaderReference)h.CloneNode(true);
            foreach (var f in original.Elements<FooterReference>()) footers[f.Type?.InnerText ?? "default"] = (FooterReference)f.CloneNode(true);
            var copy = (SectionProperties)original.CloneNode(true);
            copy.RemoveAllChildren<HeaderReference>(); copy.RemoveAllChildren<FooterReference>();
            foreach (var f in footers.Values.Reverse()) copy.PrependChild(f.CloneNode(true));
            foreach (var h in headers.Values.Reverse()) copy.PrependChild(h.CloneNode(true));
            result.Add(copy);
        }
        return result;
    }

    private static void RemoveReviewMarkup(OpenXmlElement block, bool removeTextBoxes)
    {
        foreach (var el in block.Descendants().Where(x => x is CommentRangeStart or CommentRangeEnd or CommentReference or ProofError
            || x.LocalName is "lastRenderedPageBreak" or "bookmarkStart" or "bookmarkEnd").ToArray()) el.Remove();
        if (removeTextBoxes)
            foreach (var el in block.Descendants().Where(x => x.LocalName == "AlternateContent" && x.Descendants().Any(c => c.LocalName == "txbxContent")).ToArray()) el.Remove();
    }

    private static void ReplaceParagraphText(Paragraph paragraph, string text)
    {
        var format = paragraph.Elements<Run>().FirstOrDefault()?.RunProperties?.CloneNode(true) as RunProperties;
        foreach (var child in paragraph.ChildElements.Where(x => x is not ParagraphProperties).ToArray()) child.Remove();
        var run = new Run();
        if (format is not null) run.Append(format);
        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.Append(run);
    }

    private static void ReplaceText(OpenXmlElement block, Dictionary<string, string> replacements)
    {
        foreach (var p in Paragraphs(block))
        {
            foreach (var pair in replacements)
            {
                var nodes = p.Descendants<Text>().Where(x => !x.Ancestors().Any(a => a.LocalName == "txbxContent")).ToList();
                var joined = string.Concat(nodes.Select(x => x.Text));
                var start = joined.IndexOf(pair.Key, StringComparison.Ordinal);
                if (start < 0) continue;
                if (nodes.Count == 1) { nodes[0].Text = joined.Replace(pair.Key, pair.Value, StringComparison.Ordinal); continue; }
                // Distribute replacements across original runs, preserving all other inline XML.
                while (start >= 0)
                {
                    var end = start + pair.Key.Length; var offset = 0; var inserted = false;
                    foreach (var node in nodes)
                    {
                        var value = node.Text; var next = offset + value.Length;
                        if (next > start && offset < end)
                        {
                            var prefix = value[..Math.Clamp(start - offset, 0, value.Length)];
                            var suffix = value[Math.Clamp(end - offset, 0, value.Length)..];
                            node.Text = prefix + (inserted ? "" : pair.Value) + suffix;
                            node.Space = SpaceProcessingModeValues.Preserve; inserted = true;
                        }
                        offset = next;
                    }
                    joined = string.Concat(nodes.Select(x => x.Text));
                    start = joined.IndexOf(pair.Key, start + pair.Value.Length, StringComparison.Ordinal);
                }
            }
        }
    }

    private static void CopyRelationships(MainDocumentPart source, MainDocumentPart target, OpenXmlElement block, Dictionary<string, string> map)
        => OpenXmlPartTransfer.Copy(source, target, block, map);

    private static XElement Element(string name, params (string Name, object? Value)[] attributes) =>
        new(W + name, attributes.Where(a => a.Value is not null).Select(a => new XAttribute(W + a.Name, a.Value!)));

    private static XElement RunFormat(TemplateRoleFormat f) => new(W + "rPr",
        Element("rFonts", ("ascii", f.LatinFont), ("hAnsi", f.LatinFont), ("eastAsia", f.EastAsiaFont), ("cs", f.LatinFont)),
        Element("b", ("val", f.Bold ? "1" : "0")), Element("bCs", ("val", f.Bold ? "1" : "0")),
        Element("color", ("val", "000000")), Element("sz", ("val", f.SizeHalfPoints)), Element("szCs", ("val", f.SizeHalfPoints)),
        Element("lang", ("val", "en-US"), ("eastAsia", "zh-CN")));

    private static XElement ParagraphFormat(TemplateRoleFormat f) => new(W + "pPr",
        f.KeepNext ? Element("keepNext") : null, Element("widowControl"),
        Element("snapToGrid", ("val", "0")),
        Element("spacing", ("before", f.Before), ("after", f.After), ("line", f.Line), ("lineRule", f.LineRule)),
        Element("ind", ("firstLineChars", f.FirstLineChars == 0 ? null : f.FirstLineChars),
            ("left", f.LeftTwips == 0 ? null : f.LeftTwips), ("hanging", f.HangingTwips == 0 ? null : f.HangingTwips)),
        Element("jc", ("val", f.Alignment)), f.OutlineLevel.HasValue ? Element("outlineLvl", ("val", f.OutlineLevel.Value)) : null);

    private static void InstallStyles(MainDocumentPart main, TemplateApplicationPlan plan)
    {
        var part = main.StyleDefinitionsPart ?? main.AddNewPart<StyleDefinitionsPart>();
        part.Styles ??= new Styles();
        // WPS-authored templates can place uiPriority after qFormat. Repair its
        // schema order while retaining the supplied styles and their formatting.
        foreach (var existing in part.Styles.Elements<Style>())
        {
            var priority = existing.GetFirstChild<UIPriority>();
            if (priority is null) continue;
            priority.Remove();
            existing.AddChild(priority, true);
        }
        foreach (var (name, format) in plan.Roles)
        {
            if (format.SizeHalfPoints < 2 || format.SizeHalfPoints > 200 || format.Line <= 0)
                throw new ArgumentException($"Invalid font size or line spacing for role {name}.");
            var id = "Thesis_" + name;
            part.Styles.Elements<Style>().FirstOrDefault(s => s.StyleId == id)?.Remove();
            var pPr = ParagraphFormat(format);
            if (format.PageBreakBefore)
            {
                var keep = pPr.Element(W + "keepNext");
                if (keep is null) pPr.AddFirst(Element("pageBreakBefore")); else keep.AddAfterSelf(Element("pageBreakBefore"));
            }
            var style = new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", id),
                Element("name", ("val", id)), Element("next", ("val", "Thesis_body")), Element("qFormat"), pPr, RunFormat(format));
            part.Styles.Append(new Style(style.ToString(SaveOptions.DisableFormatting)));
        }
        part.Styles.Save();
    }

    private static string GuessRole(Paragraph p)
    {
        var text = p.InnerText.Trim();
        if (Regex.IsMatch(text, @"^第\s*[一二三四五六七八九十0-9]+\s*章")) return "heading1";
        if (Regex.IsMatch(text, @"^\d+\.\d+\.\d+\s")) return "heading3";
        if (Regex.IsMatch(text, @"^\d+\.\d+\s")) return "heading2";
        if (Regex.IsMatch(text, @"^表\s*\d+[.－-]\d+")) return "tableCaption";
        if (Regex.IsMatch(text, @"^图\s*\d+[.－-]\d+")) return "figureCaption";
        if (p.Descendants<Drawing>().Any()) return "image";
        return "body";
    }

    private static void FormatBlock(OpenXmlElement block, string? role, TemplateApplicationPlan plan)
    {
        if (block is Table table)
        {
            FormatTable(table, plan);
            return;
        }
        foreach (var paragraph in Paragraphs(block)) ApplyRole(paragraph, role ?? GuessRole(paragraph), plan);
    }

    private static void ApplyRole(Paragraph paragraph, string role, TemplateApplicationPlan plan)
    {
        if (!plan.Roles.TryGetValue(role, out var format)) throw new ArgumentException($"Undefined role: {role}.");
        var numbering = paragraph.ParagraphProperties?.NumberingProperties;
        if (numbering?.NumberingId?.Val?.Value is int numberId && numberId != 0)
            throw new InvalidDataException("Source automatic numbering requires an explicit mapping; convert it in the reviewed plan before importing.");
        paragraph.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = "Thesis_" + role });
        foreach (var run in paragraph.Descendants<Run>())
        {
            var semantic = run.RunProperties?.ChildElements.Where(x => x is VerticalTextAlignment or Italic or SymbolChar).Select(x => x.CloneNode(true)).ToArray() ?? [];
            var rPr = new RunProperties(RunFormat(format).ToString(SaveOptions.DisableFormatting));
            foreach (var value in semantic) rPr.AddChild(value, true);
            run.RunProperties = rPr;
        }
        foreach (var pageBreak in paragraph.Descendants<Break>().Where(x => x.Type?.Value == BreakValues.Page).ToArray()) pageBreak.Remove();
    }

    private static Paragraph CreateParagraph(string text, string role, TemplateApplicationPlan plan)
    {
        var p = new Paragraph();
        foreach (var segment in Regex.Split(text, @"(\[\d+\]|\n)"))
        {
            if (segment.Length == 0) continue;
            if (segment == "\n") { p.Append(new Run(new Break())); continue; }
            var run = new Run(new Text(segment) { Space = SpaceProcessingModeValues.Preserve });
            if (role != "reference" && Regex.IsMatch(segment, @"^\[\d+\]$"))
                run.RunProperties = new RunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript });
            p.Append(run);
        }
        ApplyRole(p, role, plan);
        if (plan.Roles[role].BoldPrefix is string prefix && text.StartsWith(prefix, StringComparison.Ordinal))
        {
            var first = p.Elements<Run>().First();
            var value = first.GetFirstChild<Text>()!;
            var suffix = (Run)first.CloneNode(true);
            suffix.GetFirstChild<Text>()!.Text = value.Text[prefix.Length..];
            value.Text = prefix;
            first.RunProperties!.Bold = new Bold();
            first.RunProperties.BoldComplexScript = new BoldComplexScript();
            first.InsertAfterSelf(suffix);
        }
        return p;
    }

    private static Table CreateTable(TemplateApplicationBlock block, TemplateApplicationPlan plan)
    {
        if (block.Rows.Count == 0 || block.Rows.Any(x => x.Count != block.Rows[0].Count) || block.Rows[0].Count == 0)
            throw new ArgumentException("Table rows must have a consistent nonzero column count.");
        var widths = block.ColumnWidths.Count == block.Rows[0].Count ? block.ColumnWidths : Enumerable.Repeat(plan.ContentWidthTwips / block.Rows[0].Count, block.Rows[0].Count).ToList();
        var t = new Table(new TableProperties(new TableWidth { Type = TableWidthUnitValues.Dxa, Width = plan.ContentWidthTwips.ToString(CultureInfo.InvariantCulture) }),
            new TableGrid(widths.Select(x => new GridColumn { Width = x.ToString(CultureInfo.InvariantCulture) })));
        foreach (var row in block.Rows)
        {
            var r = new TableRow();
            for (var i = 0; i < row.Count; i++) r.Append(new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = widths[i].ToString(CultureInfo.InvariantCulture) }), CreateParagraph(row[i], "table", plan)));
            t.Append(r);
        }
        FormatTable(t, plan);
        return t;
    }

    private static void FormatTable(Table table, TemplateApplicationPlan plan)
    {
        table.TableProperties ??= new TableProperties();
        table.TableProperties.TableStyle = null;
        table.TableProperties.TableWidth = new TableWidth { Type = TableWidthUnitValues.Dxa, Width = plan.ContentWidthTwips.ToString(CultureInfo.InvariantCulture) };
        table.TableProperties.TableJustification = new TableJustification { Val = TableRowAlignmentValues.Center };
        table.TableProperties.TableBorders = new TableBorders(new TopBorder { Val = BorderValues.Single, Size = 8 }, new LeftBorder { Val = BorderValues.Single, Size = 4 }, new BottomBorder { Val = BorderValues.Single, Size = 8 }, new RightBorder { Val = BorderValues.Single, Size = 4 }, new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 });
        table.TableProperties.TableLayout = new TableLayout { Type = TableLayoutValues.Fixed };
        var grid = table.GetFirstChild<TableGrid>();
        if (grid is not null)
        {
            var columns = grid.Elements<GridColumn>().ToList();
            var sum = columns.Sum(c => int.TryParse(c.Width?.Value, out var width) ? width : 1);
            foreach (var col in columns)
                col.Width = Math.Max(1, (int)Math.Round((int.TryParse(col.Width?.Value, out var width) ? width : 1) * (double)plan.ContentWidthTwips / Math.Max(sum, 1))).ToString(CultureInfo.InvariantCulture);
        }
        var rowIndex = 0;
        foreach (var row in table.Elements<TableRow>())
        {
            row.TableRowProperties ??= new TableRowProperties();
            row.TableRowProperties.RemoveAllChildren<TableRowHeight>();
            row.TableRowProperties.RemoveAllChildren<CantSplit>();
            row.TableRowProperties.Append(new CantSplit());
            if (rowIndex++ == 0) { row.TableRowProperties.RemoveAllChildren<TableHeader>(); row.TableRowProperties.Append(new TableHeader()); }
            foreach (var cell in row.Elements<TableCell>())
            {
                cell.TableCellProperties?.RemoveAllChildren<Shading>();
                foreach (var p in cell.Descendants<Paragraph>()) ApplyRole(p, "table", plan);
            }
        }
    }

    private static void NormalizeFooter(MainDocumentPart main, SectionProperties section, bool roman)
    {
        // Some supplied templates store PAGE in a floating text box. Preserve the field,
        // but place it in an ordinary centered paragraph so different renderers agree.
        var existing = section.Elements<FooterReference>().FirstOrDefault(x => x.Type?.Value == HeaderFooterValues.Default);
        if (existing is null) return;
        var old = main.GetPartById(existing.Id!) as FooterPart;
        var fields = old?.Footer?.Descendants<FieldCode>().Any(x => x.Text.Contains("PAGE", StringComparison.OrdinalIgnoreCase)) == true;
        if (!fields) return;
        var footer = main.AddNewPart<FooterPart>();
        var format = new TemplateRoleFormat { EastAsiaFont = roman ? "宋体" : "Times New Roman", SizeHalfPoints = 18, Alignment = "center", Line = 240 };
        var p = new Paragraph(new ParagraphProperties(ParagraphFormat(format).ToString(SaveOptions.DisableFormatting)));
        foreach (var child in FieldRuns(" PAGE ", roman ? "I" : "1", format)) p.Append(child);
        footer.Footer = new Footer(p); footer.Footer.Save();
        section.RemoveAllChildren<FooterReference>();
        // AddChild resolves the shared header/footer choice by replacing existing
        // header references. Insert into the ordered reference group explicitly.
        var reference = new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) };
        var lastHeader = section.Elements<HeaderReference>().LastOrDefault();
        if (lastHeader is null) section.PrependChild(reference);
        else section.InsertAfter(reference, lastHeader);
    }

    private static List<Run> FieldRuns(string code, string cached, TemplateRoleFormat? format = null)
    {
        Run Make(OpenXmlElement child) => format is null ? new Run(child) : new Run(new RunProperties(RunFormat(format).ToString(SaveOptions.DisableFormatting)), child);
        return [Make(new FieldChar { FieldCharType = FieldCharValues.Begin }), Make(new FieldCode(code) { Space = SpaceProcessingModeValues.Preserve }), Make(new FieldChar { FieldCharType = FieldCharValues.Separate }), Make(new Text(cached) { Space = SpaceProcessingModeValues.Preserve }), Make(new FieldChar { FieldCharType = FieldCharValues.End })];
    }

    private static Paragraph CreateImage(MainDocumentPart main, TemplateApplicationBlock block, TemplateApplicationPlan plan)
    {
        if (block.ImagePath is null || block.ImageWidthEmu <= 0 || block.ImageHeightEmu <= 0
            || block.ImageWidthEmu > 20000000 || block.ImageHeightEmu > 20000000)
            throw new ArgumentException("An image block requires a local PNG/JPEG path and positive dimensions up to 20000000 EMU.");
        var type = Path.GetExtension(block.ImagePath).ToLowerInvariant() switch
        {
            ".png" => ImagePartType.Png,
            ".jpg" or ".jpeg" => ImagePartType.Jpeg,
            _ => throw new ArgumentException("Image blocks accept PNG and JPEG files.")
        };
        var image = main.AddImagePart(type);
        using (var stream = File.OpenRead(block.ImagePath)) image.FeedData(stream);
        var picture = new PIC.Picture(
            new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 0, Name = Path.GetFileName(block.ImagePath) }, new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(new A.Blip { Embed = main.GetIdOfPart(image) }, new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = block.ImageWidthEmu, Cy = block.ImageHeightEmu }), new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
        var inline = new DW.Inline(new DW.Extent { Cx = block.ImageWidthEmu, Cy = block.ImageHeightEmu },
            new DW.DocProperties { Id = 1, Name = Path.GetFileName(block.ImagePath), Description = block.Text ?? "" },
            new A.Graphic(new A.GraphicData(picture) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }));
        var paragraph = new Paragraph(new Run(new Drawing(inline)));
        ApplyRole(paragraph, block.Role ?? "image", plan);
        return paragraph;
    }

    private static void BuildToc(Body body, List<Paragraph> markers, TemplateApplicationPlan plan, TemplateApplicationReport report)
    {
        var headings = body.Elements<Paragraph>().Where(p =>
        {
            var role = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value?.Replace("Thesis_", "", StringComparison.Ordinal);
            return role is not null && plan.Roles.TryGetValue(role, out var f) && f.OutlineLevel is >= 0 and <= 2;
        }).ToList();
        for (var i = 0; i < headings.Count; i++)
        {
            var p = headings[i]; var id = (10000 + i).ToString(CultureInfo.InvariantCulture);
            p.InsertAfter(new BookmarkStart { Id = id, Name = "thesis_heading_" + i }, p.ParagraphProperties!);
            p.Append(new BookmarkEnd { Id = id }); report.Headings.Add(p.InnerText);
        }
        foreach (var marker in markers)
        {
            var toc = new SdtContentBlock();
            for (var i = 0; i < headings.Count; i++)
            {
                var heading = headings[i];
                var headingRole = heading.ParagraphProperties!.ParagraphStyleId!.Val!.Value!.Replace("Thesis_", "", StringComparison.Ordinal);
                var level = plan.Roles[headingRole].OutlineLevel!.Value;
                var role = "toc" + (level + 1);
                var p = CreateParagraph(heading.InnerText, role, plan);
                p.ParagraphProperties!.Tabs = new Tabs(new TabStop { Val = TabStopValues.Right, Leader = TabStopLeaderCharValues.Dot, Position = plan.ContentWidthTwips });
                p.Append(new Run(new TabChar()));
                var value = plan.TocPages.GetValueOrDefault(heading.InnerText, "0");
                var pageFormat = new TemplateRoleFormat { SizeHalfPoints = 24 };
                foreach (var run in FieldRuns($" PAGEREF thesis_heading_{i} \\h ", value, pageFormat)) p.Append(run);
                if (i == 0)
                {
                    var preamble = new[] { new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }), new Run(new FieldCode(" TOC \\o \"1-3\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }), new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }) };
                    foreach (var run in preamble.Reverse()) p.InsertAfter(run, p.ParagraphProperties!);
                }
                if (i == headings.Count - 1) p.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
                toc.Append(p);
            }
            if (marker.ParagraphProperties?.SectionProperties is SectionProperties endingSection && toc.LastChild is Paragraph lastEntry)
            {
                lastEntry.ParagraphProperties ??= new ParagraphProperties();
                lastEntry.ParagraphProperties.SectionProperties = (SectionProperties)endingSection.CloneNode(true);
            }
            marker.InsertBeforeSelf(new SdtBlock(new SdtProperties(new SdtContentDocPartObject(new DocPartGallery { Val = "Table of Contents" }, new DocPartUnique())), toc));
            marker.Remove();
        }
    }

    private static void NormalizeDrawingIdsAndSizes(Body body, int contentWidth)
    {
        uint id = 1; var maxWidth = contentWidth * 635L;
        foreach (var property in body.Descendants<DW.DocProperties>()) property.Id = id++;
        foreach (var drawing in body.Descendants<Drawing>())
        {
            var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
            if (extent?.Cx?.Value is not long width || width <= maxWidth || extent.Cy?.Value is not long height) continue;
            var newHeight = (long)Math.Round(height * (double)maxWidth / width);
            extent.Cx = maxWidth; extent.Cy = newHeight;
            foreach (var x in drawing.Descendants<A.Extents>()) { x.Cx = maxWidth; x.Cy = newHeight; }
        }
    }
}
