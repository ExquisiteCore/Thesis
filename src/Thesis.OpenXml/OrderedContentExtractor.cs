using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static class OrderedContentExtractor
{
    public static List<ContentExtractFinding> Populate(ThesisContent content, DocumentMap map)
    {
        var findings = new List<ContentExtractFinding>();
        if (!File.Exists(map.Path)) return findings;
        using var stream = File.OpenRead(map.Path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        using var doc = WordprocessingDocument.Open(map.Path, false);
        var blocks = doc.MainDocumentPart!.Document!.Body!.ChildElements;
        var cursor = -1;
        for (var c = 0; c < content.Chapters.Count; c++)
        {
            var chapter = content.Chapters[c];
            var heading = map.Paragraphs.FirstOrDefault(p => p.BodyElementIndex > cursor && p.Text.Trim() == chapter.Title);
            if (heading is null) continue;
            chapter.OrderedContentIncludesSections = true;
            cursor = heading.BodyElementIndex;
            var nextTitle = c + 1 < content.Chapters.Count ? content.Chapters[c + 1].Title : null;
            var end = map.Paragraphs.FirstOrDefault(p => p.BodyElementIndex > cursor && (p.Text.Trim() == nextTitle
                || ThesisTextHeuristics.IsReferencesHeading(p.Text) || ThesisTextHeuristics.IsAcknowledgementsHeading(p.Text) || ThesisTextHeuristics.IsAppendixHeading(p.Text)))?.BodyElementIndex ?? blocks.Count;
            for (var i = cursor + 1; i < end; i++)
            {
                var item = blocks[i];
                if (item is SectionProperties) continue;
                var text = string.Concat(item.Descendants<Text>().Select(t => t.Text));
                var p = map.Paragraphs.FirstOrDefault(p => p.BodyElementIndex == i);
                var role = item is Table ? "table" : p?.OutlineLevel is 1 ? "heading2" : p?.OutlineLevel is 2 ? "heading3"
                    : Regex.IsMatch(text.Trim(), @"^\d+\.\d+\.\d+\s") ? "heading3" : Regex.IsMatch(text.Trim(), @"^\d+\.\d+\s") ? "heading2"
                    : ThesisTextHeuristics.IsTableCaption(text) ? "tableCaption" : ThesisTextHeuristics.IsFigureCaption(text) ? "figureCaption" : "body";
                chapter.Blocks.Add(new ThesisContentBlock { Type = "native", Text = text, SourcePath = Path.GetFullPath(map.Path), SourceSha256 = hash, SourceBodyElementIndex = i, Role = role });
                foreach (var feature in NativeContentImporter.Unsupported(item, doc.MainDocumentPart)) findings.Add(new ContentExtractFinding
                {
                    Severity = "error", Code = "native_feature_unsupported", Message = $"Block {i}: {feature} needs explicit migration.", Path = $"body[{i}]"
                });
            }
        }
        return findings;
    }
}
