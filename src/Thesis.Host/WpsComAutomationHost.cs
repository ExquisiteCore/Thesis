using System.Runtime.InteropServices;
using Thesis.Schema;

namespace Thesis.Host;

public sealed class WpsComAutomationHost
{
    private static readonly string[] WpsProgIds =
    [
        "KWps.Application",
        "Wps.Application",
        "WPS.Application"
    ];

    private static readonly string[] WordProgIds =
    [
        "Word.Application"
    ];

    public HostApplicationReport FinalizeDocument(string documentPath, HostApplicationOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(options);

        var fullPath = Path.GetFullPath(documentPath);
        if (!File.Exists(fullPath))
        {
            throw new HostApplicationException("document_missing", $"Document not found: {fullPath}");
        }

        var host = ResolveHost(options);
        var report = NewReport("finalize", fullPath, host, executed: true);

        dynamic? application = null;
        dynamic? document = null;
        try
        {
            application = CreateApplication(host);
            TrySet(application, "Visible", options.Visible);
            TrySet(application, "DisplayAlerts", 0);

            document = OpenDocument(application, fullPath, options.Visible);

            if (options.UpdateFields)
            {
                UpdateFields(document);
                report.Steps.Add(Step("updateFields", "applied", "Fields were updated through the COM host."));
            }
            else
            {
                report.Steps.Add(Step("updateFields", "skipped", "Field update was disabled."));
            }

            if (options.UpdateTableOfContents)
            {
                UpdateTablesOfContents(document);
                report.Steps.Add(Step("updateTableOfContents", "applied", "Tables of contents were refreshed through the COM host."));
            }
            else
            {
                report.Steps.Add(Step("updateTableOfContents", "skipped", "TOC update was disabled."));
            }

            if (options.Repaginate)
            {
                Repaginate(document);
                report.Steps.Add(Step("repaginate", "applied", "The document was repaginated by the COM host."));
            }
            else
            {
                report.Steps.Add(Step("repaginate", "skipped", "Repagination was disabled."));
            }

            report.Layout = ReadLayoutMetrics(document);

            if (options.Save)
            {
                document.Save();
                report.Steps.Add(Step("save", "applied", "The document was saved after host finalization."));
            }
            else
            {
                report.Steps.Add(Step("save", "skipped", "Saving was disabled."));
            }

            return report;
        }
        catch (COMException ex)
        {
            throw new HostApplicationException("host_application_failed", $"The COM host failed while finalizing the document: {ex.Message}", ex);
        }
        catch (RuntimeBinderExceptionShim ex)
        {
            throw new HostApplicationException("host_application_failed", $"The COM host did not expose a required Word-compatible automation member: {ex.Message}", ex);
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
        {
            throw new HostApplicationException("host_application_failed", $"The COM host did not expose a required Word-compatible automation member: {ex.Message}", ex);
        }
        finally
        {
            AddCloseDocumentStep(report, document, saveChanges: false, keepOpen: options.KeepOpen);
            if (!options.KeepOpen)
            {
                AddQuitApplicationStep(report, application);
            }
            else
            {
                report.Steps.Add(Step("quitApplication", "skipped", "Host application was left open because keepOpen was enabled."));
            }

            ReleaseComObject(document);
            ReleaseComObject(application);
        }
    }

    public HostApplicationReport ValidateLayout(string documentPath, HostApplicationOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(options);

        var fullPath = Path.GetFullPath(documentPath);
        if (!File.Exists(fullPath))
        {
            throw new HostApplicationException("document_missing", $"Document not found: {fullPath}");
        }

        var host = ResolveHost(options);
        var report = NewReport("validate", fullPath, host, executed: false);

        dynamic? application = null;
        dynamic? document = null;
        try
        {
            application = CreateApplication(host);
            TrySet(application, "Visible", options.Visible);
            TrySet(application, "DisplayAlerts", 0);

            document = OpenDocument(application, fullPath, options.Visible, readOnly: true);
            Repaginate(document);
            report.Layout = ReadLayoutMetrics(document);
            report.Steps.Add(Step("layoutMetrics", "applied", "Layout metrics were read from the COM host."));
            return report;
        }
        catch (COMException ex)
        {
            throw new HostApplicationException("host_application_failed", $"The COM host failed while validating layout: {ex.Message}", ex);
        }
        catch (RuntimeBinderExceptionShim ex)
        {
            throw new HostApplicationException("host_application_failed", $"The COM host did not expose a required Word-compatible automation member: {ex.Message}", ex);
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
        {
            throw new HostApplicationException("host_application_failed", $"The COM host did not expose a required Word-compatible automation member: {ex.Message}", ex);
        }
        finally
        {
            AddCloseDocumentStep(report, document, saveChanges: false, keepOpen: options.KeepOpen);
            if (!options.KeepOpen)
            {
                AddQuitApplicationStep(report, application);
            }
            else
            {
                report.Steps.Add(Step("quitApplication", "skipped", "Host application was left open because keepOpen was enabled."));
            }

            ReleaseComObject(document);
            ReleaseComObject(application);
        }
    }

    private static ResolvedHost ResolveHost(HostApplicationOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new HostApplicationException("host_application_unsupported_os", "WPS/Word COM automation is only available on Windows.");
        }

        var requestedHost = NormalizeHost(options.RequestedHost);
        var progIds = GetCandidateProgIds(requestedHost, options.ProgId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var progId in progIds)
        {
            var type = Type.GetTypeFromProgID(progId, throwOnError: false);
            if (type is not null)
            {
                return new ResolvedHost(requestedHost, progId, type);
            }
        }

        throw new HostApplicationException(
            "host_application_unavailable",
            $"No Word-compatible COM host is registered for '{requestedHost}'. Tried: {string.Join(", ", progIds)}.");
    }

    private static string NormalizeHost(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "wps"
            : value.Trim().ToLowerInvariant();
    }

    private static IEnumerable<string> GetCandidateProgIds(string requestedHost, string? explicitProgId)
    {
        if (!string.IsNullOrWhiteSpace(explicitProgId))
        {
            yield return explicitProgId.Trim();
            yield break;
        }

        var environmentProgId = Environment.GetEnvironmentVariable("THESIS_WPS_PROGID");
        if (!string.IsNullOrWhiteSpace(environmentProgId)
            && requestedHost is "wps" or "auto")
        {
            yield return environmentProgId.Trim();
        }

        if (requestedHost is "wps")
        {
            foreach (var progId in WpsProgIds)
            {
                yield return progId;
            }

            yield break;
        }

        if (requestedHost is "word")
        {
            foreach (var progId in WordProgIds)
            {
                yield return progId;
            }

            yield break;
        }

        if (requestedHost is "auto")
        {
            foreach (var progId in WpsProgIds.Concat(WordProgIds))
            {
                yield return progId;
            }

            yield break;
        }

        yield return requestedHost;
    }

    private static dynamic CreateApplication(ResolvedHost host)
    {
        try
        {
            return Activator.CreateInstance(host.ApplicationType)
                ?? throw new HostApplicationException("host_application_unavailable", $"Unable to create COM application: {host.ProgId}");
        }
        catch (COMException ex)
        {
            throw new HostApplicationException("host_application_unavailable", $"Unable to create COM application '{host.ProgId}': {ex.Message}", ex);
        }
    }

    private static dynamic OpenDocument(dynamic application, string documentPath, bool visible, bool readOnly = false)
    {
        try
        {
            dynamic documents = application.Documents;
            return documents.Open(FileName: documentPath, ReadOnly: readOnly, AddToRecentFiles: false, Visible: visible);
        }
        catch (COMException) when (!readOnly)
        {
            return application.Documents.Open(documentPath);
        }
        catch (COMException) when (readOnly)
        {
            return application.Documents.Open(documentPath, false, true);
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
        {
            throw new RuntimeBinderExceptionShim(ex.Message, ex);
        }
    }

    private static void UpdateFields(dynamic document)
    {
        TryInvoke(() => document.Fields.Update());

        try
        {
            foreach (dynamic section in document.Sections)
            {
                foreach (dynamic header in section.Headers)
                {
                    TryInvoke(() => header.Range.Fields.Update());
                }

                foreach (dynamic footer in section.Footers)
                {
                    TryInvoke(() => footer.Range.Fields.Update());
                }
            }
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
        {
            throw new RuntimeBinderExceptionShim(ex.Message, ex);
        }
    }

    private static void UpdateTablesOfContents(dynamic document)
    {
        try
        {
            foreach (dynamic tableOfContents in document.TablesOfContents)
            {
                TryInvoke(() => tableOfContents.Update());
                TryInvoke(() => tableOfContents.UpdatePageNumbers());
            }
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
        {
            throw new RuntimeBinderExceptionShim(ex.Message, ex);
        }
    }

    private static void Repaginate(dynamic document)
    {
        TryInvoke(() => document.Repaginate());
    }

    private static HostLayoutMetrics ReadLayoutMetrics(dynamic document)
    {
        return new HostLayoutMetrics
        {
            PageCount = TryReadInt(() => document.ComputeStatistics(2)),
            ParagraphCount = TryReadInt(() => document.Paragraphs.Count),
            TableCount = TryReadInt(() => document.Tables.Count),
            FieldCount = TryReadInt(() => document.Fields.Count),
            TableOfContentsCount = TryReadInt(() => document.TablesOfContents.Count)
        };
    }

    private static HostApplicationReport NewReport(string action, string documentPath, ResolvedHost host, bool executed)
    {
        return new HostApplicationReport
        {
            Action = action,
            RequestedHost = host.RequestedHost,
            ProgId = host.ProgId,
            Document = documentPath,
            Executed = executed
        };
    }

    private static HostApplicationStep Step(string id, string status, string message)
    {
        return new HostApplicationStep
        {
            Id = id,
            Status = status,
            Message = message
        };
    }

    private static void TrySet(dynamic target, string property, object value)
    {
        try
        {
            target.GetType().InvokeMember(property, System.Reflection.BindingFlags.SetProperty, null, target, new[] { value });
        }
        catch (COMException)
        {
        }
        catch (MissingMethodException)
        {
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
        }
    }

    private static void TryInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
        {
            throw new RuntimeBinderExceptionShim(ex.Message, ex);
        }
    }

    private static int? TryReadInt(Func<dynamic> read)
    {
        try
        {
            var value = read();
            return Convert.ToInt32(value);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or FormatException or OverflowException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    private static void AddCloseDocumentStep(HostApplicationReport report, dynamic? document, bool saveChanges, bool keepOpen)
    {
        if (document is null)
        {
            report.Steps.Add(Step("closeDocument", "skipped", "No COM document was opened."));
            return;
        }

        if (keepOpen)
        {
            report.Steps.Add(Step("closeDocument", "skipped", "Document was left open because keepOpen was enabled."));
            return;
        }

        try
        {
            document.Close(SaveChanges: saveChanges);
            report.Steps.Add(Step("closeDocument", "applied", "The COM document was closed."));
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            report.Steps.Add(Step("closeDocument", "warning", $"The COM document could not be closed: {ex.Message}"));
        }
    }

    private static void AddQuitApplicationStep(HostApplicationReport report, dynamic? application)
    {
        if (application is null)
        {
            report.Steps.Add(Step("quitApplication", "skipped", "No COM application was created."));
            return;
        }

        try
        {
            application.Quit();
            report.Steps.Add(Step("quitApplication", "applied", "The COM application was asked to quit."));
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            report.Steps.Add(Step("quitApplication", "warning", $"The COM application could not be quit: {ex.Message}"));
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (!OperatingSystem.IsWindows() || value is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch (ArgumentException)
        {
        }
    }

    private sealed record ResolvedHost(string RequestedHost, string ProgId, Type ApplicationType);

    private sealed class RuntimeBinderExceptionShim : Exception
    {
        public RuntimeBinderExceptionShim(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
