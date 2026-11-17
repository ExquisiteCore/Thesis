namespace Thesis.Schema;

public sealed class ThesisContent
{
    public string SchemaVersion { get; set; } = "1.0";

    public string DocumentKind { get; set; } = "thesisContent";

    public string Title { get; set; } = "";
    public string AssetDirectory { get; set; } = ".";

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
    public string? Markdown { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool OrderedContentIncludesSections { get; set; }
    public string Title { get; set; } = "";

    public List<string> Paragraphs { get; set; } = [];

    public List<ThesisContentBlock> Blocks { get; set; } = [];

    public List<ThesisSectionContent> Sections { get; set; } = [];

    public List<ThesisTableContent> Tables { get; set; } = [];
}

public sealed class ThesisSectionContent
{
    public string? Markdown { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";

    public List<string> Paragraphs { get; set; } = [];

    public List<ThesisContentBlock> Blocks { get; set; } = [];

    public List<ThesisTableContent> Tables { get; set; } = [];
}

public sealed class ThesisContentBlock
{
    public string Type { get; set; } = "paragraph";

    public string? Text { get; set; }

    public string? ParagraphStyle { get; set; }

    public string? ListKind { get; set; }

    public int? ListLevel { get; set; }

    public List<ThesisTextRun> Runs { get; set; } = [];

    public string? Latex { get; set; }

    public bool DisplayEquation { get; set; }

    public string? Path { get; set; }

    public string? Caption { get; set; }

    public string? AltText { get; set; }

    public int? WidthEmu { get; set; }

    public int? HeightEmu { get; set; }

    public ThesisTableContent? Table { get; set; }

    /// <summary>Fingerprint-pinned source for a native block, resolved relative to content.json.</summary>
    public string? SourcePath { get; set; }
    public string? SourceSha256 { get; set; }
    public int? SourceBodyElementIndex { get; set; }
    public string? Role { get; set; }
}

public sealed class ThesisTextRun
{
    public string Text { get; set; } = "";

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public bool Underline { get; set; }

    public bool Strike { get; set; }

    public bool Code { get; set; }

    public bool Superscript { get; set; }

    public bool Subscript { get; set; }
}

public sealed class ThesisTableContent
{
    public string? Caption { get; set; }

    public List<string> Headers { get; set; } = [];

    public List<List<string>> Rows { get; set; } = [];
}
