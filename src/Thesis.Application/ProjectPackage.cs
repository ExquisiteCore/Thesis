using System.IO.Compression;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Application;

/// <summary>Portable project archives contain project inputs, never machine-specific render executables.</summary>
public static class ProjectPackage
{
    public static void Export(OpenThesisProject opened, string output)
    {
        output = Path.GetFullPath(output);
        if (FileOutputTransaction.PathComparer.Equals(output, opened.Path)) throw new ArgumentException("项目包请保存为独立的 zip 文件。");
        ContentPaths.ProtectAssets(opened.Document.Content, output);
        foreach (var input in new[] { opened.Document.TemplatePath, opened.Document.TemplateDesignPath, opened.Document.PlanPath, opened.Document.RulesPath, opened.Document.RenderConfigPath })
            if (ThesisProjectStore.Resolve(opened, input) is { } resolved && FileOutputTransaction.PathComparer.Equals(output, resolved)) throw new ArgumentException("项目包请保存在模板、方案和配置之外的独立路径。");
        var temporary = Path.Combine(Path.GetTempPath(), "ThesisPackage", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            var doc = ThesisJson.Deserialize<ThesisProject>(ThesisJson.Serialize(opened.Document));
            string Copy(string path) => MarkdownProjectFormat.CopyAsset(path, Path.Combine(temporary, "assets"));
            foreach (var block in ContentPaths.Blocks(doc.Content))
            {
                if (block.Path is not null) block.Path = Copy(block.Path);
                if (block.SourcePath is not null) block.SourcePath = Copy(block.SourcePath);
            }
            MarkdownProjectFormat.Transform(doc.Content, text => MarkdownSupport.RewriteAssets(text, doc.Content.AssetDirectory,
                path => Path.GetRelativePath(temporary, Copy(path))));
            doc.Content.AssetDirectory = temporary;
            string? Asset(string? path) => ThesisProjectStore.Resolve(opened, path) is { } resolved ? Path.GetRelativePath(temporary, Copy(resolved)) : null;
            doc.TemplatePath = Asset(doc.TemplatePath); doc.RulesPath = Asset(doc.RulesPath);
            // Source-bound plans and local render software configuration must be regenerated at the destination.
            doc.PlanPath = null; doc.RenderConfigPath = null;
            if (ThesisProjectStore.Resolve(opened, doc.TemplateDesignPath) is { } designPath)
                doc.TemplateDesignPath = Path.GetRelativePath(temporary, TemplateLibraryStore.Save(TemplateLibraryStore.Load(designPath), Path.Combine(temporary, "templates")));
            var projectPath = Path.Combine(temporary, "paper.thesis.json");
            ThesisProjectStore.Create(projectPath, doc);
            using var lease = OutputPathLease.Acquire([output]);
            using var transaction = new FileOutputTransaction();
            var staged = transaction.Stage(output); File.Delete(staged);
            ZipFile.CreateFromDirectory(temporary, staged); transaction.Commit();
        }
        finally { Directory.Delete(temporary, true); }
    }
    public static string Import(string archive, string directory)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("请选择一个新的空目录来解包项目。");
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 20000 || zip.Entries.Sum(e => e.Length) > 1024L * 1024 * 1024) throw new InvalidDataException("项目包超出 1 GB 或 20000 个文件的限制。");
        foreach (var entry in zip.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(directory, entry.FullName.Replace('\\', '/')));
            if (!destination.StartsWith(directory + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || entry.FullName.Contains(':')) throw new InvalidDataException("项目包包含目录外路径。");
        }
        if (zip.GetEntry("paper.thesis.json") is null) throw new InvalidDataException("项目包缺少 paper.thesis.json。");
        var stage = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".import-" + Guid.NewGuid().ToString("N");
        using var lease = OutputPathLease.Acquire([directory]);
        Directory.CreateDirectory(stage);
        try
        {
            zip.ExtractToDirectory(stage, false);
            var project = ThesisProjectStore.Open(Path.Combine(stage, "paper.thesis.json"));
            void CheckPath(string value)
            {
                var path = Path.GetFullPath(value, stage);
                if (!path.StartsWith(stage + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !File.Exists(path))
                    throw new InvalidDataException("项目包引用了包外或缺失的资源：" + Path.GetFileName(path));
            }
            foreach (var value in ContentPaths.Assets(project.Document.Content)) CheckPath(value);
            foreach (var value in new[] { project.Document.TemplatePath, project.Document.TemplateDesignPath, project.Document.RulesPath, project.Document.PlanPath, project.Document.RenderConfigPath }.OfType<string>()) CheckPath(value);
            if (ThesisProjectStore.Resolve(project, project.Document.TemplateDesignPath) is { } designPath)
            {
                var design = TemplateLibraryStore.Load(designPath); CheckPath(design.SourcePath);
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, false);
            Directory.Move(stage, directory);
            return Path.Combine(directory, "paper.thesis.json");
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
