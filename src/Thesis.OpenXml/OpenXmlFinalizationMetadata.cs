using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.VariantTypes;
using Thesis.Schema;
using CustomProperties = DocumentFormat.OpenXml.CustomProperties;

namespace Thesis.OpenXml;

public static class OpenXmlFinalizationMetadata
{
    private const string SchemaVersionProperty = "thesis.finalization.schemaVersion";
    private const string FingerprintProperty = "thesis.finalization.fingerprint";
    private const string HostProperty = "thesis.finalization.host";
    private const string ProgIdProperty = "thesis.finalization.progId";
    private const string CompletedAtProperty = "thesis.finalization.completedAtUtc";
    private const string ReasonsProperty = "thesis.finalization.reasons";

    private static readonly string[] PropertyNames =
    [
        SchemaVersionProperty,
        FingerprintProperty,
        HostProperty,
        ProgIdProperty,
        CompletedAtProperty,
        ReasonsProperty
    ];

    public static HostFinalizationState? Read(string docxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);

        using var document = WordprocessingDocument.Open(Path.GetFullPath(docxPath), isEditable: false);
        var customProperties = document.CustomFilePropertiesPart?.Properties;
        if (customProperties is null)
        {
            return null;
        }

        var fingerprint = ReadProperty(customProperties, FingerprintProperty);
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        var currentFingerprint = ComputeDocumentFingerprint(document);
        return new HostFinalizationState
        {
            SchemaVersion = ReadProperty(customProperties, SchemaVersionProperty) ?? "1.0",
            IsCurrent = string.Equals(fingerprint, currentFingerprint, StringComparison.Ordinal),
            Host = ReadProperty(customProperties, HostProperty),
            ProgId = ReadProperty(customProperties, ProgIdProperty),
            CompletedAtUtc = ReadProperty(customProperties, CompletedAtProperty),
            Fingerprint = fingerprint,
            Reasons = SplitReasons(ReadProperty(customProperties, ReasonsProperty))
        };
    }

    public static void MarkHostFinalized(string docxPath, HostApplicationReport report, IEnumerable<string> reasons)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);
        ArgumentNullException.ThrowIfNull(report);

        using var document = WordprocessingDocument.Open(Path.GetFullPath(docxPath), isEditable: true);
        var customProperties = document.CustomFilePropertiesPart ?? document.AddCustomFilePropertiesPart();
        customProperties.Properties ??= new CustomProperties.Properties();

        var fingerprint = ComputeDocumentFingerprint(document);
        UpsertProperty(customProperties.Properties, SchemaVersionProperty, "1.0");
        UpsertProperty(customProperties.Properties, FingerprintProperty, fingerprint);
        UpsertProperty(customProperties.Properties, HostProperty, report.RequestedHost);
        UpsertProperty(customProperties.Properties, ProgIdProperty, report.ProgId);
        UpsertProperty(customProperties.Properties, CompletedAtProperty, DateTimeOffset.UtcNow.ToString("O"));
        UpsertProperty(customProperties.Properties, ReasonsProperty, string.Join("|", reasons.Distinct(StringComparer.OrdinalIgnoreCase)));
        customProperties.Properties.Save();
    }

    public static void Clear(string docxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);

        using var document = WordprocessingDocument.Open(Path.GetFullPath(docxPath), isEditable: true);
        var customProperties = document.CustomFilePropertiesPart?.Properties;
        if (customProperties is null)
        {
            return;
        }

        foreach (var property in customProperties.Elements<CustomProperties.CustomDocumentProperty>()
            .Where(property => PropertyNames.Contains(property.Name?.Value, StringComparer.Ordinal))
            .ToList())
        {
            property.Remove();
        }

        customProperties.Save();
    }

    internal static string ComputeDocumentFingerprint(WordprocessingDocument document)
    {
        using var hash = SHA256.Create();
        foreach (var part in EnumerateParts(document)
            .Where(part => part.Uri.OriginalString.StartsWith("/word/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(part => part.Uri.OriginalString, StringComparer.OrdinalIgnoreCase))
        {
            var uriBytes = Encoding.UTF8.GetBytes(part.Uri.OriginalString);
            hash.TransformBlock(uriBytes, 0, uriBytes.Length, null, 0);
            hash.TransformBlock([0], 0, 1, null, 0);
            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            stream.CopyTo(new HashingStream(hash));
            hash.TransformBlock([0], 0, 1, null, 0);
        }

        hash.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(hash.Hash ?? []).ToLowerInvariant();
    }

    private static IEnumerable<OpenXmlPart> EnumerateParts(OpenXmlPartContainer container)
    {
        var visited = new HashSet<Uri>();
        foreach (var pair in container.Parts)
        {
            foreach (var part in EnumerateParts(pair.OpenXmlPart, visited))
            {
                yield return part;
            }
        }
    }

    private static IEnumerable<OpenXmlPart> EnumerateParts(OpenXmlPart part, HashSet<Uri> visited)
    {
        if (!visited.Add(part.Uri))
        {
            yield break;
        }

        yield return part;
        foreach (var pair in part.Parts)
        {
            foreach (var child in EnumerateParts(pair.OpenXmlPart, visited))
            {
                yield return child;
            }
        }
    }

    private static string? ReadProperty(CustomProperties.Properties properties, string name)
    {
        var property = properties.Elements<CustomProperties.CustomDocumentProperty>()
            .FirstOrDefault(property => string.Equals(property.Name?.Value, name, StringComparison.Ordinal));
        return property?.VTLPWSTR?.Text ?? property?.InnerText;
    }

    private static void UpsertProperty(CustomProperties.Properties properties, string name, string? value)
    {
        var property = properties.Elements<CustomProperties.CustomDocumentProperty>()
            .FirstOrDefault(property => string.Equals(property.Name?.Value, name, StringComparison.Ordinal));
        if (property is null)
        {
            property = new CustomProperties.CustomDocumentProperty
            {
                FormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}",
                PropertyId = NextPropertyId(properties),
                Name = name
            };
            properties.AppendChild(property);
        }

        property.RemoveAllChildren<VTLPWSTR>();
        property.AppendChild(new VTLPWSTR(value ?? ""));
    }

    private static int NextPropertyId(CustomProperties.Properties properties)
    {
        return properties.Elements<CustomProperties.CustomDocumentProperty>()
            .Select(property => property.PropertyId?.Value ?? 1)
            .DefaultIfEmpty(1)
            .Max() + 1;
    }

    private static List<string> SplitReasons(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class HashingStream(HashAlgorithm hash) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            hash.TransformBlock(buffer, offset, count, null, 0);
        }
    }
}
