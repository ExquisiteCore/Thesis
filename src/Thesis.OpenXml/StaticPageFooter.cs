using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

internal static class StaticPageFooter
{
    internal sealed record Label(string Format, int Number);
    private sealed record Match(FooterReference Reference, Footer Footer, Paragraph Paragraph, Run Run, Text Text);

    public static Label? Read(MainDocumentPart main, SectionProperties section)
    {
        var value = Find(main, section);
        if (value is null) return null;
        var text = value.Text.Text.Trim().Normalize(NormalizationForm.FormKC);
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0)
            return new("decimal", number);
        if (!Regex.IsMatch(text, @"^(?:[IVXLCDM]+|[ivxlcdm]+)$")) return null;
        const string roman = "IVXLCDM";
        int[] values = [1, 5, 10, 50, 100, 500, 1000];
        var total = 0;
        var previous = 0;
        foreach (var character in text.ToUpperInvariant().Reverse())
        {
            var current = values[roman.IndexOf(character)];
            total += current < previous ? -current : current;
            previous = Math.Max(previous, current);
        }
        return new(char.IsUpper(text[0]) ? "upperRoman" : "lowerRoman", total);
    }

    public static void Promote(MainDocumentPart main, SectionProperties section)
    {
        if (Read(main, section) is not { } label || Find(main, section) is not { } match) return;
        // Work on a new part: a template footer may be shared with a retained cover section.
        var part = main.AddNewPart<FooterPart>();
        part.Footer = (Footer)match.Footer.CloneNode(true);
        var text = part.Footer.Descendants<Text>().Single(t => !string.IsNullOrWhiteSpace(t.Text));
        var run = text.Ancestors<Run>().Single();
        var cached = Format(label.Format, label.Number);
        var replacement = new[]
        {
            MakeRun(run, new FieldChar { FieldCharType = FieldCharValues.Begin }),
            MakeRun(run, new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
            MakeRun(run, new FieldChar { FieldCharType = FieldCharValues.Separate }),
            MakeRun(run, new Text(cached) { Space = SpaceProcessingModeValues.Preserve }),
            MakeRun(run, new FieldChar { FieldCharType = FieldCharValues.End })
        };
        foreach (var fieldRun in replacement) run.InsertBeforeSelf(fieldRun);
        run.Remove();
        part.Footer.Save();
        match.Reference.Id = main.GetIdOfPart(part);
    }

    private static string Format(string format, int number)
    {
        if (format == "decimal") return number.ToString(CultureInfo.InvariantCulture);
        var values = new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
        var builder = new StringBuilder();
        foreach (var (value, glyph) in values)
            while (number >= value) { builder.Append(glyph); number -= value; }
        var result = builder.ToString();
        return format == "lowerRoman" ? result.ToLowerInvariant() : result;
    }

    private static Run MakeRun(Run source, OpenXmlElement child)
    {
        var run = new Run();
        if (source.RunProperties is { } properties)
            run.RunProperties = (RunProperties)properties.CloneNode(true);
        run.AppendChild(child);
        return run;
    }

    private static Match? Find(MainDocumentPart main, SectionProperties section)
    {
        var reference = section.Elements<FooterReference>().FirstOrDefault(r => r.Type is null || r.Type.Value == HeaderFooterValues.Default);
        if (reference?.Id?.Value is not string id || !main.TryGetPartById(id, out var part)
            || part is not FooterPart { Footer: { } footer }) return null;
        // Only transform a footer whose sole visible text is a standalone number.
        // Logos, notices, fields and text boxes are intentionally left untouched.
        if (footer.ChildElements.Any(child => child is not Paragraph)
            || footer.Descendants<FieldCode>().Any() || footer.Descendants<SimpleField>().Any()
            || footer.Descendants<Drawing>().Any() || footer.Descendants<Picture>().Any()
            || footer.Descendants<AlternateContent>().Any())
            return null;
        var texts = footer.Descendants<Text>().Where(text => !string.IsNullOrWhiteSpace(text.Text)).ToArray();
        if (texts.Length != 1) return null;
        var text = texts[0];
        var run = text.Ancestors<Run>().SingleOrDefault();
        var paragraph = text.Ancestors<Paragraph>().SingleOrDefault();
        if (run is null || paragraph is null
            || footer.Descendants<Run>().Any(item => item.ChildElements.Any(child => child is not (RunProperties or Text)))
            || paragraph.ChildElements.Any(child => child is not (ParagraphProperties or Run)))
            return null;
        return new(reference, footer, paragraph, run, text);
    }
}
