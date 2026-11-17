using System.Security.Cryptography;
using System.Text;
using Thesis.Core;
using Thesis.Schema;

namespace Thesis.Application;

/// <summary>Keep the exact reviewed source bytes stable until content, assets or template rules change.</summary>
internal static class ProjectSourceCache
{
    public static string Prepare(ThesisContent content, string template, string? rules, string projectPath, string work)
    {
        var contentJson = ThesisJson.Serialize(content);
        var inputs = new[] { template, rules }.OfType<string>()
            .Concat(ContentPaths.Assets(content)).Distinct().Order().ToArray();
        var fingerprints = inputs.Select(p => new { Path = Path.GetFullPath(p), Hash = HashFile(p) }).ToArray();
        var identity = ThesisJson.Serialize(new { Version = 1,
            Generator = typeof(Thesis.OpenXml.ThesisDocumentGenerator).Assembly.ManifestModule.ModuleVersionId,
            Preparation = typeof(CompositionSourceBuilder).Assembly.ManifestModule.ModuleVersionId,
            Content = contentJson, Inputs = fingerprints });
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var directory = Path.Combine(Path.GetDirectoryName(projectPath)!, ".thesis", "sources", key);
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.docx");
        var stamp = Path.Combine(directory, "sha256.txt");
        using var lease = OutputPathLease.Acquire([source]);
        if (File.Exists(source) && File.Exists(stamp) && File.ReadAllText(stamp) == HashFile(source)) return source;
        var contentPath = Path.Combine(work, "content.json");
        File.WriteAllText(contentPath, contentJson);
        var generated = CompositionSourceBuilder.FromContent(contentPath, template, rules, work);
        if (fingerprints.Any(p => p.Hash != HashFile(p.Path)))
            throw new IOException("正文、图片或模板在生成过程中发生变化，请重新生成。");
        using var transaction = new FileOutputTransaction();
        File.Copy(generated, transaction.Stage(source));
        File.WriteAllText(transaction.Stage(stamp), HashFile(generated));
        transaction.Commit();
        return source;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
