using System.Text;
using System.Text.RegularExpressions;
using Thesis.Schema;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Outline;

namespace Thesis.Application;

/// <summary>Managed equivalent of the legacy PDF audit. Reads rendered glyph positions, never edits a PDF.</summary>
public static class PdfLayoutAudit
{
    public sealed class Manifest
    {
        public List<string> Headings { get; set; } = [];
        public List<HeadingEntry> HeadingEntries { get; set; } = [];
        public Dictionary<string, string> PageFormats { get; set; } = [];
        public Dictionary<string, HostBookmarkPage>? HostPages { get; set; }
    }
    public sealed class HeadingEntry { public string Key { get; set; } = ""; public string Text { get; set; } = ""; }
    private sealed record Line(string Text, double Left, double Top, double Right, double Bottom);
    private sealed record PageInfo(List<Line> Lines, List<string> Labels, double Height);
    private static readonly Regex Footer = new(@"^[-—–·\s]*((?:[0-9]\s*)+|(?:[IVXLCDMivxlcdm]\s*)+)[-—–·\s]*$", RegexOptions.Compiled);
    private static string Compact(string text) => Regex.Replace(text.Normalize(NormalizationForm.FormKC), @"\s+", "");
    private static string Key(string text) => Regex.Replace(Compact(text), @"^(?:第[一二三四五六七八九十百0-9]+[章节]|[0-9]+(?:\.[0-9]+)*[.、]?)", "");

