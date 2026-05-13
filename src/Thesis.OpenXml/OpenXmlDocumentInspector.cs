using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Thesis.Schema;

namespace Thesis.OpenXml;

public static class OpenXmlDocumentInspector
{
    private const int PreviewLimit = 200;
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static bool TryInspect(string docxPath, out DocumentMap? documentMap, out Diagnostic? diagnostic)
    {
        try
        {
            documentMap = Inspect(docxPath);
            diagnostic = null;
            return true;
        }
        catch (Exception ex) when (IsExpectedInspectionFailure(ex))
        {
            documentMap = null;
            diagnostic = new Diagnostic
            {
                Severity = "warning",
                Code = "document_map_unavailable",
                Message = $"Working document could not be inspected as DOCX: {ex.Message}",
                Path = System.IO.Path.GetFullPath(docxPath)
            };
            return false;
        }
    }

    public static DocumentMap Inspect(string docxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);

        var fullPath = System.IO.Path.GetFullPath(docxPath);

        using var document = WordprocessingDocument.Open(fullPath, isEditable: false);
        var mainPart = document.MainDocumentPart
            ?? throw new InvalidDataException("DOCX does not contain a main document part.");
        var wordDocument = mainPart.Document
            ?? throw new InvalidDataException("DOCX does not contain a document.");
        var body = wordDocument.Body
            ?? throw new InvalidDataException("DOCX does not contain a document body.");

        var finalizationReasons = GetFinalizationReasons(body);
        var hostFinalization = OpenXmlFinalizationMetadata.Read(fullPath);
        var requiresFinalization = finalizationReasons.Count > 0
            && hostFinalization?.IsCurrent != true;
        var styleOutlineLevels = ReadStyleOutlineLevels(mainPart);

