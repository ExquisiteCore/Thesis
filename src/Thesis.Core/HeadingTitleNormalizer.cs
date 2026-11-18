using System.Text.RegularExpressions;

namespace Thesis.Core;

/// <summary>Reads printed heading numbers independently of Word's style names.</summary>
public static class HeadingTitleNormalizer
{
    private static readonly Regex Chapter = new(@"^第\s*(?<number>[一二三四五六七八九十百千万零〇两0-9Xx]+)\s*章\s*(?<title>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Section = new(@"^\d{1,2}[\.．]\d{1,2}(?:[\.．]\d{1,2})?\s+\S", RegexOptions.CultureInvariant);

    public static bool TryParseChapter(string? text, out string number, out string title)
    {
        var match = Chapter.Match(text?.Trim() ?? "");
        number = match.Groups["number"].Value;
        title = match.Groups["title"].Value.Trim();
        return match.Success;
    }

    public static bool IsSection(string? text) => Section.IsMatch(text?.Trim() ?? "");
}
