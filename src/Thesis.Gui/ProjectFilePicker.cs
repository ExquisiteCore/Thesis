using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;

namespace Thesis.Gui;

internal static class ProjectFilePicker
{
    public static FileSavePicker Save(Window owner, string label, string extension, string suggestedName = "我的论文")
    {
        // Keep the compound project suffix in the name; the picker filter uses .json.
        var suffix = Path.GetExtension("file" + extension);
        var stem = extension[..^suffix.Length];
        var picker = new FileSavePicker(owner.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName + stem
        };
        picker.FileTypeChoices.Add(label, [suffix]);
        picker.DefaultFileExtension = suffix;
        return picker;
    }

    public static string FileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Concat(title.Trim().Select(c => invalid.Contains(c) ? '_' : c)).TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(name)) name = "未命名论文";
        if (name.Length > 80) name = name[..80];
        return name;
    }
}
