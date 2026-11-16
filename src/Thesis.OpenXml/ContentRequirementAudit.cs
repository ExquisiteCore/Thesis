using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static class ContentRequirementAudit
{
    public static List<RequirementResult> Check(string path, IReadOnlyList<ContentRequirement> requirements)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
            .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim()).ToArray();
        var results = new List<RequirementResult>();
        var ids = new HashSet<string>();
        foreach (var rule in requirements)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id) || string.IsNullOrWhiteSpace(rule.Evidence))
                throw new ArgumentException("Requirements need unique IDs and template evidence.");
            if (rule.Minimum is < 0 || rule.Maximum is < 0 || rule.Minimum > rule.Maximum)
                throw new ArgumentException($"Invalid bounds for requirement {rule.Id}.");
            var result = new RequirementResult { Id = rule.Id, Evidence = rule.Evidence };
            try
            {
                var start = rule.StartAnchor is null ? 0 : Find(paragraphs, rule.StartAnchor) + 1;
                var end = rule.EndAnchor is null ? paragraphs.Length : Find(paragraphs, rule.EndAnchor);
                if (end < start) throw new InvalidDataException("Requirement anchors are reversed.");
                var range = paragraphs[start..end];
                int actual;
                switch (rule.Kind)
                {
                    case "sectionCharacters": actual = Regex.Replace(string.Concat(range), @"\s+", "").Length; break;
                    case "journalReferences": actual = range.Count(p => Regex.IsMatch(p, @"\[J(?:/OL)?\]", RegexOptions.IgnoreCase)); break;
                    case "requiredText":
                        if (string.IsNullOrWhiteSpace(rule.Text)) throw new ArgumentException($"Requirement {rule.Id} needs text.");
                        actual = string.Join("\n", range).Contains(rule.Text, StringComparison.Ordinal) ? 1 : 0; break;
                    case "keywords":
                        var lines = range.Where(p => Regex.IsMatch(p, @"^(关键词|关键字|key\s*words?)\s*[:：]", RegexOptions.IgnoreCase)).ToArray();
                        if (lines.Length != 1) throw new InvalidDataException("Keyword rule needs exactly one matching keyword line; narrow its anchors.");
                        var content = Regex.Replace(lines[0], @"^.*?[:：]\s*", "");
                        actual = Regex.Split(content, @"[;；,，、]+").Count(s => !string.IsNullOrWhiteSpace(s)); break;
                    default: throw new ArgumentException($"Unknown requirement kind: {rule.Kind}");
                }
                result.Actual = actual;
                result.Passed = actual >= (rule.Minimum ?? (rule.Kind == "requiredText" ? 1 : 0)) && actual <= (rule.Maximum ?? int.MaxValue);
                result.Message = $"{rule.Kind}: actual={actual}, minimum={rule.Minimum}, maximum={rule.Maximum}.";
            }
            catch (InvalidDataException ex) { result.Message = ex.Message; }
            results.Add(result);
        }
        return results;
    }

    private static int Find(string[] paragraphs, string anchor)
    {
        var found = paragraphs.Select((p, i) => (p, i)).Where(x => x.p == anchor.Trim()).ToArray();
        if (found.Length != 1) throw new InvalidDataException($"Requirement anchor must match once: {anchor}");
        return found[0].i;
    }
}
