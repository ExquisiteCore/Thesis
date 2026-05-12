namespace Thesis.Schema;

public sealed class ThesisContent
{
    public string SchemaVersion { get; set; } = "1.0";

    public string DocumentKind { get; set; } = "thesisContent";

    public string Title { get; set; } = "";

    public string? Author { get; set; }

    public string? AbstractZh { get; set; }

    public List<string> KeywordsZh { get; set; } = [];

    public string? AbstractEn { get; set; }

    public List<string> KeywordsEn { get; set; } = [];

    public List<ThesisChapterContent> Chapters { get; set; } = [];

    public List<string> References { get; set; } = [];

    public string? Acknowledgements { get; set; }
}

public sealed class ThesisChapterContent
{
    public string Title { get; set; } = "";

    public List<string> Paragraphs { get; set; } = [];

    public List<ThesisSectionContent> Sections { get; set; } = [];

    public List<ThesisTableContent> Tables { get; set; } = [];
}

public sealed class ThesisSectionContent
{
    public string Title { get; set; } = "";

    public List<string> Paragraphs { get; set; } = [];

    public List<ThesisTableContent> Tables { get; set; } = [];
}

public sealed class ThesisTableContent
{
    public string? Caption { get; set; }

    public List<string> Headers { get; set; } = [];

    public List<List<string>> Rows { get; set; } = [];
}
