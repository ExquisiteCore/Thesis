using Thesis.Host;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Application;

public static class DocumentFinalizer
{
    public static HostApplicationReport ConvertWithOffice(string source, string output, string host, string? progId, int timeoutSeconds = 120)
    {
        if (!OperatingSystem.IsWindows()) throw new ArgumentException("Office conversion requires Windows.");
        if (Thesis.Core.FileOutputTransaction.PathComparer.Equals(Path.GetFullPath(source), Path.GetFullPath(output)))
            throw new ArgumentException("Conversion output must differ from its input.");
        var inputHash = TemplatePlanPreflight.Fingerprint(source);
        var report = OfficeProcessRunner.Run(source, new HostApplicationOptions { RequestedHost = host, ProgId = progId,
            OutputDocumentPath = output, UpdateFields = false, UpdateTableOfContents = false, Repaginate = false }, timeoutSeconds);
        if (inputHash != TemplatePlanPreflight.Fingerprint(source)) throw new InvalidDataException("Conversion changed the input file.");
        OpenXmlCompatibilityRepair.NormalizeStyleOrder(output);
        return report;
    }

    public static HostApplicationReport FinalizeWithOffice(string document, HostApplicationOptions options, int timeoutSeconds = 120)
    {
        EnsureHostAvailable(options);
        var before = DocumentIntegrityAudit.AuthoredText(document);
        HostApplicationReport report;
        try { report = OfficeProcessRunner.Run(document, options, timeoutSeconds); }
        catch (IOException ex) { throw new HostApplicationException("host_application_failed", ex.Message, ex); }
        if (OpenXmlCompatibilityRepair.NormalizeStyleOrder(document))
            report.Steps.Add(new HostApplicationStep { Id = "normalizeStyleOrder", Status = "applied", Message = "Normalized Office property order without changing formatting." });
        if (before != DocumentIntegrityAudit.AuthoredText(document))
            throw new InvalidDataException("Office finalization changed authored text; inspect the candidate before promoting it.");
        var errors = DocumentIntegrityAudit.Check(document);
        if (errors.Count > 0) throw new InvalidDataException("Office finalization integrity check failed: " + string.Join("; ", errors));
        return report;
    }

    private static void EnsureHostAvailable(HostApplicationOptions options)
    {
        if (!OperatingSystem.IsWindows()) throw new HostApplicationException("host_application_unsupported_os", "Office COM finalization requires Windows.");
        var ids = options.ProgId is string explicitId ? [explicitId] : options.RequestedHost.ToLowerInvariant() switch
        {
            "wps" => new[] { "KWPS.Application", "WPS.Application" },
            "word" => new[] { "Word.Application" },
            _ => new[] { "KWPS.Application", "WPS.Application", "Word.Application" }
        };
        var registered = false;
        foreach (var id in ids) registered |= Type.GetTypeFromProgID(id) is not null;
        if (!registered)
            throw new HostApplicationException("host_application_unavailable", $"No Office automation host is registered for {options.RequestedHost} ({string.Join(", ", ids)}).");
    }
}
