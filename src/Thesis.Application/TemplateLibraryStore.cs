using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Application;

public static class TemplateLibraryStore
{
    public static string Materialize(TemplateDesign design, string directory)
    {
        if (!design.ApplyMarginsToAllSections) return design.SourcePath;
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ThesisJson.Serialize(design)))).ToLowerInvariant();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, key + ".docx");
        var stamp = path + ".sha256";
        using var lease = OutputPathLease.Acquire([path, stamp]);
        if (!File.Exists(path) || !File.Exists(stamp) || TemplatePlanPreflight.Fingerprint(path) != File.ReadAllText(stamp))
        {
            using var transaction = new FileOutputTransaction();
            var staged = transaction.Stage(path); File.Copy(design.SourcePath, staged, true);
            TemplateDesigner.ApplyMargins(staged, design);
            File.WriteAllText(transaction.Stage(stamp), TemplatePlanPreflight.Fingerprint(staged)); transaction.Commit();
        }
        return path;
    }
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thesis", "Templates");
    public static TemplateDesign Load(string path)
    {
        var design = ThesisJson.DeserializeStrict<TemplateDesign>(File.ReadAllText(path));
        TemplateDesigner.Validate(design);
        design.SourcePath = Path.GetFullPath(design.SourcePath, Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!File.Exists(design.SourcePath) || TemplatePlanPreflight.Fingerprint(design.SourcePath) != design.SourceSha256)
            throw new InvalidDataException("模板源文件与设计记录不一致，请重新导入并核对。");
        return design;
    }
    public static string Save(TemplateDesign design, string directory, bool immutable = false)
    {
        TemplateDesigner.Validate(design);
        if (TemplatePlanPreflight.Fingerprint(design.SourcePath) != design.SourceSha256) throw new IOException("模板文件已变化，请重新识别。");
        var target = Path.Combine(directory, design.Id);
        Directory.CreateDirectory(target);
        var source = MarkdownProjectFormat.CopyAsset(design.SourcePath, target);
        var copy = ThesisJson.Deserialize<TemplateDesign>(ThesisJson.Serialize(design));
        copy.SourcePath = Path.GetRelativePath(target, source);
        var json = ThesisJson.Serialize(copy);
        var version = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var path = Path.Combine(target, immutable ? "version-" + version + ".json" : "template.json");
        using var lease = OutputPathLease.Acquire([path]);
        using var transaction = new FileOutputTransaction();
        File.WriteAllText(transaction.Stage(path), json);
        transaction.Commit();
        return path;
    }
    public static IReadOnlyList<(string Path, string Name)> List() => Directory.Exists(DirectoryPath)
        ? Directory.EnumerateFiles(DirectoryPath, "template.json", SearchOption.AllDirectories).Select(path =>
        {
            try { return (Path: path, Name: Load(path).Name); }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException) { return (Path: path, Name: "[待修复] " + Path.GetFileName(Path.GetDirectoryName(path))); }
        }).ToList() : [];
    public static TemplateDesign Default(string? userDataDirectory = null)
    {
        var directory = Path.Combine(userDataDirectory ?? DirectoryPath, "builtin"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "default.docx");
        using var lease = OutputPathLease.Acquire([path]);
        if (!File.Exists(path))
        {
            using var transaction = new FileOutputTransaction();
            TemplateDesigner.CreateDefault(transaction.Stage(path)); transaction.Commit();
        }
        var result = TemplateDesigner.Inspect(path); result.Name = "通用本科论文 · A4";
        result.ReviewNotes.Add("通用模板只作为起点；请依据学校规定调整封面、字数、目录与页码。");
        return result;
    }
}
