using Thesis.Schema;
using Thesis.Session;

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

        if (args is [("inspect" or "snapshot" or "rollback" or "export"), .. var stubArgs])
        {
            var workspace = OptionalOption(stubArgs, "--workspace");
            var requestPath = OptionalOption(stubArgs, "--request");
            var requestId = requestPath is null
                ? null
                : ThesisJson.Deserialize<OperationRequest>(File.ReadAllText(requestPath)).RequestId;
            var paths = workspace is null ? null : SessionPaths.FromWorkspace(workspace);

            return new CliResult
            {
                Status = "notImplemented",
                RequestId = requestId,
                Workspace = paths?.Workspace,
                Document = paths?.WorkingDocument,
                Diagnostics =
                [
                    new Diagnostic
                    {
                        Severity = "error",
                        Code = "not_implemented",
                        Message = $"Command '{args[0]}' is not implemented in P0."
                    }
                ]
            };
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
