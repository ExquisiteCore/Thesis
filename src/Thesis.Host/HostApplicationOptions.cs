namespace Thesis.Host;

public sealed class HostApplicationOptions
{
    public string Action { get; set; } = "finalize";

    public string RequestedHost { get; set; } = "wps";

    public string? ProgId { get; set; }

    public bool Visible { get; set; }

    public bool KeepOpen { get; set; }

    public bool UpdateFields { get; set; } = true;

    public bool UpdateTableOfContents { get; set; } = true;

    public bool Repaginate { get; set; } = true;

    public bool Save { get; set; } = true;
}
