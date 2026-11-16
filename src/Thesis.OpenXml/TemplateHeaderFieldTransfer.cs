using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal static class TemplateHeaderFieldTransfer
{
    public static void Remap(MainDocumentPart main, TemplateApplicationPlan plan)
    {
        var styles = main.StyleDefinitionsPart!.Styles!;
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, format) in plan.Roles.Where(r => r.Value.OutlineLevel.HasValue).OrderBy(r => r.Key is "heading1" or "heading2" or "heading3" ? 0 : 1))
        {
            var name = "Thesis_" + role;
            mappings.TryAdd((format.OutlineLevel!.Value + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), name);
            if (format.TemplateParagraphIndex is not int index || main.Document!.Body!.ChildElements[index] is not Paragraph paragraph) continue;
            var id = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            var style = styles.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == id);
            if (id is not null) mappings.TryAdd(id, name);
            if (style?.StyleName?.Val?.Value is string displayName) mappings.TryAdd(displayName, name);
        }
        var expression = new Regex(@"(\bSTYLEREF\s+)(?:""([^""]+)""|([^\s\\]+))", RegexOptions.IgnoreCase);
        foreach (var root in main.HeaderParts.Select(p => p.RootElement).Concat(main.FooterParts.Select(p => p.RootElement)).OfType<DocumentFormat.OpenXml.OpenXmlPartRootElement>())
        {
            OpenXmlFieldInstructions.RewriteAll(root, instruction => expression.Replace(instruction, match =>
            {
                var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                return mappings.TryGetValue(value, out var mapped) ? match.Groups[1].Value + "\"" + mapped + "\"" : match.Value;
            }));
            root.Save();
        }
    }
}
