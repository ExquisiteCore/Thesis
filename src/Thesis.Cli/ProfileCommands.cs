using Thesis.Schema;
using Thesis.Profile;
using Thesis.OpenXml;
using Thesis.Session;
using static Thesis.Cli.CliInputReader;
using static Thesis.Cli.CliCommandSupport;

namespace Thesis.Cli;

internal static class ProfileCommands
{
    internal static CliResult ExtractProfile(string[] args)
    {
        var outputPath = RequiredOption(args, "--out");
        var sourceType = "doc";
        var doc = OptionalOption(args, "--doc");
        var workspaceOption = OptionalOption(args, "--workspace");
        SessionPaths? workspacePaths = null;

        if (doc is not null && workspaceOption is not null)
        {
            return Error("profile_source_ambiguous", "Specify either --doc or --workspace, not both.");
        }

        if (doc is null && workspaceOption is null)
        {
            return Error("profile_source_missing", "Specify either --doc or --workspace.");
        }

        if (doc is null)
        {
            workspacePaths = SessionPaths.FromWorkspace(workspaceOption!);
            var inspect = SessionLifecycle.Inspect(workspaceOption!);
            if (!string.Equals(inspect.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return inspect;
            }

            doc = inspect.Document
                ?? throw new CliException("working_doc_missing", "Workspace inspect did not return a working document.");
            sourceType = "workspace";
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Error("profile_output_directory_missing", $"Profile output directory not found: {parent}");
        }

        var fullDocPath = Path.GetFullPath(doc);
        if (IsProfileOutputRefused(fullOutputPath, fullDocPath, workspacePaths))
        {
            return Error("profile_output_refused", "Profile output path must not overwrite the source document or workspace state.");
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

        var profile = TemplateProfileBuilder.Build(map, sourceType);
        File.WriteAllText(fullOutputPath, ThesisJson.Serialize(profile));

        return new CliResult
        {
            Status = "success",
            Document = map.Path,
            OutputPath = fullOutputPath,
            ProfileExplanation = ProfileExplanationBuilder.Build(profile, fullOutputPath)
        };
    }

    internal static CliResult ExplainProfile(string[] args)
    {
        var profilePath = OptionalOption(args, "--profile");
        var workspace = OptionalOption(args, "--workspace");
        if (profilePath is not null && workspace is not null)
        {
            return Error("profile_source_ambiguous", "Specify either --profile or --workspace, not both.");
        }

        if (profilePath is null && workspace is null)
        {
            return Error("profile_source_missing", "Specify either --profile or --workspace.");
        }

        profilePath ??= SessionPaths.FromWorkspace(workspace!).ProfileJson;
        var fullProfilePath = Path.GetFullPath(profilePath);
        TemplateProfile profile;
        try
        {
            profile = ThesisJson.Deserialize<TemplateProfile>(File.ReadAllText(fullProfilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new CliResult
            {
                Status = "error",
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "profile_invalid",
                        Message = $"Profile JSON could not be read: {ex.Message}",
                        Path = fullProfilePath
                    }
                ]
            };
        }

        profile.StyleRoles ??= [];
        profile.Diagnostics ??= [];
        profile.TableArchetypes ??= [];
        profile.TablePolicy ??= new ProfileTablePolicy();
        profile.SourceEvidence ??= new ProfileSourceEvidence();

        return new CliResult
        {
            Status = "success",
            ProfileExplanation = ProfileExplanationBuilder.Build(profile, fullProfilePath)
        };
    }

    internal static CliResult DiffProfiles(string[] args)
    {
        var leftPath = RequiredOption(args, "--left");
        var rightPath = RequiredOption(args, "--right");
        if (!TryReadProfile(leftPath, out var left, out var leftError))
        {
            return leftError!;
        }

        if (!TryReadProfile(rightPath, out var right, out var rightError))
        {
            return rightError!;
        }

        NormalizeProfile(left!);
        NormalizeProfile(right!);

        return new CliResult
        {
            Status = "success",
            ProfileDiff = ProfileDiffBuilder.Build(left!, leftPath, right!, rightPath)
        };
    }


    private static bool IsProfileOutputRefused(string outputPath, string docPath, SessionPaths? workspacePaths)
    {
        if (SamePath(outputPath, docPath))
        {
            return true;
        }

        return workspacePaths is not null
            && (SamePath(outputPath, workspacePaths.WorkingDocument)
                || SamePath(outputPath, workspacePaths.ProfileJson)
                || SamePath(outputPath, workspacePaths.SessionJson)
                || IsPathInsideDirectory(workspacePaths.Workspace, outputPath));
    }

}
