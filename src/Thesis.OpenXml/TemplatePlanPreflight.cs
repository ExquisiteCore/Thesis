using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

/// <summary>Validates and resolves a private copy, so reusable caller plans stay unchanged.</summary>
public static class TemplatePlanPreflight
{
    public static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static (TemplateApplicationPlan Plan, TemplatePreflightReport Report) Check(string source, string template, TemplateApplicationPlan original, bool rejectUnsupported = true)
    {
        var plan = ThesisJson.Deserialize<TemplateApplicationPlan>(ThesisJson.Serialize(original));
        var report = new TemplatePreflightReport { SourceSha256 = Fingerprint(source), TemplateSha256 = Fingerprint(template) };
        if (plan.SourceSha256 is not null && !string.Equals(plan.SourceSha256, report.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source fingerprint changed. Review and bind the plan again.");
        if (plan.TemplateSha256 is not null && !string.Equals(plan.TemplateSha256, report.TemplateSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Template fingerprint changed. Review and bind the plan again.");
        report.FingerprintsPinned = plan.SourceSha256 is not null && plan.TemplateSha256 is not null;
        if (!report.FingerprintsPinned) report.Warnings.Add("Legacy unpinned plan: use template-plan bind after review.");
        using var sourceDoc = WordprocessingDocument.Open(source, false);
        using var templateDoc = WordprocessingDocument.Open(template, false);
        var sourceBlocks = sourceDoc.MainDocumentPart?.Document?.Body?.ChildElements.ToArray() ?? throw new InvalidDataException("Source has no body.");
        var templateBlocks = templateDoc.MainDocumentPart?.Document?.Body?.ChildElements.ToArray() ?? throw new InvalidDataException("Template has no body.");
        var sections = templateDoc.MainDocumentPart!.Document.Body!.Descendants<SectionProperties>().Count();
        if (plan.SchemaVersion != "1.0" || plan.Blocks is null || plan.Roles is null || plan.SkippedSourceRanges is null
            || plan.PendingReviews is null || plan.Requirements is null || plan.SourceTextReplacements is null || plan.TemplateTextReplacements is null || plan.TocPages is null)
            throw new ArgumentException("Invalid version 1.0 plan collections.");
        foreach (var (name, role) in plan.Roles)
        {
            if (role is null) throw new ArgumentException($"Null role format: {name}");
            if (role.TemplateParagraphIndex is int sample && (sample < 0 || sample >= templateBlocks.Length || templateBlocks[sample] is not Paragraph))
                throw new ArgumentException($"Role {name} must reference a template body paragraph.");
        }
        var covered = new HashSet<int>();
        if (plan.TemplateTableIndex is int tableSample && (tableSample < 0 || tableSample >= templateBlocks.Length || templateBlocks[tableSample] is not Table))
            throw new ArgumentException("Template table sample must reference a body table.");
        foreach (var (element, index) in sourceBlocks.Select((element, index) => (element, index)))
        {
            if (element is SectionProperties) continue;
            var unsupported = NativeContentImporter.Unsupported(element, sourceDoc.MainDocumentPart);
            if (unsupported.Count > 0) report.UnsupportedSourceBlocks.Add(index);
            report.SourceBlocks.Add(new SourceBlockDescriptor { Index = index, Kind = element.LocalName,
                UnsupportedFeatures = unsupported,
                Text = Text(element), Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(element.OuterXml))).ToLowerInvariant() });
        }
        foreach (var block in plan.Blocks)
        {
            if (block is null) throw new ArgumentException("Plan contains a null block.");
            if (block.Type is not ("sourceRange" or "templateRange" or "paragraph" or "table" or "image" or "toc" or "section"))
                throw new ArgumentException($"Unknown plan block type: {block.Type}");
            if (block.ColumnCount is < 1 or > 45 || block.SectionBreakType is not (null or "continuous" or "nextPage" or "nextColumn" or "evenPage" or "oddPage"))
                throw new ArgumentException("Invalid section columns or break type.");
            if (block.ContinuePageNumbering && block.PageNumberStart.HasValue)
                throw new ArgumentException("A section must either continue numbering or specify a starting page.");
            if (block.Role is not null && !plan.Roles.ContainsKey(block.Role)) throw new ArgumentException($"Unknown role: {block.Role}");
            if (block.Role is not null && plan.Roles[block.Role] is null) throw new ArgumentException($"Null role format: {block.Role}");
            if (block.ParagraphTexts is null || block.CellTexts is null) throw new ArgumentException("Range override maps must be objects.");
            if (block.Type is "sourceRange" or "templateRange")
            {
                var candidates = block.Type == "sourceRange" ? sourceBlocks : templateBlocks;
                if (block.StartAnchor is not null) block.Start = Find(candidates, block.StartAnchor);
                if (block.EndAnchor is not null) block.End = Find(candidates, block.EndAnchor);
                if (block.Start < 0 || block.End <= block.Start || block.End > candidates.Length) throw new ArgumentException($"Invalid {block.Type} [{block.Start}, {block.End}).");
                for (var index = block.Start; index < block.End; index++)
                {
                    if (block.Type == "sourceRange" && candidates[index] is not SectionProperties && !covered.Add(index))
                        throw new ArgumentException($"Source block {index} was included more than once.");
                    if (rejectUnsupported && block.Type == "sourceRange" && NativeContentImporter.Unsupported(candidates[index], sourceDoc.MainDocumentPart) is { Count: > 0 } features)
                        throw new InvalidDataException($"Source block {index} needs explicit migration for: {string.Join(", ", features)}");
                    if (block.ParagraphTexts.ContainsKey(index) && candidates[index] is not Paragraph)
                        throw new ArgumentException("Paragraph override addresses a non-paragraph.");
                }
                if (block.ParagraphTexts.Keys.Any(i => i < block.Start || i >= block.End)) throw new ArgumentException("Paragraph override is outside its range.");
                if (block.CellTexts.Count > 0)
                {
                    var tables = candidates[block.Start..block.End].OfType<Table>().ToArray();
                    if (tables.Length != 1) throw new ArgumentException("Cell overrides require exactly one table in the range.");
                    foreach (var address in block.CellTexts.Keys)
                    {
                        var parts = address.Split(',');
                        if (parts.Length != 2 || !int.TryParse(parts[0], out var r) || !int.TryParse(parts[1], out var c)
                            || r < 0 || c < 0 || r >= tables[0].Elements<TableRow>().Count()
                            || c >= tables[0].Elements<TableRow>().ElementAt(r).Elements<TableCell>().Count())
                            throw new ArgumentException($"Cell override outside table: {address}");
                    }
                }
            }
            else if (block.StartAnchor is not null || block.EndAnchor is not null) throw new ArgumentException("Anchors require a range block.");
            if (block.Type == "section" && (block.SectionTemplateIndex < 0 || block.SectionTemplateIndex >= sections))
                throw new ArgumentException("Section template index outside template.");
            if (block.Type == "image" && (block.ImageWidthEmu <= 0 || block.ImageHeightEmu <= 0 || !File.Exists(block.ImagePath)))
                throw new ArgumentException("Image requires an existing file and positive dimensions.");
            if (block.Type == "table" && (block.Rows is null || block.Rows.Count == 0 || block.Rows[0] is null || block.Rows.Any(r => r is null || r.Count != block.Rows[0].Count)
                || block.Rows[0].Count == 0 || block.ColumnWidths is null || block.ColumnWidths.Count != block.Rows[0].Count || block.ColumnWidths.Any(w => w <= 0)))
                throw new ArgumentException("Table rows and positive column widths must have matching dimensions.");
        }
        foreach (var skipped in plan.SkippedSourceRanges)
        {
            if (skipped is null || skipped.Start < 0 || skipped.End <= skipped.Start || skipped.End > sourceBlocks.Length || string.IsNullOrWhiteSpace(skipped.Reason))
                throw new ArgumentException("Skipped source ranges require valid indexes and a review reason.");
            for (var i = skipped.Start; i < skipped.End; i++)
                if (sourceBlocks[i] is not SectionProperties && !covered.Add(i)) throw new ArgumentException($"Skipped source block {i} overlaps another range.");
        }
        report.UnmappedSourceBlocks = report.SourceBlocks.Select(b => b.Index).Where(i => !covered.Contains(i)).ToList();
        if (plan.RequireSourceCoverage && report.UnmappedSourceBlocks.Count > 0)
            throw new ArgumentException($"Source coverage is incomplete: {string.Join(",", report.UnmappedSourceBlocks)}");
        if (report.UnmappedSourceBlocks.Count > 0) report.Warnings.Add($"{report.UnmappedSourceBlocks.Count} source blocks are neither copied nor explicitly skipped.");
        if (report.UnsupportedSourceBlocks.Count > 0) report.Warnings.Add($"{report.UnsupportedSourceBlocks.Count} source blocks contain features requiring an explicit migration mapping.");
        return (plan, report);
    }

    private static int Find(OpenXmlElement[] elements, string anchor)
    {
        var matches = elements.Select((e, i) => (e, i)).Where(x => Text(x.e) == anchor).Select(x => x.i).ToArray();
        if (matches.Length != 1) throw new ArgumentException($"Anchor must match exactly one body block: '{anchor}' ({matches.Length} matches).");
        return matches[0];
    }
    private static string Text(OpenXmlElement element) => string.Concat(element.Descendants<Text>().Select(t => t.Text)).Trim();
}
