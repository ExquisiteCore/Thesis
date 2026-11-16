using Thesis.Schema;

namespace Thesis.Application;

public interface IDocumentLayoutEngine
{
    string Name { get; }
    bool UpdatesDocument { get; }
    HostApplicationReport? Render(string documentPath, string pdfPath, string workingDirectory, LayoutRenderConfig config);
}