    public static LayoutFacts Analyze(string path, Manifest manifest, CancellationToken cancellationToken = default)
    {
        using var pdf = PdfDocument.Open(path);
        var result = new LayoutFacts { PageCount = pdf.NumberOfPages };
        var pages = new List<PageInfo>();
        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = Lines(page);
            var numeric = lines.Where(l => l.Top > page.Height * .85 && Footer.IsMatch(l.Text)).ToArray();
            var lowest = numeric.Select(l => l.Top).DefaultIfEmpty(0).Max();
            var labels = numeric.Where(l => l.Top >= lowest - 3).Select(l => Compact(Footer.Match(l.Text).Groups[1].Value)).ToList();
            pages.Add(new(lines, labels, page.Height));
            foreach (var l in lines.Where(l => l.Left < -1 || l.Top < -1 || l.Right > page.Width + 1 || l.Bottom > page.Height + 1))
                result.Errors.Add($"Page {page.Number}: text extends outside the page: {l.Text[..Math.Min(60, l.Text.Length)]}");
            foreach (var image in page.GetImages())
                if (image.BoundingBox.Left < -1 || image.BoundingBox.Bottom < -1 || image.BoundingBox.Right > page.Width + 1 || image.BoundingBox.Top > page.Height + 1)
                    result.Errors.Add($"Page {page.Number}: image extends outside the page.");
        }
        var outlines = new Dictionary<string, HashSet<int>>();
        void Visit(IEnumerable<BookmarkNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node is DocumentBookmarkNode item)
                {
                    var key = Compact(node.Title);
                    if (!outlines.TryGetValue(key, out var set)) outlines[key] = set = [];
                    set.Add(item.PageNumber - 1);
                }
                Visit(node.Children);
            }
        }
        if (pdf.TryGetBookmarks(out var bookmarks)) Visit(bookmarks.Roots);
        var toc = pages.Select((p, i) => (p, i)).Where(x => x.p.Lines.Any(l => Compact(l.Text).ToLowerInvariant() is "目录" or "contents" or "tableofcontents")
            || x.p.Lines.Count(l => Regex.IsMatch(l.Text, @"[.．·]{3,}\s*(?:[0-9]+|[IVXLCDMivxlcdm]+)\s*$")) >= 2).Select(x => x.i).ToHashSet();
        var entries = manifest.HeadingEntries.Count > 0 ? manifest.HeadingEntries : manifest.Headings.Select(h => new HeadingEntry { Key = h, Text = h }).ToList();
        var headingKeys = entries.Select(e => Compact(e.Text)).ToHashSet();
        if (entries.Count != headingKeys.Count && (manifest.HostPages?.Count ?? 0) == 0) result.Errors.Add("Repeated heading text requires distinct headings for TOC page-cache verification.");
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Compact(entry.Text); var format = manifest.PageFormats.GetValueOrDefault(entry.Key, "decimal");
            var matches = new HashSet<int>(); var positions = new Dictionary<int, Line>();
            for (var index = 0; index < pages.Count; index++)
            {
                if (toc.Contains(index)) continue;
                var page = pages[index];
                for (var i = 0; i < page.Lines.Count; i++)
                {
                    var line = page.Lines[i];
                    if ((Compact(line.Text) == key || Key(line.Text) == Key(entry.Text)) && line.Top < page.Height * .9)
                    { matches.Add(index); positions[index] = line; }
                    else if (key.StartsWith(Compact(line.Text), StringComparison.Ordinal))
                        for (var count = 2; count <= 3 && i + count <= page.Lines.Count; count++)
                            if (Compact(string.Concat(page.Lines.Skip(i).Take(count).Select(l => l.Text))) == key)
                            { matches.Add(index); positions[index] = page.Lines[i + count - 1]; }
                }
            }
            var explicitPages = outlines.GetValueOrDefault(key);
            if (manifest.HostPages?.TryGetValue(entry.Key, out var host) == true)
            {
                var index = host.PhysicalPage - 1;
                if (index < 0 || index >= pages.Count) { result.Errors.Add("Office bookmark points outside the exported PDF: " + entry.Key); continue; }
                matches = [index];
                if (!Compact(string.Concat(pages[index].Lines.Select(l => l.Text))).Contains(Key(entry.Text), StringComparison.Ordinal))
                    result.Errors.Add("Office heading is missing on its exported PDF page: " + entry.Text);
            }
            else if (explicitPages?.Count == 1) matches = explicitPages;
            else if (matches.Count > 1) matches = matches.Where(i => Labels(pages[i].Labels, format).Count == 1).ToHashSet();
            if (matches.Count != 1) { result.Errors.Add($"Heading must resolve to one rendered page: {entry.Text} ({matches.Count} matches)"); continue; }
            var pageIndex = matches.Single();
            if (pageIndex < 0 || pageIndex >= pages.Count) { result.Errors.Add("PDF bookmark points outside the document: " + entry.Text); continue; }
            var matched = pages[pageIndex]; var valid = Labels(matched.Labels, format);
            if (valid.Count != 1) result.Errors.Add($"Heading page label is missing or ambiguous: {entry.Text}, PDF page {pageIndex + 1}");
            else result.TocPages[entry.Key] = valid[0];
            if (positions.TryGetValue(pageIndex, out var box) && box.Bottom > matched.Height * .6
                && !matched.Lines.Any(l => l.Top >= box.Bottom - 1 && l.Bottom < matched.Height * .94 && !headingKeys.Contains(Compact(l.Text)) && !Footer.IsMatch(l.Text)))
                result.Errors.Add($"Heading has no following body content on PDF page {pageIndex + 1}: {entry.Text}");
        }
        result.Errors = result.Errors.Distinct().Order().ToList();
        result.ReviewItems = ["Review table continuations, repeated table headers and caption placement in the exported PDF.",
            "Review font substitution, formula layout, cover alignment and page balance visually.",
            "This report checks rendered pages and TOC labels; scholarly content and references need separate review."];
        return result;
    }
    private static List<string> Labels(List<string> values, string format)
    {
        var pattern = format == "decimal" ? "^[0-9]+$" : format == "upperRoman" ? "^[IVXLCDM]+$" : "^[ivxlcdm]+$";
        return values.Where(v => Regex.IsMatch(v, pattern)).ToList();
    }
    private static List<Line> Lines(Page page)
    {
        var groups = new List<List<Letter>>();
        foreach (var letter in page.Letters.OrderByDescending(l => l.StartBaseLine.Y).ThenBy(l => l.BoundingBox.Left))
        {
            var group = groups.LastOrDefault();
            if (group is null || Math.Abs(group[0].StartBaseLine.Y - letter.StartBaseLine.Y) > 2) groups.Add(group = []);
            group.Add(letter);
        }
        var lines = new List<Line>();
        foreach (var group in groups)
        {
            var segment = new List<Letter>();
            void Flush()
            {
                if (segment.Count == 0) return;
                var text = new StringBuilder(); Letter? prior = null;
                foreach (var glyph in segment)
                {
                    if (prior is not null && glyph.BoundingBox.Left - prior.BoundingBox.Right > Math.Max(1, glyph.FontSize * .2)) text.Append(' ');
                    text.Append(glyph.Value); prior = glyph;
                }
                var value = text.ToString().Trim();
                if (value.Length > 0) lines.Add(new(value, segment.Min(l => l.BoundingBox.Left), page.Height - segment.Max(l => l.BoundingBox.Top), segment.Max(l => l.BoundingBox.Right), page.Height - segment.Min(l => l.BoundingBox.Bottom)));
                segment.Clear();
            }
            foreach (var letter in group.OrderBy(l => l.BoundingBox.Left))
            {
                if (segment.Count > 0 && letter.BoundingBox.Left - segment[^1].BoundingBox.Right > Math.Max(24, letter.FontSize * 3)) Flush();
                segment.Add(letter);
            }
            Flush();
        }
        return lines.OrderBy(l => l.Top).ThenBy(l => l.Left).ToList();
    }
}
