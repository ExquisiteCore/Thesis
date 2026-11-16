using System.Text.RegularExpressions;
using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;
using Thesis.Session;

namespace Thesis.Cli;

internal static class ContentExtractCommand
{
    public static CliResult Execute(string[] args)
    {
        var source = ResolveSource(args);
        if (source.Error is not null)
        {
            return source.Error;
        }

        var outputPath = RequiredOption(args, "--out");
        var reportPath = OptionalOption(args, "--report");
        var profilePath = OptionalOption(args, "--profile");
        var projectRulesPath = OptionalOption(args, "--project-rules");
        var fullDocPath = Path.GetFullPath(source.DocumentPath!);
        var fullOutputPath = Path.GetFullPath(outputPath);
        var fullReportPath = string.IsNullOrWhiteSpace(reportPath) ? null : Path.GetFullPath(reportPath);

        if (SamePath(fullOutputPath, fullDocPath))
        {
            return Error("content_output_refused", "Content extract output path must not overwrite the source document.");
        }

        if (fullReportPath is not null && SamePath(fullReportPath, fullDocPath))
        {
            return Error("content_report_refused", "Content extract report path must not overwrite the source document.");
        }

        if (fullReportPath is not null && SamePath(fullReportPath, fullOutputPath))
        {
            return Error("content_report_refused", "Content extract report path must not overwrite content output.");
        }

        if ((!string.IsNullOrWhiteSpace(profilePath) && SamePath(fullOutputPath, profilePath))
            || (!string.IsNullOrWhiteSpace(projectRulesPath) && SamePath(fullOutputPath, projectRulesPath)))
        {
            return Error("content_output_refused", "Content extract output path must not overwrite input rule files.");
        }

        if (fullReportPath is not null
            && ((!string.IsNullOrWhiteSpace(profilePath) && SamePath(fullReportPath, profilePath))
                || (!string.IsNullOrWhiteSpace(projectRulesPath) && SamePath(fullReportPath, projectRulesPath))))
        {
            return Error("content_report_refused", "Content extract report path must not overwrite input rule files.");
        }

        var parent = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Error("content_output_directory_missing", $"Content extract output directory not found: {parent}");
        }

        if (fullReportPath is not null)
        {
            var reportParent = Path.GetDirectoryName(fullReportPath);
            if (string.IsNullOrWhiteSpace(reportParent) || !Directory.Exists(reportParent))
            {
                return Error("content_report_directory_missing", $"Content extract report directory not found: {reportParent}");
            }
        }

        TemplateProfile? profile = null;
        if (!string.IsNullOrWhiteSpace(profilePath))
        {
            if (!TryReadProfile(profilePath, out profile, out var profileError))
            {
                return profileError!;
            }
        }

        ProjectRules? projectRules = null;
        if (!string.IsNullOrWhiteSpace(projectRulesPath))
        {
            if (!TryReadProjectRules(projectRulesPath, out projectRules, out var projectRulesError))
            {
                return projectRulesError!;
            }
        }

        if (!OpenXmlDocumentInspector.TryInspect(fullDocPath, out var map, out var diagnostic) || map is null)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullDocPath,
                OutputPath = fullOutputPath,
                Diagnostics = diagnostic is null ? [] : [diagnostic]
            };
        }

        try
        {
            using var transaction = new Thesis.Core.FileOutputTransaction();
            var contentTempPath = transaction.Stage(fullOutputPath);
            var reportTempPath = fullReportPath is null ? null : transaction.Stage(fullReportPath);
            var extracted = ContentExtractBuilder.Build(map, profile, projectRules, profilePath, projectRulesPath);
            extracted.Report.OutputPath = fullOutputPath;
            var native = Thesis.Core.ContentPaths.Blocks(extracted.Content).Where(b => b.Type == "native").ToList();
            if (native.Count > 0)
            {
                var assetDirectory = fullOutputPath + ".assets";
                Directory.CreateDirectory(assetDirectory);
                var asset = Path.Combine(assetDirectory, "source-" + native[0].SourceSha256!.ToLowerInvariant() + ".docx");
                File.Copy(fullDocPath, transaction.Stage(asset));
                foreach (var block in native) block.SourcePath = Path.GetRelativePath(parent!, asset);
            }
            File.WriteAllText(contentTempPath, ThesisJson.Serialize(extracted.Content));

            if (fullReportPath is not null && reportTempPath is not null)
            {
                File.WriteAllText(reportTempPath, ThesisJson.Serialize(extracted.Report));
            }

            transaction.Commit();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CliResult
            {
                Status = "error",
                Document = fullDocPath,
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "content_extract_failed",
                        Message = $"Content extract failed: {ex.Message}",
                        Path = fullDocPath
                    }
                ]
            };
        }

        return new CliResult
        {
            Status = "success",
            Document = fullDocPath,
            OutputPath = fullOutputPath,
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "info",
                    Code = "content_extracted",
                    Message = "Document content was extracted into thesisContent JSON.",
                    Path = fullOutputPath
                }
            ]
        };
    }

    private static (string? DocumentPath, CliResult? Error) ResolveSource(string[] args)
    {
        var doc = OptionalOption(args, "--doc");
        var workspace = OptionalOption(args, "--workspace");
        if (doc is not null && workspace is not null)
        {
            return (null, Error("content_source_ambiguous", "Specify either --doc or --workspace, not both."));
        }

        if (doc is not null)
        {
            return (doc, null);
        }

        if (workspace is null)
        {
            return (null, Error("content_source_missing", "Specify either --doc or --workspace."));
        }

        return (SessionPaths.FromWorkspace(workspace).WorkingDocument, null);
    }

    private static bool TryReadProfile(string path, out TemplateProfile? profile, out CliResult? error)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            profile = ThesisJson.Deserialize<TemplateProfile>(File.ReadAllText(fullPath));
            if (!string.Equals(profile.ProfileKind, "templateProfile", StringComparison.OrdinalIgnoreCase))
            {
                error = Error("content_profile_invalid", "Profile JSON must have profileKind 'templateProfile'.");
                error.Diagnostics[0].Path = fullPath;
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            profile = null;
            error = Error("content_profile_invalid", $"Profile JSON could not be read: {ex.Message}");
            error.Diagnostics[0].Path = fullPath;
            return false;
        }
    }

    private static bool TryReadProjectRules(string path, out ProjectRules? rules, out CliResult? error)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            rules = ThesisJson.Deserialize<ProjectRules>(File.ReadAllText(fullPath));
            if (!string.Equals(rules.RulesKind, "projectRules", StringComparison.OrdinalIgnoreCase))
            {
                error = Error("content_project_rules_invalid", "Project rules JSON must have rulesKind 'projectRules'.");
                error.Diagnostics[0].Path = fullPath;
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            rules = null;
            error = Error("content_project_rules_invalid", $"Project rules JSON could not be read: {ex.Message}");
            error.Diagnostics[0].Path = fullPath;
            return false;
        }
    }

    private static string RequiredOption(string[] args, string name)
    {
        return OptionalOption(args, name)
            ?? throw new CliException("missing_option", $"Missing required option: {name}");
    }

    private static string? OptionalOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static CliResult Error(string code, string message)
    {
        return new CliResult
        {
            Status = "error",
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = "error",
                    Code = code,
                    Message = message
                }
            ]
        };
    }
}
