using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static class TemplateStructureAudit
{
    public static void Check(string path, TemplateApplicationReport report)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        report.StructuralErrors.Clear();
        report.StructuralErrors.AddRange(DocumentIntegrityAudit.Check(path));
        foreach (var error in new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(doc).Take(30))
            report.StructuralErrors.Add($"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}");
        var body = doc.MainDocumentPart!.Document!.Body!;
        var pending = new List<DocumentFormat.OpenXml.OpenXmlElement>();
        foreach (var block in body.ChildElements)
        {
            pending.Add(block);
            var section = block as SectionProperties ?? block.GetFirstChild<ParagraphProperties>()?.SectionProperties
                ?? block.Descendants<SectionProperties>().LastOrDefault();
            if (section is null) continue;
            var size = section.GetFirstChild<PageSize>();
            var margin = section.GetFirstChild<PageMargin>();
            var pageWidth = (long?)size?.Width?.Value ?? 11906;
            var usable = pageWidth - (long)(margin?.Left?.Value ?? 0) - (long)(margin?.Right?.Value ?? 0) - (long)(margin?.Gutter?.Value ?? 0);
            if (usable <= 0 || ((long?)size?.Height?.Value ?? 16838) - (margin?.Top?.Value ?? 0) - (margin?.Bottom?.Value ?? 0) <= 0)
                report.StructuralErrors.Add("Section margins leave no usable page area.");
            // A cover may intentionally use zero margins. Widths are checked against their own section.
            foreach (var table in pending.SelectMany(b => b is Table t ? new[] { t } : b.Descendants<Table>()).Where(t => t.Ancestors<Table>().Count() == 0))
            {
                var width = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().Sum(c => long.TryParse(c.Width?.Value, out var value) ? value : 0) ?? 0;
                if (width > usable + 20) report.StructuralErrors.Add($"Table grid width {width} exceeds section content width {usable}.");
            }
            pending.Clear();
        }
        if (pending.Count > 0 || body.LastChild is not SectionProperties) report.StructuralErrors.Add("Document has no final section properties.");
        report.StructurePassed = report.StructuralErrors.Count == 0;
    }
}
