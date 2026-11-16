using Thesis.Schema;

namespace Thesis.Core;

public static class ContentNormalizer
{
    public static void Normalize(ThesisContent content)
    {
        content.KeywordsZh ??= [];
        content.KeywordsEn ??= [];
        content.Chapters ??= [];
        content.References ??= [];
        foreach (var chapter in content.Chapters)
        {
            chapter.Paragraphs ??= [];
            chapter.Blocks ??= [];
            chapter.Sections ??= [];
            chapter.Tables ??= [];
            foreach (var section in chapter.Sections)
            {
                section.Paragraphs ??= [];
                section.Blocks ??= [];
                section.Tables ??= [];
            }
            foreach (var table in chapter.Tables.Concat(chapter.Sections.SelectMany(s => s.Tables)))
            {
                table.Headers ??= [];
                table.Rows ??= [];
            }
        }
    }
}
