using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Profile;
using Thesis.Schema;

namespace Thesis.Application;

/// <summary>Structured text and an existing DOCX converge before the same template workflow.</summary>
public static class CompositionSourceBuilder
{
    public static string FromContent(string contentPath, string templatePath, string? projectRulesPath, string workingDirectory)
    {
        var content = ThesisJson.Deserialize<ThesisContent>(File.ReadAllText(contentPath));
        ContentNormalizer.Normalize(content);
        ContentPaths.Resolve(content, contentPath);
        if (!OpenXmlDocumentInspector.TryInspect(templatePath, out var map, out var diagnostic) || map is null)
            throw new InvalidDataException(diagnostic?.Message ?? "Template inspection failed.");
        var profile = TemplateProfileBuilder.Build(map, "doc");
        if (projectRulesPath is not null) profile = ProjectRulesMerger.Merge(profile, ThesisJson.Deserialize<ProjectRules>(File.ReadAllText(projectRulesPath)));
        Directory.CreateDirectory(workingDirectory);
        var output = Path.Combine(workingDirectory, "source-" + Guid.NewGuid().ToString("N") + ".docx");
        ThesisDocumentGenerator.Generate(content, profile, output);
        return output;
    }
}
