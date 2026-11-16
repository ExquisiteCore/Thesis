using Thesis.Host;
using Thesis.Schema;

namespace Thesis.Application;

public static class DocumentLayoutEngines
{
    public static IDocumentLayoutEngine Create(LayoutRenderConfig config) => config.Engine.ToLowerInvariant() switch
    {
        "libreoffice" => new LibreOfficeLayoutEngine(),
        "wps" or "word" or "auto" => new OfficeLayoutEngine(config.Engine),
        _ => throw new ArgumentException($"Unknown layout engine: {config.Engine}")
    };
}

internal sealed class OfficeLayoutEngine(string host) : IDocumentLayoutEngine
{
    public string Name => host;
    public bool UpdatesDocument => true;
    public HostApplicationReport Render(string documentPath, string pdfPath, string workingDirectory, LayoutRenderConfig config)
    {
        File.Copy(documentPath, Path.Combine(workingDirectory, "before-office.docx"));
        try { return DocumentFinalizer.FinalizeWithOffice(documentPath, new HostApplicationOptions { RequestedHost = host, ProgId = config.ProgId, PdfPath = pdfPath }, config.TimeoutSeconds); }
        finally { if (File.Exists(documentPath)) File.Copy(documentPath, Path.Combine(workingDirectory, "after-office.docx")); }
    }
}

internal sealed class LibreOfficeLayoutEngine : IDocumentLayoutEngine
{
    public string Name => "libreoffice";
    public bool UpdatesDocument => false;
    public HostApplicationReport? Render(string documentPath, string pdfPath, string workingDirectory, LayoutRenderConfig config)
    {
        var input = Path.Combine(workingDirectory, "document.docx");
        File.Copy(documentPath, input);
        var profileUri = new Uri(Path.Combine(workingDirectory, "lo-profile")).AbsoluteUri;
        ExternalProcessRunner.Run(config.LibreOfficePath,
            ["-env:UserInstallation=" + profileUri, "--headless", "--convert-to", "pdf:writer_pdf_Export", "--outdir", workingDirectory, input], workingDirectory, config.TimeoutSeconds);
        var produced = Path.Combine(workingDirectory, "document.pdf");
        if (!File.Exists(produced)) throw new IOException("LibreOffice produced no PDF.");
        if (!string.Equals(produced, pdfPath, StringComparison.OrdinalIgnoreCase)) File.Copy(produced, pdfPath);
        return null;
    }
}