        return new DocumentMap
        {
            Path = fullPath,
            RequiresFinalization = requiresFinalization,
            FinalizationReasons = finalizationReasons,
            HostFinalization = hostFinalization,
            Paragraphs = ReadParagraphs(body, styleOutlineLevels),
            Styles = ReadStyles(mainPart, body),
            Numbering = ReadNumbering(mainPart),
            Sections = ReadSections(body),
            Tables = ReadTables(body),
            Comments = ReadComments(mainPart),
            RequirementHints = ReadRequirementHints(body, mainPart)
        };
    }

    private static List<DocumentParagraph> ReadParagraphs(Body body, IReadOnlyDictionary<string, int> styleOutlineLevels)
    {
        return body
            .Descendants<Paragraph>()
            .Where(paragraph => !paragraph.Ancestors<Table>().Any())
            .Where(paragraph => !IsFieldOnlyParagraph(paragraph))
            .Select((paragraph, index) => new DocumentParagraph
            {
                Index = index,
                Text = paragraph.InnerText,
                StyleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value,
                OutlineLevel = ReadParagraphOutlineLevel(paragraph, styleOutlineLevels),
                Format = ReadParagraphFormat(paragraph),
                Numbering = ReadParagraphNumbering(paragraph),
                Runs = ReadRuns(paragraph)
            })
            .ToList();
    }

    private static ParagraphFormatSample ReadParagraphFormat(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        var spacing = properties?.SpacingBetweenLines;
        var indentation = properties?.Indentation;
        var runFormat = paragraph
            .Descendants<Run>()
            .Select(ReadRunFormat)
            .FirstOrDefault(HasAnyRunFormat);

        return new ParagraphFormatSample
        {
            StyleId = properties?.ParagraphStyleId?.Val?.Value,
            Alignment = LowerInnerText(properties?.Justification?.Val),
            SpacingBeforeTwips = ToInt(spacing?.Before),
            SpacingAfterTwips = ToInt(spacing?.After),
            LineSpacing = spacing?.Line?.Value,
            LineSpacingRule = LowerInnerText(spacing?.LineRule),
            FirstLineIndentTwips = ToInt(indentation?.FirstLine),
            LeftIndentTwips = ToInt(indentation?.Left),
            RightIndentTwips = ToInt(indentation?.Right),
            RunFormat = runFormat
        };
    }

    private static NumberingReference? ReadParagraphNumbering(Paragraph paragraph)
    {
        var numberingProperties = paragraph.ParagraphProperties?.NumberingProperties;
        var numberingId = numberingProperties?.NumberingId?.Val is null
            ? null
            : numberingProperties.NumberingId.Val.Value.ToString();
        var level = numberingProperties?.NumberingLevelReference?.Val is null
            ? null
            : numberingProperties.NumberingLevelReference.Val.Value.ToString();

        if (numberingId is null && level is null)
        {
            return null;
        }

        return new NumberingReference
        {
            NumberingId = numberingId,
            Level = level
        };
    }

    private static List<DocumentRun> ReadRuns(Paragraph paragraph)
    {
        return paragraph
            .Descendants<Run>()
            .Select((run, index) =>
            {
                var format = ReadRunFormat(run);

                return new DocumentRun
                {
                    Index = index,
                    Text = run.InnerText,
                    Bold = format.Bold == true,
                    Italic = format.Italic == true,
                    FontSizeHalfPoints = format.FontSizeHalfPoints,
                    AsciiFont = format.AsciiFont,
                    HighAnsiFont = format.HighAnsiFont,
                    EastAsiaFont = format.EastAsiaFont,
                    ComplexScriptFont = format.ComplexScriptFont
                };
            })
            .ToList();
    }

    private static RunFormatSample ReadRunFormat(Run run)
    {
        var properties = run.RunProperties;
        var fonts = properties?.RunFonts;

        return new RunFormatSample
        {
            Bold = ReadOnOffValue(properties?.Bold),
            Italic = ReadOnOffValue(properties?.Italic),
            FontSizeHalfPoints = properties?.FontSize?.Val?.Value
                ?? properties?.FontSizeComplexScript?.Val?.Value,
            AsciiFont = fonts?.Ascii?.Value,
            HighAnsiFont = fonts?.HighAnsi?.Value,
            EastAsiaFont = fonts?.EastAsia?.Value,
            ComplexScriptFont = fonts?.ComplexScript?.Value
        };
    }

    private static bool HasAnyRunFormat(RunFormatSample format)
    {
        return format.Bold is not null
            || format.Italic is not null
            || format.FontSizeHalfPoints is not null
            || format.AsciiFont is not null
            || format.HighAnsiFont is not null
            || format.EastAsiaFont is not null
            || format.ComplexScriptFont is not null;
    }

    private static bool? ReadOnOffValue(OnOffType? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Val is null)
        {
            return true;
        }

        return value.Val.Value;
    }

    private static List<DocumentStyle> ReadStyles(MainDocumentPart mainPart, Body body)
    {
        var styleUsage = body.Descendants<Paragraph>()
            .Select(paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value)
            .Where(styleId => !string.IsNullOrWhiteSpace(styleId))
            .GroupBy(styleId => styleId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return mainPart.StyleDefinitionsPart?.Styles?
            .Elements<Style>()
            .Select(style =>
            {
                var styleId = style.StyleId?.Value;

                return new DocumentStyle
                {
                    StyleId = styleId,
                    Name = style.StyleName?.Val?.Value,
                    Type = LowerInnerText(style.Type),
                    BasedOn = style.BasedOn?.Val?.Value,
                    UsageCount = styleId is not null && styleUsage.TryGetValue(styleId, out var count) ? count : 0
                };
            })
            .ToList()
            ?? [];
    }

    private static Dictionary<string, int> ReadStyleOutlineLevels(MainDocumentPart mainPart)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles is null)
        {
            return result;
        }

        foreach (var style in styles.Elements<Style>())
        {
            var styleId = style.StyleId?.Value;
            var outlineLevel = ToInt(style.GetFirstChild<StyleParagraphProperties>()?
                .GetFirstChild<OutlineLevel>()?.Val);
            if (string.IsNullOrWhiteSpace(styleId) || outlineLevel is null)
            {
                continue;
            }

            result[styleId] = outlineLevel.Value;
        }

        return result;
    }

    private static int? ReadParagraphOutlineLevel(
        Paragraph paragraph,
        IReadOnlyDictionary<string, int> styleOutlineLevels)
    {
        var directOutlineLevel = ToInt(paragraph.ParagraphProperties?.OutlineLevel?.Val);
        if (directOutlineLevel is not null)
        {
            return directOutlineLevel;
        }

        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return styleId is not null && styleOutlineLevels.TryGetValue(styleId, out var styleOutlineLevel)
            ? styleOutlineLevel
            : null;
    }

    private static List<DocumentNumbering> ReadNumbering(MainDocumentPart mainPart)
    {
        var numbering = mainPart.NumberingDefinitionsPart?.Numbering;
        if (numbering is null)
        {
            return [];
        }

        var abstractNumbers = numbering.Elements<AbstractNum>()
            .Where(abstractNumber => abstractNumber.AbstractNumberId?.Value is not null)
            .ToDictionary(
                abstractNumber => abstractNumber.AbstractNumberId!.Value!.ToString(),
                abstractNumber => abstractNumber,
                StringComparer.Ordinal);

        return numbering
            .Elements<NumberingInstance>()
            .Select(instance => new DocumentNumbering
            {
                NumberingId = instance.NumberID?.Value.ToString(),
                AbstractNumberingId = instance.AbstractNumId?.Val?.Value.ToString(),
                Levels = ReadNumberingLevels(instance, abstractNumbers)
            })
            .ToList();
    }

    private static List<DocumentNumberingLevel> ReadNumberingLevels(
        NumberingInstance instance,
        IReadOnlyDictionary<string, AbstractNum> abstractNumbers)
    {
        var abstractNumberingId = instance.AbstractNumId?.Val?.Value.ToString();
        if (abstractNumberingId is null || !abstractNumbers.TryGetValue(abstractNumberingId, out var abstractNumber))
        {
            return [];
        }

        return abstractNumber
            .Elements<Level>()
            .Select(level => new DocumentNumberingLevel
            {
                Level = level.LevelIndex is null ? null : level.LevelIndex.Value.ToString(),
                Format = LowerInnerText(level.NumberingFormat?.Val),
                Text = level.LevelText?.Val?.Value
            })
            .ToList();
    }

    private static List<DocumentSection> ReadSections(Body body)
    {
        var sectionProperties = body.Descendants<SectionProperties>().ToList();

        return sectionProperties
            .Select((section, index) => new DocumentSection
            {
                Index = index,
                PageSize = ReadPageSize(section.GetFirstChild<PageSize>()),
                PageMargin = ReadPageMargin(section.GetFirstChild<PageMargin>()),
                Headers = ReadHeaders(section),
                Footers = ReadFooters(section)
            })
            .ToList();
    }

    private static PageSizeInfo? ReadPageSize(PageSize? pageSize)
    {
        if (pageSize is null)
        {
            return null;
        }

        return new PageSizeInfo
        {
            WidthTwips = ToInt(pageSize.Width),
            HeightTwips = ToInt(pageSize.Height),
            Orientation = LowerInnerText(pageSize.Orient)
        };
    }

    private static PageMarginInfo? ReadPageMargin(PageMargin? pageMargin)
    {
        if (pageMargin is null)
        {
            return null;
        }

        return new PageMarginInfo
        {
            TopTwips = ToInt(pageMargin.Top),
            RightTwips = ToInt(pageMargin.Right),
            BottomTwips = ToInt(pageMargin.Bottom),
            LeftTwips = ToInt(pageMargin.Left),
            HeaderTwips = ToInt(pageMargin.Header),
            FooterTwips = ToInt(pageMargin.Footer),
            GutterTwips = ToInt(pageMargin.Gutter)
        };
    }

    private static List<HeaderFooterReference> ReadHeaders(SectionProperties section)
    {
        return section
            .Elements<HeaderReference>()
            .Select(header => new HeaderFooterReference
            {
                Type = LowerInnerText(header.Type),
                RelationshipId = header.Id?.Value
            })
            .ToList();
    }

    private static List<HeaderFooterReference> ReadFooters(SectionProperties section)
    {
        return section
            .Elements<FooterReference>()
            .Select(footer => new HeaderFooterReference
            {
                Type = LowerInnerText(footer.Type),
                RelationshipId = footer.Id?.Value
            })
            .ToList();
    }

    private static List<DocumentTable> ReadTables(Body body)
    {
        return body
            .Descendants<Table>()
            .Select((table, index) =>
            {
                var rows = table.Elements<TableRow>().ToList();

                return new DocumentTable
                {
                    Index = index,
                    RowCount = rows.Count,
                    CellCounts = rows
                        .Select(row => row.Elements<TableCell>().Count())
                        .ToList(),
                    TextPreview = Preview(string.Join(" ", rows.SelectMany(row =>
                        row.Elements<TableCell>().Select(cell => cell.InnerText)))),
                    Format = ReadTableFormat(table, rows)
                };
            })
            .ToList();
    }

    private static List<DocumentComment> ReadComments(MainDocumentPart mainPart)
    {
        return mainPart.WordprocessingCommentsPart?.Comments?
            .Elements<Comment>()
            .Select(comment => new DocumentComment
            {
                Id = comment.Id?.Value,
                Author = comment.Author?.Value,
                Text = comment.InnerText
            })
            .Where(comment => !string.IsNullOrWhiteSpace(comment.Text))
            .ToList()
            ?? [];
    }

    private static List<DocumentRequirementHint> ReadRequirementHints(Body body, MainDocumentPart mainPart)
    {
        var hints = new List<DocumentRequirementHint>();

        foreach (var (paragraph, index) in body
            .Descendants<Paragraph>()
            .Where(paragraph => !paragraph.Ancestors<Table>().Any())
            .Select((paragraph, index) => (paragraph, index)))
        {
            var text = paragraph.InnerText;
            if (LooksLikeRequirement(text))
            {
                hints.Add(new DocumentRequirementHint
                {
                    Source = "paragraph",
                    ParagraphIndex = index,
                    Text = Preview(text)
                });
            }
        }

        foreach (var comment in ReadComments(mainPart))
        {
            if (LooksLikeRequirement(comment.Text))
            {
                hints.Add(new DocumentRequirementHint
                {
                    Source = "comment",
                    CommentId = comment.Id,
                    Text = Preview(comment.Text)
                });
            }
        }

        return hints;
    }

    private static bool LooksLikeRequirement(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var keywords = new[]
        {
            "格式", "要求", "应", "须", "必须", "字体", "字号", "行距", "三线表",
            "首行缩进", "页边距", "目录", "参考文献", "页码", "标题", "正文"
        };
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static TableFormatSample ReadTableFormat(Table table, IReadOnlyList<TableRow> rows)
    {
        var properties = table.TableProperties;
        var width = properties?.TableWidth;
        var firstCellParagraph = rows
            .SelectMany(row => row.Elements<TableCell>())
            .SelectMany(cell => cell.Elements<Paragraph>())
            .FirstOrDefault();

        return new TableFormatSample
        {
            WidthTwips = ToInt(width?.Width),
            WidthType = LowerInnerText(width?.Type),
            Alignment = LowerInnerText(properties?.TableJustification?.Val),
            GridColumnWidthsTwips = [.. table.TableGrid?
                .Elements<GridColumn>()
                .Select(column => ToInt(column.Width))
                .OfType<int>() ?? []],
            Borders = ReadTableBorders(properties?.TableBorders),
            CellMargins = ReadTableCellMargins(properties?.TableCellMarginDefault),
            HeaderRowCount = rows.Count(row => row.TableRowProperties?.GetFirstChild<TableHeader>() is not null),
            FirstCellParagraphFormat = firstCellParagraph is null ? null : ReadParagraphFormat(firstCellParagraph)
        };
    }

    private static TableBordersSample? ReadTableBorders(TableBorders? borders)
    {
        if (borders is null)
        {
            return null;
        }

        return new TableBordersSample
        {
            Top = ReadBorderLine(borders.TopBorder),
            Bottom = ReadBorderLine(borders.BottomBorder),
            Left = ReadBorderLine(borders.LeftBorder ?? (OpenXmlElement?)borders.StartBorder),
            Right = ReadBorderLine(borders.RightBorder ?? (OpenXmlElement?)borders.EndBorder),
            InsideHorizontal = ReadBorderLine(borders.InsideHorizontalBorder),
            InsideVertical = ReadBorderLine(borders.InsideVerticalBorder)
        };
    }

    private static TableBorderLineSample? ReadBorderLine(OpenXmlElement? border)
    {
        if (border is null)
        {
            return null;
        }

        return new TableBorderLineSample
        {
            Value = Lower(GetWordprocessingAttribute(border, "val")),
            Size = GetWordprocessingAttribute(border, "sz"),
            Color = GetWordprocessingAttribute(border, "color"),
            Space = GetWordprocessingAttribute(border, "space")
        };
    }

    private static TableCellMarginsSample? ReadTableCellMargins(TableCellMarginDefault? margins)
    {
        if (margins is null)
        {
            return null;
        }

        return new TableCellMarginsSample
        {
            TopTwips = ToInt(GetWordprocessingAttribute(margins.TopMargin, "w")),
            RightTwips = ToInt(GetWordprocessingAttribute(margins.TableCellRightMargin, "w")
                ?? GetWordprocessingAttribute(margins.EndMargin, "w")),
            BottomTwips = ToInt(GetWordprocessingAttribute(margins.BottomMargin, "w")),
            LeftTwips = ToInt(GetWordprocessingAttribute(margins.TableCellLeftMargin, "w")
                ?? GetWordprocessingAttribute(margins.StartMargin, "w"))
        };
    }

    private static List<string> GetFinalizationReasons(Body body)
    {
        var reasons = new List<string>();

        if (body.Descendants<FieldChar>().Any()
            || body.Descendants<FieldCode>().Any()
            || body.Descendants<SimpleField>().Any())
        {
            reasons.Add("fields");
        }

        if (body.Descendants<FieldCode>().Any(field => IsTocInstruction(field.Text))
            || body.Descendants<SimpleField>().Any(field => IsTocInstruction(field.Instruction?.Value)))
        {
            reasons.Add("toc");
        }

        return reasons;
    }

    private static bool IsTocInstruction(string? value)
    {
        return value?.TrimStart().StartsWith("TOC", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsFieldOnlyParagraph(Paragraph paragraph)
    {
        var hasFields = paragraph.Descendants<FieldChar>().Any()
            || paragraph.Descendants<FieldCode>().Any()
            || paragraph.Descendants<SimpleField>().Any();
        return hasFields && !paragraph.Descendants<Text>().Any(text => !string.IsNullOrWhiteSpace(text.Text));
    }

    private static string? LowerInnerText(OpenXmlSimpleType? value)
    {
        return Lower(value?.InnerText);
    }

    private static string? Lower(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.ToLowerInvariant();
    }

    private static string? GetWordprocessingAttribute(OpenXmlElement? element, string localName)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var attribute in element.GetAttributes())
        {
            if (string.Equals(attribute.LocalName, localName, StringComparison.Ordinal)
                && string.Equals(attribute.NamespaceUri, WordprocessingNamespace, StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(attribute.Value) ? null : attribute.Value;
            }
        }

        return null;
    }

    private static bool IsExpectedInspectionFailure(Exception ex)
    {
        return ex is InvalidDataException
            or FileFormatException
            or OpenXmlPackageException
            or IOException
            or UnauthorizedAccessException;
    }

    private static int? ToInt(UInt32Value? value)
    {
        return value?.Value is null ? null : checked((int)value.Value);
    }

    private static int? ToInt(Int32Value? value)
    {
        return value?.Value;
    }

    private static int? ToInt(StringValue? value)
    {
        return int.TryParse(value?.Value, out var result) ? result : null;
    }

    private static int? ToInt(string? value)
    {
        return int.TryParse(value, out var result) ? result : null;
    }

    private static string Preview(string text)
    {
        if (text.Length <= PreviewLimit)
        {
            return text;
        }

        return text[..PreviewLimit];
    }
}
