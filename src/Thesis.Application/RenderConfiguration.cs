using Thesis.Schema;

namespace Thesis.Application;

public static class RenderConfiguration
{
    public static LayoutRenderConfig Load(string path)
    {
        path = Path.GetFullPath(path);
        var config = ThesisJson.DeserializeStrict<LayoutRenderConfig>(File.ReadAllText(path));
        config.Engine = config.Engine?.ToLowerInvariant() ?? throw new ArgumentException("Render engine is required.");
        _ = DocumentLayoutEngines.Create(config);
        var directory = Path.GetDirectoryName(path)!;
        if (config.Engine == "libreoffice") config.LibreOfficePath = Path.GetFullPath(config.LibreOfficePath, directory);
        if (!string.IsNullOrWhiteSpace(config.PythonPath)) config.PythonPath = Path.GetFullPath(config.PythonPath, directory);
        if ((config.Engine == "libreoffice" && !File.Exists(config.LibreOfficePath)) || (!string.IsNullOrWhiteSpace(config.PythonPath) && !File.Exists(config.PythonPath))
            || config.TimeoutSeconds is < 1 or > 600 || config.MaxPasses is < 2 or > 6)
            throw new ArgumentException("Render config requires existing executables, timeoutSeconds 1–600 and maxPasses 2–6.");
        if (config.Engine != "libreoffice" && !OperatingSystem.IsWindows()) throw new ArgumentException("Office COM rendering requires Windows.");
        return config;
    }
}
