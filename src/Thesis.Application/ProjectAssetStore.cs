using Thesis.Schema;

namespace Thesis.Application;

/// <summary>Content-addressed project assets, shared by desktop and command-line clients.</summary>
public static class ProjectAssetStore
{
    public static ThesisContentBlock ImportImage(string sourcePath, string projectPath, string? caption = null)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff")) throw new InvalidDataException("请选择 PNG、JPEG、GIF、BMP 或 TIFF 图片。");
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var path = MarkdownProjectFormat.CopyAsset(Path.GetFullPath(sourcePath), Path.Combine(directory, "assets"));
        return new() { Type = "image", Path = Path.GetRelativePath(directory, path), Caption = caption, AltText = caption };
    }
}
