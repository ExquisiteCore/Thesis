internal static partial class Program
{
    static (int ExitCode, CliResult Result) RunCli(string[] args)
    {
        var output = new StringWriter();
        var exitCode = ThesisCli.Run(args, output, TextWriter.Null);
        return (exitCode, ThesisJson.Deserialize<CliResult>(output.ToString()));
    }

    static (int ExitCode, string Output) RunCliRaw(string[] args)
    {
        var output = new StringWriter();
        var exitCode = ThesisCli.Run(args, output, TextWriter.Null);
        return (exitCode, output.ToString());
    }
}
