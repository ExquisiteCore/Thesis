using DocumentFormat.OpenXml;
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
        var ignoredLegacyValues = 0;
        foreach (var error in new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(doc).Take(100))
        {
            if (IsSupportedLegacyValue(error)) { ignoredLegacyValues++; continue; }
            report.StructuralErrors.Add($"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}");
        }
        if (ignoredLegacyValues > 0)
            report.Notes.Add($"Preserved {ignoredLegacyValues} legacy Word/WPS table-format values accepted by Office renderers.");
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

    private static bool IsSupportedLegacyValue(ValidationErrorInfo error)
    {
        var path = error.Path?.XPath ?? "";
        var description = error.Description ?? "";
        if ((path.Contains("/w:style[", StringComparison.Ordinal) || path.Contains("/w:tblPr[", StringComparison.Ordinal))
            && description.Contains("invalid child element", StringComparison.Ordinal)
            && (description.Contains("wBefore", StringComparison.Ordinal) || description.Contains("tblStyle", StringComparison.Ordinal)))
            return true;
        if (path.Contains("noWrap", StringComparison.Ordinal) && description.Contains("invalid value '0'", StringComparison.Ordinal))
            return true;
        if (error.Node is not OpenXmlElement node) return false;
        if (node is NoWrap && node.GetAttributes().Any(attribute => attribute.LocalName == "val" && attribute.Value == "0"))
            return true;
        if (node.LocalName == "tblStyle" && node.Parent?.LocalName == "tblPr" && node.Parent.Parent is Style tableStyle
            && tableStyle.Type?.Value == StyleValues.Table
            && node.GetAttributes().Any(attribute => attribute.LocalName == "val" && attribute.Value == tableStyle.StyleId?.Value))
            return true;
        if (node.LocalName == "wBefore" && node.Parent?.LocalName == "trPr" && node.Parent.Parent is Style tableRowStyle
            && tableRowStyle.Type?.Value == StyleValues.Table)
            return true;
        return false;
    }
}
