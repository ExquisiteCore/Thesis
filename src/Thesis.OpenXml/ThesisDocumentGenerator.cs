using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Core;
using Thesis.Schema;
using A = DocumentFormat.OpenXml.Drawing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WP = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Thesis.OpenXml;

public static class ThesisDocumentGenerator
{
    public static void Generate(ThesisContent content, TemplateProfile rules, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        using (var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            AppendThesisContent(mainPart, body, content, rules);
            MarkDocumentFieldsDirty(mainPart);
            body.AppendChild(CreateSectionProperties(rules.PageSetup));
            mainPart.Document.Save();
        }

        NormalizeImagePackageTargets(outputPath);
    }

    public static void AssembleIntoTemplate(
        ThesisContent content,
        TemplateProfile rules,
        string templateCopyPath,
        IReadOnlyList<string>? frontMatterDocPaths = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCopyPath);

        using (var document = WordprocessingDocument.Open(templateCopyPath, isEditable: true))
        {
            var mainPart = document.MainDocumentPart
                ?? throw new InvalidDataException("DOCX does not contain a main document part.");
            var wordDocument = mainPart.Document
                ?? throw new InvalidDataException("DOCX does not contain a document.");
            var body = wordDocument.Body
                ?? throw new InvalidDataException("DOCX does not contain a document body.");

            if (!TryReplaceTemplateThesisRange(mainPart, body, content, rules, frontMatterDocPaths ?? []))
            {
                var sectionProperties = body.Elements<SectionProperties>().LastOrDefault()?.CloneNode(deep: true) as SectionProperties
                    ?? CreateSectionProperties(rules.PageSetup);
                body.RemoveAllChildren();
                AppendFrontMatterDocuments(mainPart, body, frontMatterDocPaths ?? []);
                AppendThesisContent(mainPart, body, content, rules);
                body.AppendChild(sectionProperties);
            }

            MarkDocumentFieldsDirty(mainPart);
            wordDocument.Save();
        }

