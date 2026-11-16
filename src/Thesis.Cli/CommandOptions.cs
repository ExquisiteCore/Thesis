namespace Thesis.Cli;

/// <summary>Strict named option parsing shared by the assembly commands.</summary>
internal sealed class CommandOptions
{
    private readonly Dictionary<string, string> values = [];
    public CommandOptions(string[] args, params string[] allowed)
    {
        for (var i = 0; i < args.Length; i += 2)
            if (!allowed.Contains(args[i]) || i + 1 >= args.Length || args[i + 1].StartsWith("--") || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException($"Invalid, duplicate or incomplete option: {args[i]}");
    }
    public string? Get(string name) => values.GetValueOrDefault(name);
    public string Required(string name) => Get(name) ?? throw new ArgumentException($"Missing {name}.");
    public string Path(string name) => System.IO.Path.GetFullPath(Required(name));
    public string? OptionalPath(string name) => Get(name) is null ? null : Path(name);
}
