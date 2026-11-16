using Thesis.Schema;
using Thesis.Core;
using Thesis.Session;
using Thesis.OpenXml;
using static Thesis.Cli.CliCommandSupport;
using static Thesis.Cli.CliInputReader;

namespace Thesis.Cli;

internal static class DocumentBuildCommands
{
    internal static CliResult GenerateDocument(string[] args)
    {
        var contentPath = RequiredOption(args, "--content");
        var rulesPath = RequiredOption(args, "--rules");
        var outputPath = RequiredOption(args, "--out");
        var fullOutputPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Error("generate_output_directory_missing", $"Generate output directory not found: {parent}");
        }

        if (SamePath(fullOutputPath, contentPath) || SamePath(fullOutputPath, rulesPath))
        {
            return Error("generate_output_refused", "Generate output path must not overwrite input JSON files.");
        }

        if (!TryReadContent(contentPath, out var content, out var contentError))
        {
            return contentError!;
        }

        if (!TryReadProfile(rulesPath, out var rules, out var rulesError))
        {
            return rulesError!;
        }

        ContentNormalizer.Normalize(content!);
        NormalizeProfile(rules!);

        try
        {
            using var transaction = new FileOutputTransaction();
            var staged = transaction.Stage(fullOutputPath);
            ContentPaths.ProtectAssets(content!, fullOutputPath);
            ThesisDocumentGenerator.Generate(content!, rules!, staged);
            transaction.Commit();
            return new CliResult
            {
                Status = "success",
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "info",
                        Code = "thesis_generated",
                        Message = "Thesis content JSON was generated into a DOCX document.",
                        Path = fullOutputPath
                    }
                ]
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CliResult
            {
                Status = "error",
                OutputPath = fullOutputPath,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "generate_failed",
                        Message = $"Generate failed: {ex.Message}",
                        Path = fullOutputPath
                    }
                ]
            };
        }
    }
    internal static CliResult AssembleDocument(string[] args)
    {
        var docPath = RequiredOption(args, "--doc");
        var contentPath = RequiredOption(args, "--content");
        var profilePath = RequiredOption(args, "--profile");
        var outputPath = RequiredOption(args, "--out");
        var frontMatterDocPaths = Options(args, "--front-matter-doc");
        var fullDocPath = Path.GetFullPath(docPath);
        var fullOutputPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Error("assemble_output_directory_missing", $"Assemble output directory not found: {parent}");
        }

        if (SamePath(fullOutputPath, fullDocPath)
            || SamePath(fullOutputPath, contentPath)
            || SamePath(fullOutputPath, profilePath)
            || frontMatterDocPaths.Any(path => SamePath(fullOutputPath, path)))
        {
            return Error("assemble_output_refused", "Assemble output path must not overwrite input files.");
        }

        if (!TryReadContent(contentPath, out var content, out var contentError))
        {
            return contentError!;
        }

        if (!TryReadProfile(profilePath, out var profile, out var profileError))
        {
            return profileError!;
        }

        ContentNormalizer.Normalize(content!);
        NormalizeProfile(profile!);

        try
        {
            using var transaction = new FileOutputTransaction();
            var staged = transaction.Stage(fullOutputPath);
            ContentPaths.ProtectAssets(content!, fullOutputPath);
            File.Copy(fullDocPath, staged, overwrite: true);
            ThesisDocumentGenerator.AssembleIntoTemplate(content!, profile!, staged, frontMatterDocPaths);
            transaction.Commit();
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
                        Code = "thesis_assembled",
                        Message = "Thesis content JSON was assembled into a template DOCX copy.",
                        Path = fullOutputPath
                    }
                ]
            };
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
                        Code = "assemble_failed",
                        Message = $"Assemble failed: {ex.Message}",
                        Path = fullDocPath
                    }
                ]
            };
        }
    }

}
