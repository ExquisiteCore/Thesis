using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Application;

public static class DocumentPreviewService
{
    public static DocumentPreview Content(ThesisContent content, TemplateDesign? design = null, CancellationToken cancellationToken = default, bool chaptersOnly = false)
    {
        var profile = new TemplateProfile();
        if (design is not null)
            profile.StyleRoles = design.Roles.Select(r => new ProfileStyleRole { Role = r.Role, Confidence = r.Confidence,
                Format = new() { Alignment = r.Alignment, FirstLineIndentTwips = (int)(r.FirstLineCharacters * r.Size * 20),
                    LineSpacing = ((int)(r.LineSpacing * 240)).ToString(), LineSpacingRule = "auto",
                    RunFormat = new() { EastAsiaFont = r.Font, AsciiFont = r.LatinFont, HighAnsiFont = r.LatinFont, Bold = r.Bold, FontSizeHalfPoints = ((int)(r.Size * 2)).ToString() } } }).ToList();
        var directory = Path.Combine(Path.GetTempPath(), "ThesisPreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, "preview.docx");
            ThesisDocumentGenerator.Generate(content, profile, path, chaptersOnly);
            return DocumentPreviewReader.Read(path, cancellationToken);
        }
        finally { Directory.Delete(directory, true); }
    }
}
