using Thesis.Schema;
using Thesis.Session;
using Thesis.OpenXml;

namespace Thesis.Cli;

public static class ThesisCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        CliResult result;
        var exitCode = 0;

        try
        {
            result = Dispatch(args);
            if (!string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                exitCode = 1;
            }
        }
        catch (CliException ex)
        {
            result = Error(ex.Code, ex.Message);
            exitCode = 1;
        }
        catch (Exception ex)
        {
            result = Error("unhandled_error", ex.Message);
            exitCode = 1;
        }

        output.WriteLine(ThesisJson.Serialize(result));
        return exitCode;
    }

    private static CliResult Dispatch(string[] args)
    {
        if (args is ["session", "init", .. var initArgs])
        {
            var doc = RequiredOption(initArgs, "--doc");
            var profile = RequiredOption(initArgs, "--profile");
            var workspace = RequiredOption(initArgs, "--workspace");
            return SessionInitializer.Initialize(doc, profile, workspace);
        }

        if (args is ["run", .. var runArgs])
        {
            var workspace = RequiredOption(runArgs, "--workspace");
            var requestPath = RequiredOption(runArgs, "--request");
            var request = ThesisJson.Deserialize<OperationRequest>(File.ReadAllText(requestPath));
            var paths = SessionPaths.FromWorkspace(workspace);

            return new CliResult
            {
                Status = "success",
                RequestId = request.RequestId,
                Mode = request.Mode,
                Workspace = paths.Workspace,
                Document = paths.WorkingDocument
            };
        }

        if (args is ["snapshot", .. var snapshotArgs])
        {
            var workspace = RequiredOption(snapshotArgs, "--workspace");
            var name = RequiredOption(snapshotArgs, "--name");
            return SessionLifecycle.Snapshot(workspace, name);
        }

        if (args is ["rollback", .. var rollbackArgs])
        {
            var workspace = RequiredOption(rollbackArgs, "--workspace");
            var snapshot = RequiredOption(rollbackArgs, "--snapshot");
            return SessionLifecycle.Rollback(workspace, snapshot);
        }

        if (args is ["export", .. var exportArgs])
        {
            var workspace = RequiredOption(exportArgs, "--workspace");
            var outputPath = RequiredOption(exportArgs, "--out");
            return SessionLifecycle.Export(workspace, outputPath);
        }

        if (args is ["inspect", .. var inspectArgs])
        {
            var workspace = RequiredOption(inspectArgs, "--workspace");
            var result = SessionLifecycle.Inspect(workspace);
            if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(result.Document))
            {
                if (OpenXmlDocumentInspector.TryInspect(result.Document, out var documentMap, out var diagnostic))
                {
                    result.DocumentMap = documentMap;
                }
                else if (diagnostic is not null)
                {
                    result.Diagnostics.Add(diagnostic);
                }
            }

            return result;
        }

        throw new CliException("unknown_command", "Unknown command.");
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

internal sealed class CliException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