        NormalizeImagePackageTargets(templateCopyPath);
    }

    private static bool TryReplaceTemplateThesisRange(
        MainDocumentPart mainPart,
        Body body,
        ThesisContent content,
        TemplateProfile rules,
        IReadOnlyList<string> frontMatterDocPaths)
    {
        var blocks = body.Elements<OpenXmlElement>().ToList();
        var startIndex = FindThesisRangeStart(blocks);
        if (startIndex is null)
        {
            return false;
        }

        var sectionBreakIndex = FindThesisRangeSectionBreak(blocks, startIndex.Value);
        if (sectionBreakIndex is null)
        {
            return false;
        }

        var generatedBody = new Body();
        AppendThesisContent(mainPart, generatedBody, content, rules);
        var generatedBlocks = generatedBody.Elements<OpenXmlElement>()
            .Select(block => block.CloneNode(deep: true))
            .ToList();
        var sectionBreak = CreateSectionBreakBlock(blocks[sectionBreakIndex.Value]);
        var rewrittenBlocks = blocks
            .Take(startIndex.Value)
            .Select(block => block.CloneNode(deep: true))
            .Concat(ReadFrontMatterBlocks(mainPart, frontMatterDocPaths))
            .Concat(generatedBlocks)
            .Concat([sectionBreak])
            .ToList();

        body.RemoveAllChildren();
        foreach (var block in rewrittenBlocks)
        {
            body.AppendChild(block);
        }

        return true;
    }

    private static List<OpenXmlElement> ReadFrontMatterBlocks(MainDocumentPart targetMainPart, IReadOnlyList<string> frontMatterDocPaths)
    {
        var blocks = new List<OpenXmlElement>();
        foreach (var docPath in frontMatterDocPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            using var source = WordprocessingDocument.Open(Path.GetFullPath(docPath), isEditable: false);
            var sourceMainPart = source.MainDocumentPart
                ?? throw new InvalidDataException($"Front matter DOCX does not contain a main document part: {docPath}");
            var sourceBody = sourceMainPart.Document?.Body
                ?? throw new InvalidDataException($"Front matter DOCX does not contain a document body: {docPath}");

            foreach (var sourceBlock in sourceBody.Elements<OpenXmlElement>())
            {
                if (sourceBlock is SectionProperties)
                {
                    continue;
                }

                if (sourceBlock is Paragraph paragraph
                    && paragraph.ParagraphProperties?.SectionProperties is not null
                    && string.IsNullOrWhiteSpace(BlockText(paragraph))
                    && !paragraph.Descendants<Drawing>().Any())
                {
                    continue;
                }

                var clone = sourceBlock.CloneNode(deep: true);
                CopyReferencedParts(sourceMainPart, targetMainPart, clone);
                blocks.Add(clone);
            }
        }

        return blocks;
    }

    private static void AppendFrontMatterDocuments(
        MainDocumentPart targetMainPart,
        Body body,
        IReadOnlyList<string> frontMatterDocPaths)
    {
        foreach (var block in ReadFrontMatterBlocks(targetMainPart, frontMatterDocPaths))
        {
            body.AppendChild(block);
        }
    }

    private static void CopyReferencedParts(MainDocumentPart sourceMainPart, MainDocumentPart targetMainPart, OpenXmlElement block)
    {
        foreach (var drawing in block.Descendants<Drawing>())
        {
            var blip = drawing.Descendants<A.Blip>().FirstOrDefault();
            var oldRelationshipId = blip?.Embed?.Value;
            if (string.IsNullOrWhiteSpace(oldRelationshipId))
            {
                continue;
            }

            var sourcePart = sourceMainPart.GetPartById(oldRelationshipId);
            if (sourcePart is not ImagePart sourceImagePart)
            {
                continue;
            }

            var targetImagePart = targetMainPart.AddImagePart(sourceImagePart.ContentType);
            using (var stream = sourceImagePart.GetStream(FileMode.Open, FileAccess.Read))
            {
                targetImagePart.FeedData(stream);
            }

            blip!.Embed = targetMainPart.GetIdOfPart(targetImagePart);
        }
    }

    private static int? FindThesisRangeStart(List<OpenXmlElement> blocks)
    {
        for (var index = 0; index < blocks.Count; index++)
        {
            if (IsThesisStartAnchor(BlockText(blocks[index])))
            {
                return index;
            }
        }

        return null;
    }

    private static int? FindThesisRangeSectionBreak(List<OpenXmlElement> blocks, int startIndex)
    {
        var sectionBreakIndices = blocks
            .Select((block, index) => new { Block = block, Index = index })
            .Where(item => item.Index >= startIndex && HasSectionProperties(item.Block))
            .Select(item => item.Index)
            .ToList();
        if (sectionBreakIndices.Count == 0)
        {
            return null;
        }

        if (sectionBreakIndices.Count == 1)
        {
            return sectionBreakIndices[0];
        }

        var lastSectionBreakIndex = sectionBreakIndices[^1];
        var previousSectionBreakIndex = sectionBreakIndices[^2];
        return LooksLikeTemplateTailSection(blocks, previousSectionBreakIndex, lastSectionBreakIndex)
            ? previousSectionBreakIndex
            : lastSectionBreakIndex;
    }

    private static bool IsThesisStartAnchor(string text)
    {
        return ThesisTextHeuristics.IsChineseAbstractHeading(text)
            || ThesisTextHeuristics.IsEnglishAbstractHeading(text)
            || ThesisTextHeuristics.IsTocHeading(text)
            || IsChapterHeading(text);
    }

    private static bool IsChapterHeading(string text)
    {
        var normalized = Regex.Replace(text, @"\s+", "", RegexOptions.CultureInvariant);
        return Regex.IsMatch(normalized, @"^第[一二三四五六七八九十百千万零〇两0-9Xx]+章", RegexOptions.CultureInvariant);
    }

    private static bool LooksLikeTemplateTailSection(List<OpenXmlElement> blocks, int previousSectionBreakIndex, int lastSectionBreakIndex)
    {
        var tailTexts = blocks
            .Skip(previousSectionBreakIndex + 1)
            .Take(lastSectionBreakIndex - previousSectionBreakIndex - 1)
            .Select(BlockText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return tailTexts.Count > 0 && !tailTexts.Any(IsThesisStartAnchor);
    }

    private static bool HasSectionProperties(OpenXmlElement block)
    {
        return block is SectionProperties || block.Descendants<SectionProperties>().Any();
    }

    private static OpenXmlElement CreateSectionBreakBlock(OpenXmlElement block)
    {
        if (block is SectionProperties sectionProperties)
        {
            return sectionProperties.CloneNode(deep: true);
        }

        var clonedSection = block.Descendants<SectionProperties>().LastOrDefault()?.CloneNode(deep: true) as SectionProperties;
        return clonedSection is null
            ? block.CloneNode(deep: true)
            : new Paragraph(new ParagraphProperties(clonedSection));
    }

    private static string BlockText(OpenXmlElement block)
    {
        return string.Concat(block.Descendants<Text>().Select(text => text.Text));
    }

    private static void AppendThesisContent(MainDocumentPart mainPart, Body body, ThesisContent content, TemplateProfile rules)
    {
        AppendParagraph(body, RequiredTitle(content), ResolveParagraphFormat(rules, "title"), "Title");
        AppendOptionalParagraph(body, content.Author, ResolveParagraphFormat(rules, "body"), "Normal");
        AppendAbstracts(body, content, rules);
        AppendTableOfContents(body, rules);
        AppendChapters(mainPart, body, content.Chapters, rules);
        AppendReferences(body, content.References, rules);
        AppendAcknowledgements(body, content.Acknowledgements, rules);
    }

    private static void AppendAbstracts(Body body, ThesisContent content, TemplateProfile rules)
    {
        if (!string.IsNullOrWhiteSpace(content.AbstractZh) || content.KeywordsZh.Count > 0)
        {
            AppendParagraph(body, "摘要", ResolveParagraphFormat(rules, "abstract.zh", "heading1"), "Heading1");
            AppendOptionalParagraph(body, content.AbstractZh, ResolveParagraphFormat(rules, "body"), "Normal");
            AppendKeywords(body, "关键词：", content.KeywordsZh, ResolveParagraphFormat(rules, "keywords.zh", "body"));
        }

        if (!string.IsNullOrWhiteSpace(content.AbstractEn) || content.KeywordsEn.Count > 0)
        {
            AppendParagraph(body, "Abstract", ResolveParagraphFormat(rules, "abstract.en", "heading1"), "Heading1");
            AppendOptionalParagraph(body, content.AbstractEn, ResolveParagraphFormat(rules, "body"), "Normal");
            AppendKeywords(body, "Keywords: ", content.KeywordsEn, ResolveParagraphFormat(rules, "keywords.en", "body"));
        }
    }

    private static void AppendKeywords(Body body, string prefix, List<string> keywords, ParagraphFormatSample? format)
    {
        if (keywords.Count == 0)
        {
            return;
        }

        var separator = prefix.EndsWith('：') ? "；" : "; ";
        AppendParagraph(body, prefix + string.Join(separator, keywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword))), format, "Normal");
    }

    private static void AppendChapters(MainDocumentPart mainPart, Body body, List<ThesisChapterContent> chapters, TemplateProfile rules)
    {
        for (var chapterIndex = 0; chapterIndex < chapters.Count; chapterIndex++)
        {
            var chapter = chapters[chapterIndex];
            var chapterNumber = chapterIndex + 1;
            AppendParagraph(body, FormatChapterTitle(chapter.Title, chapterNumber), ResolveParagraphFormat(rules, "heading1"), "Heading1");
            AppendContentBlocks(mainPart, body, chapter.Blocks, chapter.Paragraphs, chapter.Tables, rules);

            for (var sectionIndex = 0; sectionIndex < chapter.Sections.Count; sectionIndex++)
            {
                var section = chapter.Sections[sectionIndex];
                AppendParagraph(body, FormatSectionTitle(section.Title, chapterNumber, sectionIndex + 1), ResolveParagraphFormat(rules, "heading2", "heading1"), "Heading2");
                AppendContentBlocks(mainPart, body, section.Blocks, section.Paragraphs, section.Tables, rules);
            }
        }
    }

    private static void AppendContentBlocks(
        MainDocumentPart mainPart,
        Body body,
        List<ThesisContentBlock> blocks,
        List<string> legacyParagraphs,
        List<ThesisTableContent> legacyTables,
        TemplateProfile rules)
    {
        if (blocks.Count == 0)
        {
            AppendBodyParagraphs(body, legacyParagraphs, rules);
            AppendTables(body, legacyTables, rules);
            return;
        }

        foreach (var block in blocks)
        {
            var type = string.IsNullOrWhiteSpace(block.Type) ? "paragraph" : block.Type.Trim();
            if (string.Equals(type, "paragraph", StringComparison.OrdinalIgnoreCase))
            {
                AppendOptionalParagraph(body, block.Text, ResolveParagraphFormat(rules, "body"), "Normal");
            }
            else if (string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
            {
                AppendImageBlock(mainPart, body, block, rules);
            }
            else if (string.Equals(type, "table", StringComparison.OrdinalIgnoreCase) && block.Table is not null)
            {
                AppendTables(body, [block.Table], rules);
            }
        }
    }

    private static void AppendBodyParagraphs(Body body, List<string> paragraphs, TemplateProfile rules)
    {
        var format = ResolveParagraphFormat(rules, "body");
        foreach (var paragraph in paragraphs.Where(paragraph => !string.IsNullOrWhiteSpace(paragraph)))
        {
            AppendParagraph(body, paragraph, format, "Normal");
        }
    }

    private static void AppendTables(Body body, List<ThesisTableContent> tables, TemplateProfile rules)
    {
        foreach (var table in tables)
        {
            AppendOptionalParagraph(body, table.Caption, ResolveParagraphFormat(rules, "tableCaption", "body"), "Normal");
            body.AppendChild(CreateTable(table, ResolveTableFormat(rules)));
        }
    }

    private static void AppendImageBlock(MainDocumentPart mainPart, Body body, ThesisContentBlock block, TemplateProfile rules)
    {
        if (string.IsNullOrWhiteSpace(block.Path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(block.Path);
        if (!File.Exists(fullPath))
        {
            throw new InvalidDataException($"Image file not found: {fullPath}");
        }

        var imagePart = AddImagePart(mainPart, fullPath);
        var (widthEmu, heightEmu) = ResolveImageSize(block);
        body.AppendChild(CreateImageParagraph(
            mainPart.GetIdOfPart(imagePart),
            widthEmu,
            heightEmu,
            block.AltText ?? block.Caption ?? Path.GetFileName(fullPath),
            NextDrawingId(mainPart, body),
            ResolveParagraphFormat(rules, "figure", "body") ?? new ParagraphFormatSample { Alignment = "center" }));
        AppendOptionalParagraph(body, block.Caption, ResolveParagraphFormat(rules, "figureCaption", "body"), "Normal");
    }

    private static ImagePart AddImagePart(MainDocumentPart mainPart, string imagePath)
    {
        var imagePartType = Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".bmp" => ImagePartType.Bmp,
            ".gif" => ImagePartType.Gif,
            ".ico" => ImagePartType.Icon,
            ".jpeg" or ".jpg" => ImagePartType.Jpeg,
            ".png" => ImagePartType.Png,
            ".tif" or ".tiff" => ImagePartType.Tiff,
            _ => throw new InvalidDataException("Unsupported image format.")
        };

        var imagePart = mainPart.AddImagePart(imagePartType);
        using var stream = File.OpenRead(imagePath);
        imagePart.FeedData(stream);
        return imagePart;
    }

    private static void NormalizeImagePackageTargets(string docxPath)
    {
        using var archive = ZipFile.Open(docxPath, ZipArchiveMode.Update);
        var relationshipParts = ReadRelationshipParts(archive);
        if (relationshipParts.Count == 0)
        {
            return;
        }

        var movedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in relationshipParts)
        {
            foreach (var relationship in part.Relationships)
            {
                var type = relationship.Attribute("Type")?.Value;
                if (type is null
                    || !type.EndsWith("/image", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(relationship.Attribute("TargetMode")?.Value))
                {
                    continue;
                }

                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                var sourceEntryName = ResolveRelationshipTarget(part.SourcePartEntryName, target);
                if (sourceEntryName is null)
                {
                    continue;
                }

                var sourceEntry = archive.GetEntry(sourceEntryName);
                if (sourceEntry is null)
                {
                    continue;
                }

                var targetEntryName = ResolveNormalizedMediaEntry(archive, movedEntries, sourceEntryName);
                var normalizedTarget = RelativeRelationshipTarget(part.SourcePartEntryName, targetEntryName);
                if (!string.Equals(target, normalizedTarget, StringComparison.Ordinal))
                {
                    relationship.SetAttributeValue("Target", normalizedTarget);
                    part.Changed = true;
                }
            }
        }

        foreach (var (sourceEntryName, targetEntryName) in movedEntries)
        {
            if (string.Equals(sourceEntryName, targetEntryName, StringComparison.OrdinalIgnoreCase)
                || AnyRelationshipTargetsEntry(relationshipParts, sourceEntryName))
            {
                continue;
            }

            archive.GetEntry(sourceEntryName)?.Delete();
            RemoveContentTypeOverride(archive, sourceEntryName);
        }

        foreach (var part in relationshipParts.Where(part => part.Changed))
        {
            part.Entry.Delete();
            var newEntry = archive.CreateEntry(part.EntryName);
            using var writer = new StreamWriter(newEntry.Open());
            part.Document.Save(writer, SaveOptions.DisableFormatting);
        }
    }

    private static List<RelationshipPart> ReadRelationshipParts(ZipArchive archive)
    {
        XNamespace rels = "http://schemas.openxmlformats.org/package/2006/relationships";
        var parts = new List<RelationshipPart>();
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            XDocument document;
            using (var reader = new StreamReader(entry.Open()))
            {
                document = XDocument.Load(reader);
            }

            var relationships = document.Root?.Elements(rels + "Relationship").ToList() ?? [];
            if (relationships.Count == 0)
            {
                continue;
            }

            parts.Add(new RelationshipPart(
                entry.FullName,
                entry,
                SourcePartEntryName(entry.FullName),
                document,
                relationships));
        }

        return parts;
    }

    private static string SourcePartEntryName(string relationshipsEntryName)
    {
        var normalized = relationshipsEntryName.Replace('\\', '/');
        if (string.Equals(normalized, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        const string marker = "/_rels/";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || !normalized.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var prefix = normalized[..markerIndex];
        var fileName = normalized[(markerIndex + marker.Length)..^".rels".Length];
        return string.IsNullOrWhiteSpace(prefix) ? fileName : $"{prefix}/{fileName}";
    }

    private static string? ResolveRelationshipTarget(string sourcePartEntryName, string target)
    {
        var normalizedTarget = Uri.UnescapeDataString(target.Replace('\\', '/'));
        if (normalizedTarget.Contains("://", StringComparison.Ordinal) || normalizedTarget.StartsWith('#'))
        {
            return null;
        }

        if (normalizedTarget.StartsWith("/", StringComparison.Ordinal))
        {
            return NormalizePackageEntryName(normalizedTarget.TrimStart('/'));
        }

        var sourceDirectory = PackageDirectory(sourcePartEntryName);
        return NormalizePackageEntryName(string.IsNullOrWhiteSpace(sourceDirectory)
            ? normalizedTarget
            : $"{sourceDirectory}/{normalizedTarget}");
    }

    private static string ResolveNormalizedMediaEntry(
        ZipArchive archive,
        Dictionary<string, string> movedEntries,
        string sourceEntryName)
    {
        if (IsWordMediaEntry(sourceEntryName))
        {
            return sourceEntryName;
        }

        if (movedEntries.TryGetValue(sourceEntryName, out var existingTarget))
        {
            return existingTarget;
        }

        var sourceEntry = archive.GetEntry(sourceEntryName)
            ?? throw new InvalidDataException($"Image package part not found: {sourceEntryName}");
        var targetEntryName = UniqueMediaEntryName(archive, Path.GetFileName(sourceEntryName), sourceEntryName);
        var targetEntry = archive.CreateEntry(targetEntryName);
        using (var sourceStream = sourceEntry.Open())
        using (var targetStream = targetEntry.Open())
        {
            sourceStream.CopyTo(targetStream);
        }

        EnsureContentTypeForMovedPart(archive, sourceEntryName, targetEntryName);
        movedEntries[sourceEntryName] = targetEntryName;
        return targetEntryName;
    }

    private static bool IsWordMediaEntry(string entryName)
    {
        return entryName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AnyRelationshipTargetsEntry(List<RelationshipPart> relationshipParts, string entryName)
    {
        return relationshipParts
            .SelectMany(part => part.Relationships.Select(relationship => new { Part = part, Relationship = relationship }))
            .Where(item => string.IsNullOrWhiteSpace(item.Relationship.Attribute("TargetMode")?.Value))
            .Select(item => ResolveRelationshipTarget(item.Part.SourcePartEntryName, item.Relationship.Attribute("Target")?.Value ?? ""))
            .Any(target => string.Equals(target, entryName, StringComparison.OrdinalIgnoreCase));
    }

    private static string UniqueMediaEntryName(ZipArchive archive, string fileName, string sourceEntryName)
    {
        var normalizedSource = sourceEntryName.Replace('\\', '/');
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = "word/media/" + fileName;
        if (archive.GetEntry(candidate) is null
            || string.Equals(candidate, normalizedSource, StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        for (var index = 1; ; index++)
        {
            candidate = $"word/media/{baseName}_{index}{extension}";
            if (archive.GetEntry(candidate) is null
                || string.Equals(candidate, normalizedSource, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
    }

    private static void EnsureContentTypeForMovedPart(ZipArchive archive, string sourceEntryName, string targetEntryName)
    {
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
        {
            return;
        }

        XDocument contentTypesDocument;
        using (var reader = new StreamReader(contentTypesEntry.Open()))
        {
            contentTypesDocument = XDocument.Load(reader);
        }

        var root = contentTypesDocument.Root;
        if (root is null)
        {
            return;
        }

        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
        var targetPartName = "/" + targetEntryName;
        if (root.Elements(types + "Override").Any(element =>
            string.Equals(element.Attribute("PartName")?.Value, targetPartName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var sourcePartName = "/" + sourceEntryName;
        var sourceOverride = root.Elements(types + "Override").FirstOrDefault(element =>
            string.Equals(element.Attribute("PartName")?.Value, sourcePartName, StringComparison.OrdinalIgnoreCase));
        var contentType = sourceOverride?.Attribute("ContentType")?.Value;

        var extension = Path.GetExtension(targetEntryName).TrimStart('.');
        if (string.IsNullOrWhiteSpace(contentType)
            && !string.IsNullOrWhiteSpace(extension)
            && root.Elements(types + "Default").Any(element =>
                string.Equals(element.Attribute("Extension")?.Value, extension, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = ImageContentTypeForExtension(extension);
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            return;
        }

        root.Add(new XElement(
            types + "Override",
            new XAttribute("PartName", targetPartName),
            new XAttribute("ContentType", contentType)));

        contentTypesEntry.Delete();
        var newEntry = archive.CreateEntry("[Content_Types].xml");
        using var writer = new StreamWriter(newEntry.Open());
        contentTypesDocument.Save(writer, SaveOptions.DisableFormatting);
    }

    private static void RemoveContentTypeOverride(ZipArchive archive, string sourceEntryName)
    {
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
        {
            return;
        }

        XDocument contentTypesDocument;
        using (var reader = new StreamReader(contentTypesEntry.Open()))
        {
            contentTypesDocument = XDocument.Load(reader);
        }

        var root = contentTypesDocument.Root;
        if (root is null)
        {
            return;
        }

        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
        var sourcePartName = "/" + sourceEntryName;
        var sourceOverrides = root.Elements(types + "Override")
            .Where(element => string.Equals(element.Attribute("PartName")?.Value, sourcePartName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sourceOverrides.Count == 0)
        {
            return;
        }

        foreach (var sourceOverride in sourceOverrides)
        {
            sourceOverride.Remove();
        }

        contentTypesEntry.Delete();
        var newEntry = archive.CreateEntry("[Content_Types].xml");
        using var writer = new StreamWriter(newEntry.Open());
        contentTypesDocument.Save(writer, SaveOptions.DisableFormatting);
    }

    private static string RelativeRelationshipTarget(string sourcePartEntryName, string targetEntryName)
    {
        var sourceDirectory = PackageDirectory(sourcePartEntryName);
        var relative = string.IsNullOrWhiteSpace(sourceDirectory)
            ? targetEntryName
            : Path.GetRelativePath(sourceDirectory, targetEntryName);
        return relative.Replace('\\', '/');
    }

    private static string PackageDirectory(string entryName)
    {
        var normalized = entryName.Replace('\\', '/');
        var separator = normalized.LastIndexOf('/');
        return separator < 0 ? "" : normalized[..separator];
    }

    private static string NormalizePackageEntryName(string value)
    {
        var segments = new List<string>();
        foreach (var segment in value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join("/", segments);
    }

    private static string? ImageContentTypeForExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            "bmp" => "image/bmp",
            "gif" => "image/gif",
            "ico" => "image/x-icon",
            "jpeg" or "jpg" => "image/jpeg",
            "png" => "image/png",
            "tif" or "tiff" => "image/tiff",
            _ => null
        };
    }

    private sealed class RelationshipPart(
        string entryName,
        ZipArchiveEntry entry,
        string sourcePartEntryName,
        XDocument document,
        List<XElement> relationships)
    {
        public string EntryName { get; } = entryName;

        public ZipArchiveEntry Entry { get; } = entry;

        public string SourcePartEntryName { get; } = sourcePartEntryName;

        public XDocument Document { get; } = document;

        public List<XElement> Relationships { get; } = relationships;

        public bool Changed { get; set; }
    }

    private static (int WidthEmu, int HeightEmu) ResolveImageSize(ThesisContentBlock block)
    {
        var width = block.WidthEmu is > 0 ? block.WidthEmu.Value : 5_600_000;
        var height = block.HeightEmu is > 0 ? block.HeightEmu.Value : 2_956_000;
        return (width, height);
    }

    private static Paragraph CreateImageParagraph(
        string relationshipId,
        int widthEmu,
        int heightEmu,
        string altText,
        uint drawingId,
        ParagraphFormatSample format)
    {
        var drawing = new Drawing(
            new WP.Inline(
                new WP.Extent { Cx = widthEmu, Cy = heightEmu },
                new WP.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                new WP.DocProperties { Id = drawingId, Name = string.IsNullOrWhiteSpace(altText) ? $"Picture {drawingId}" : altText, Description = altText },
                new WP.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = string.IsNullOrWhiteSpace(altText) ? "Picture" : altText },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0, Y = 0 },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });

        var paragraph = new Paragraph(new Run(drawing));
        OpenXmlFormatApplier.ApplyParagraphFormat(paragraph, OpenXmlFormatMerger.Clone(format));
        return paragraph;
    }

    private static void AppendReferences(Body body, List<string> references, TemplateProfile rules)
    {
        if (references.Count == 0)
        {
            return;
        }

        AppendParagraph(body, "参考文献", ResolveParagraphFormat(rules, "references", "heading1"), "Heading1");
        var format = ResolveParagraphFormat(rules, "referenceItem", "body");
        for (var index = 0; index < references.Count; index++)
        {
            AppendParagraph(body, $"[{index + 1}] {StripReferenceNumber(references[index])}", format, "Normal");
        }
    }

    private static void AppendTableOfContents(Body body, TemplateProfile rules)
    {
        AppendParagraph(body, "目录", ResolveTableOfContentsTitleFormat(rules), "");
        body.AppendChild(CreateTocParagraph("1-3"));
    }

    private static void AppendAcknowledgements(Body body, string? acknowledgements, TemplateProfile rules)
    {
        if (string.IsNullOrWhiteSpace(acknowledgements))
        {
            return;
        }

        AppendParagraph(body, "致谢", ResolveParagraphFormat(rules, "acknowledgements", "heading1"), "Heading1");
        AppendParagraph(body, acknowledgements, ResolveParagraphFormat(rules, "body"), "Normal");
    }

    private static void AppendOptionalParagraph(Body body, string? text, ParagraphFormatSample? format, string fallbackStyleId)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            AppendParagraph(body, text, format, fallbackStyleId);
        }
    }

    private static Paragraph AppendParagraph(Body body, string text, ParagraphFormatSample? format, string fallbackStyleId)
    {
        var paragraph = new Paragraph();
        paragraph.AppendChild(new Run(new Text(text)
        {
            Space = NeedsPreservedSpace(text) ? SpaceProcessingModeValues.Preserve : null
        }));

        var effectiveFormat = format is null
            ? new ParagraphFormatSample { StyleId = fallbackStyleId }
            : OpenXmlFormatMerger.Clone(format);
        effectiveFormat.StyleId ??= fallbackStyleId;
        OpenXmlFormatApplier.ApplyParagraphFormat(paragraph, effectiveFormat);
        body.AppendChild(paragraph);
        return paragraph;
    }

    private static Table CreateTable(ThesisTableContent content, TableFormatSample? format)
    {
        var table = new Table();
        if (content.Headers.Count > 0)
        {
            table.AppendChild(CreateTableRow(content.Headers));
        }

        foreach (var row in content.Rows)
        {
            table.AppendChild(CreateTableRow(row));
        }

        if (!table.Elements<TableRow>().Any())
        {
            table.AppendChild(CreateTableRow([""]));
        }

        if (format is not null)
        {
            OpenXmlFormatApplier.ApplyTableFormat(table, OpenXmlFormatMerger.Clone(format));
        }
        else
        {
            OpenXmlFormatApplier.EnsureTableGrid(table);
        }

        return table;
    }

    private static TableRow CreateTableRow(IEnumerable<string> cells)
    {
        var row = new TableRow();
        foreach (var cellText in cells)
        {
            row.AppendChild(new TableCell(new Paragraph(new Run(new Text(cellText ?? "")))));
        }

        return row;
    }

    private static SectionProperties CreateSectionProperties(ProfilePageSetup? pageSetup)
    {
        var section = new SectionProperties();
        if (pageSetup?.PageSize is not null)
        {
            var pageSize = new PageSize();
            if (pageSetup.PageSize.WidthTwips is not null)
            {
                pageSize.Width = (UInt32Value)(uint)pageSetup.PageSize.WidthTwips.Value;
            }

            if (pageSetup.PageSize.HeightTwips is not null)
            {
                pageSize.Height = (UInt32Value)(uint)pageSetup.PageSize.HeightTwips.Value;
            }

            if (string.Equals(pageSetup.PageSize.Orientation, "landscape", StringComparison.OrdinalIgnoreCase))
            {
                pageSize.Orient = PageOrientationValues.Landscape;
            }
            else if (string.Equals(pageSetup.PageSize.Orientation, "portrait", StringComparison.OrdinalIgnoreCase))
            {
                pageSize.Orient = PageOrientationValues.Portrait;
            }

            section.AppendChild(pageSize);
        }

        if (pageSetup?.Margins is not null)
        {
            var margins = new PageMargin();
            if (pageSetup.Margins.TopTwips is not null)
            {
                margins.Top = pageSetup.Margins.TopTwips.Value;
            }

            if (pageSetup.Margins.RightTwips is not null)
            {
                margins.Right = (UInt32Value)(uint)pageSetup.Margins.RightTwips.Value;
            }

            if (pageSetup.Margins.BottomTwips is not null)
            {
                margins.Bottom = pageSetup.Margins.BottomTwips.Value;
            }

            if (pageSetup.Margins.LeftTwips is not null)
            {
                margins.Left = (UInt32Value)(uint)pageSetup.Margins.LeftTwips.Value;
            }

            if (pageSetup.Margins.HeaderTwips is not null)
            {
                margins.Header = (UInt32Value)(uint)pageSetup.Margins.HeaderTwips.Value;
            }

            if (pageSetup.Margins.FooterTwips is not null)
            {
                margins.Footer = (UInt32Value)(uint)pageSetup.Margins.FooterTwips.Value;
            }

            if (pageSetup.Margins.GutterTwips is not null)
            {
                margins.Gutter = (UInt32Value)(uint)pageSetup.Margins.GutterTwips.Value;
            }

            section.AppendChild(margins);
        }

        return section;
    }

    private static Paragraph CreateTocParagraph(string levels)
    {
        return new Paragraph(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin, Dirty = true }),
            new Run(new FieldCode($" TOC \\o \"{levels}\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("目录待更新")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
    }

    private static void MarkDocumentFieldsDirty(MainDocumentPart mainPart)
    {
        var settingsPart = mainPart.DocumentSettingsPart ?? mainPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings ??= new Settings();
        settingsPart.Settings.RemoveAllChildren<UpdateFieldsOnOpen>();
        settingsPart.Settings.AppendChild(new UpdateFieldsOnOpen { Val = true });
        settingsPart.Settings.Save();
    }

    private static ParagraphFormatSample? ResolveParagraphFormat(TemplateProfile rules, string role, string? fallbackRole = null)
    {
        var roleFormat = rules.StyleRoles
            .Where(candidate => RoleMatches(candidate.Role, role))
            .OrderByDescending(candidate => candidate.Confidence)
            .Select(candidate => candidate.Format)
            .FirstOrDefault(candidate => candidate is not null);
        if (roleFormat is not null)
        {
            return roleFormat;
        }

        var policyFormat = rules.RolePolicies
            .Where(candidate => RoleMatches(candidate.Role, role))
            .OrderByDescending(candidate => candidate.Priority)
            .Select(candidate => candidate.Format)
            .FirstOrDefault(candidate => candidate is not null);
        if (policyFormat is not null)
        {
            return policyFormat;
        }

        return fallbackRole is null ? null : ResolveParagraphFormat(rules, fallbackRole);
    }

    private static ParagraphFormatSample? ResolveTableOfContentsTitleFormat(TemplateProfile rules)
    {
        var format = ResolveParagraphFormat(rules, "toc.title")
            ?? ResolveParagraphFormat(rules, "heading1");
        if (format is null)
        {
            return new ParagraphFormatSample { Alignment = "center" };
        }

        var clone = OpenXmlFormatMerger.Clone(format);
        clone.StyleId = null;
        return clone;
    }

    private static TableFormatSample? ResolveTableFormat(TemplateProfile rules)
    {
        return rules.TablePolicy.Default?.Format
            ?? rules.TableArchetypes
                .OrderByDescending(candidate => candidate.Confidence)
                .Select(candidate => candidate.Format)
                .FirstOrDefault(candidate => candidate is not null);
    }

    private static bool RoleMatches(string? candidate, string role)
    {
        return string.Equals(candidate, role, StringComparison.OrdinalIgnoreCase);
    }

    private static uint NextDrawingId(MainDocumentPart mainPart, Body body)
    {
        var documentMax = mainPart.Document?
            .Descendants<WP.DocProperties>()
            .Select(properties => properties.Id?.Value ?? 0U)
            .DefaultIfEmpty(0U)
            .Max() ?? 0U;
        var bodyMax = body
            .Descendants<WP.DocProperties>()
            .Select(properties => properties.Id?.Value ?? 0U)
            .DefaultIfEmpty(0U)
            .Max();
        return Math.Max(documentMax, bodyMax) + 1U;
    }

    private static string RequiredTitle(ThesisContent content)
    {
        return string.IsNullOrWhiteSpace(content.Title) ? "论文题目" : content.Title;
    }

    private static string ToChineseOrdinal(int value)
    {
        return value switch
        {
            1 => "一",
            2 => "二",
            3 => "三",
            4 => "四",
            5 => "五",
            6 => "六",
            7 => "七",
            8 => "八",
            9 => "九",
            10 => "十",
            _ => value.ToString()
        };
    }

    private static string FormatChapterTitle(string title, int chapterNumber)
    {
        var trimmed = title.Trim();
        var spacedMatch = Regex.Match(
            trimmed,
            @"^第[一二三四五六七八九十百千万零〇两0-9Xx]+章\s+\S.*$",
            RegexOptions.CultureInvariant);
        if (spacedMatch.Success)
        {
            return trimmed;
        }

        var compactMatch = Regex.Match(
            trimmed,
            @"^(?<prefix>第[一二三四五六七八九十百千万零〇两0-9Xx]+章)(?<title>\S.*)$",
            RegexOptions.CultureInvariant);
        if (compactMatch.Success)
        {
            return $"{compactMatch.Groups["prefix"].Value} {compactMatch.Groups["title"].Value}";
        }

        return $"第{ToChineseOrdinal(chapterNumber)}章 {trimmed}";
    }

    private static string FormatSectionTitle(string title, int chapterNumber, int sectionNumber)
    {
        var trimmed = title.Trim();
        return Regex.IsMatch(trimmed, @"^\d{1,2}[\.．]\d{1,2}(?:[\.．]\d{1,2})?\s+\S+", RegexOptions.CultureInvariant)
            ? trimmed
            : $"{chapterNumber}.{sectionNumber} {trimmed}";
    }

    private static string StripReferenceNumber(string text)
    {
        return Regex.Replace(text.Trim(), @"^\s*\[\d+\]\s*", "", RegexOptions.CultureInvariant);
    }

    private static bool NeedsPreservedSpace(string text)
    {
        return text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
    }
}
