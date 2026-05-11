using System.Text.Json.Nodes;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal static class OpenXmlOperationFormatBuilder
{
    public static bool TryCreateEffectiveFormat(
        IReadOnlySet<string> paragraphStyleIds,
        ParagraphFormatSample profileFormat,
        JsonNode? overrideFormat,
        out ParagraphFormatSample format,
        out string error)
    {
        format = OpenXmlFormatMerger.Clone(profileFormat);
        error = "";

        if (overrideFormat is not null && overrideFormat is not JsonObject)
        {
            error = "target_value_invalid";
            return false;
        }

        if (!ApplyParagraphOverride(overrideFormat, format, "styleId", (target, value) => target.StyleId = value, out error)
            || !ApplyParagraphOverride(overrideFormat, format, "alignment", (target, value) => target.Alignment = value, out error)
            || !ApplyParagraphOverride(overrideFormat, format, "lineSpacing", (target, value) => target.LineSpacing = value, out error)
            || !ApplyParagraphOverride(overrideFormat, format, "lineSpacingRule", (target, value) => target.LineSpacingRule = value, out error)
            || !ApplyIntParagraphOverride(overrideFormat, format, "spacingBeforeTwips", (target, value) => target.SpacingBeforeTwips = value, out error)
            || !ApplyIntParagraphOverride(overrideFormat, format, "spacingAfterTwips", (target, value) => target.SpacingAfterTwips = value, out error)
            || !ApplyIntParagraphOverride(overrideFormat, format, "firstLineIndentTwips", (target, value) => target.FirstLineIndentTwips = value, out error)
            || !ApplyIntParagraphOverride(overrideFormat, format, "leftIndentTwips", (target, value) => target.LeftIndentTwips = value, out error)
            || !ApplyIntParagraphOverride(overrideFormat, format, "rightIndentTwips", (target, value) => target.RightIndentTwips = value, out error)
            || !ApplyRunStringOverride(overrideFormat, format, "fontSizeHalfPoints", (target, value) => target.FontSizeHalfPoints = value, out error)
            || !ApplyRunStringOverride(overrideFormat, format, "asciiFont", (target, value) => target.AsciiFont = value, out error)
            || !ApplyRunStringOverride(overrideFormat, format, "highAnsiFont", (target, value) => target.HighAnsiFont = value, out error)
            || !ApplyRunStringOverride(overrideFormat, format, "eastAsiaFont", (target, value) => target.EastAsiaFont = value, out error)
            || !ApplyRunStringOverride(overrideFormat, format, "complexScriptFont", (target, value) => target.ComplexScriptFont = value, out error)
            || !ApplyRunBoolOverride(overrideFormat, format, "bold", (target, value) => target.Bold = value, out error)
            || !ApplyRunBoolOverride(overrideFormat, format, "italic", (target, value) => target.Italic = value, out error))
        {
            return false;
        }

        if (!TryValidateParagraphFormat(paragraphStyleIds, format, out error))
        {
            return false;
        }

        format.Alignment = NormalizeAlignment(format.Alignment);
        format.LineSpacingRule = NormalizeLineSpacingRule(format.LineSpacingRule);
        return true;
    }

    public static bool TryCreateEffectiveTableFormat(
        IReadOnlySet<string> paragraphStyleIds,
        TableFormatSample profileFormat,
        JsonNode? overrideFormat,
        out TableFormatSample format,
        out string error)
    {
        format = OpenXmlFormatMerger.Clone(profileFormat);
        error = "";

        if (overrideFormat is not null && overrideFormat is not JsonObject)
        {
            error = "target_value_invalid";
            return false;
        }

        if (!ApplyTableStringOverride(overrideFormat, format, "widthType", (target, value) => target.WidthType = value, out error)
            || !ApplyTableStringOverride(overrideFormat, format, "alignment", (target, value) => target.Alignment = value, out error)
            || !ApplyTableIntOverride(overrideFormat, format, "widthTwips", (target, value) => target.WidthTwips = value, out error)
            || !ApplyTableIntOverride(overrideFormat, format, "headerRowCount", (target, value) => target.HeaderRowCount = value, out error)
            || !ApplyGridColumnWidthsOverride(overrideFormat, format, out error)
            || !ApplyTableBordersOverride(overrideFormat, format, out error)
            || !ApplyTableCellMarginsOverride(overrideFormat, format, out error))
        {
            return false;
        }

        if (!TryValidateTableFormat(paragraphStyleIds, format, out error))
        {
            return false;
        }

        format.WidthType = NormalizeTableWidthType(format.WidthType);
        format.Alignment = NormalizeAlignment(format.Alignment);
        if (format.FirstCellParagraphFormat is not null)
        {
            format.FirstCellParagraphFormat.Alignment = NormalizeAlignment(format.FirstCellParagraphFormat.Alignment);
            format.FirstCellParagraphFormat.LineSpacingRule = NormalizeLineSpacingRule(format.FirstCellParagraphFormat.LineSpacingRule);
        }

        return true;
    }

    public static bool ApplyTableBordersOverride(JsonNode? overrideFormat, TableFormatSample format, out string error)
    {
        error = "";
        var bordersNode = overrideFormat?["borders"];
        if (bordersNode is null)
        {
            return true;
        }

        if (bordersNode is not JsonObject borders)
        {
            error = "target_value_invalid";
            return false;
        }

        format.Borders ??= new TableBordersSample();
        return ApplyBorderOverride(borders, "top", format.Borders.Top, value => format.Borders.Top = value, out error)
            && ApplyBorderOverride(borders, "bottom", format.Borders.Bottom, value => format.Borders.Bottom = value, out error)
            && ApplyBorderOverride(borders, "left", format.Borders.Left, value => format.Borders.Left = value, out error)
            && ApplyBorderOverride(borders, "right", format.Borders.Right, value => format.Borders.Right = value, out error)
            && ApplyBorderOverride(borders, "insideHorizontal", format.Borders.InsideHorizontal, value => format.Borders.InsideHorizontal = value, out error)
            && ApplyBorderOverride(borders, "insideVertical", format.Borders.InsideVertical, value => format.Borders.InsideVertical = value, out error);
    }

    public static bool IsValidHalfPointSize(string value)
    {
        return int.TryParse(value, out var size) && size > 0 && size <= 1638;
    }

    public static bool IsValidTwips(int? value)
    {
        return value is null or >= 0;
    }

    public static bool IsValidTableBorders(TableBordersSample? borders)
    {
        return borders is null
            || (IsValidTableBorderLine(borders.Top)
                && IsValidTableBorderLine(borders.Bottom)
                && IsValidTableBorderLine(borders.Left)
                && IsValidTableBorderLine(borders.Right)
                && IsValidTableBorderLine(borders.InsideHorizontal)
                && IsValidTableBorderLine(borders.InsideVertical));
    }

    private static bool ApplyParagraphOverride(
        JsonNode? overrideFormat,
        ParagraphFormatSample format,
        string propertyName,
        Action<ParagraphFormatSample, string> apply,
        out string error)
    {
        var value = GetString(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(format, value);
        }

        error = "";
        return true;
    }

    private static bool ApplyIntParagraphOverride(
        JsonNode? overrideFormat,
        ParagraphFormatSample format,
        string propertyName,
        Action<ParagraphFormatSample, int> apply,
        out string error)
    {
        var value = GetInt(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(format, value.Value);
        }

        error = "";
        return true;
    }

    private static bool ApplyRunStringOverride(
        JsonNode? overrideFormat,
        ParagraphFormatSample format,
        string propertyName,
        Action<RunFormatSample, string> apply,
        out string error)
    {
        var value = GetString(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            format.RunFormat ??= new RunFormatSample();
            apply(format.RunFormat, value);
        }

        error = "";
        return true;
    }

    private static bool ApplyRunBoolOverride(
        JsonNode? overrideFormat,
        ParagraphFormatSample format,
        string propertyName,
        Action<RunFormatSample, bool> apply,
        out string error)
    {
        var value = GetBool(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            format.RunFormat ??= new RunFormatSample();
            apply(format.RunFormat, value.Value);
        }

        error = "";
        return true;
    }

    private static bool ApplyTableStringOverride(
        JsonNode? overrideFormat,
        TableFormatSample format,
        string propertyName,
        Action<TableFormatSample, string> apply,
        out string error)
    {
        var value = GetString(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(format, value);
        }

        error = "";
        return true;
    }

    private static bool ApplyTableIntOverride(
        JsonNode? overrideFormat,
        TableFormatSample format,
        string propertyName,
        Action<TableFormatSample, int> apply,
        out string error)
    {
        var value = GetInt(overrideFormat, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(format, value.Value);
        }

        error = "";
        return true;
    }

    private static bool ApplyGridColumnWidthsOverride(JsonNode? overrideFormat, TableFormatSample format, out string error)
    {
        error = "";
        var value = overrideFormat?["gridColumnWidthsTwips"];
        if (value is null)
        {
            return true;
        }

        if (value is not JsonArray widths)
        {
            error = "target_value_invalid";
            return false;
        }

        var parsed = new List<int>();
        foreach (var item in widths)
        {
            if (!TryGetJsonValue(item, out int width))
            {
                error = "target_value_invalid";
                return false;
            }

            parsed.Add(width);
        }

        format.GridColumnWidthsTwips = parsed;
        return true;
    }

    private static bool ApplyBorderOverride(
        JsonObject borders,
        string propertyName,
        TableBorderLineSample? target,
        Action<TableBorderLineSample> assign,
        out string error)
    {
        error = "";
        if (!borders.TryGetPropertyValue(propertyName, out var borderNode) || borderNode is null)
        {
            return true;
        }

        if (borderNode is not JsonObject border)
        {
            error = "target_value_invalid";
            return false;
        }

        target ??= new TableBorderLineSample();
        assign(target);
        return ApplyBorderStringOverride(border, target, "value", (line, value) => line.Value = value, out error)
            && ApplyBorderStringOverride(border, target, "size", (line, value) => line.Size = value, out error)
            && ApplyBorderStringOverride(border, target, "color", (line, value) => line.Color = value, out error)
            && ApplyBorderStringOverride(border, target, "space", (line, value) => line.Space = value, out error);
    }

    private static bool ApplyBorderStringOverride(
        JsonObject border,
        TableBorderLineSample line,
        string propertyName,
        Action<TableBorderLineSample, string> apply,
        out string error)
    {
        var value = GetString(border, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(line, value);
        }

        error = "";
        return true;
    }

    private static bool ApplyTableCellMarginsOverride(JsonNode? overrideFormat, TableFormatSample format, out string error)
    {
        error = "";
        var marginsNode = overrideFormat?["cellMargins"];
        if (marginsNode is null)
        {
            return true;
        }

        if (marginsNode is not JsonObject margins)
        {
            error = "target_value_invalid";
            return false;
        }

        format.CellMargins ??= new TableCellMarginsSample();
        return ApplyMarginOverride(margins, format.CellMargins, "topTwips", (target, value) => target.TopTwips = value, out error)
            && ApplyMarginOverride(margins, format.CellMargins, "rightTwips", (target, value) => target.RightTwips = value, out error)
            && ApplyMarginOverride(margins, format.CellMargins, "bottomTwips", (target, value) => target.BottomTwips = value, out error)
            && ApplyMarginOverride(margins, format.CellMargins, "leftTwips", (target, value) => target.LeftTwips = value, out error);
    }

    private static bool ApplyMarginOverride(
        JsonObject margins,
        TableCellMarginsSample target,
        string propertyName,
        Action<TableCellMarginsSample, int> apply,
        out string error)
    {
        var value = GetInt(margins, propertyName, out var valueError);
        if (valueError is not null)
        {
            error = valueError;
            return false;
        }

        if (value is not null)
        {
            apply(target, value.Value);
        }

        error = "";
        return true;
    }

    private static bool TryValidateTableFormat(IReadOnlySet<string> paragraphStyleIds, TableFormatSample format, out string error)
    {
        error = "";
        if (!IsValidTwips(format.WidthTwips)
            || format.GridColumnWidthsTwips.Any(width => !IsValidTwips(width))
            || format.HeaderRowCount < 0
            || !IsValidTableWidthType(format.WidthType)
            || !IsValidAlignment(format.Alignment)
            || !IsValidTableCellMargins(format.CellMargins)
            || !IsValidTableBorders(format.Borders))
        {
            error = "format_value_invalid";
            return false;
        }

        if (format.FirstCellParagraphFormat is not null
            && !TryValidateParagraphFormat(paragraphStyleIds, format.FirstCellParagraphFormat, out error))
        {
            return false;
        }

        return true;
    }

    private static bool TryValidateParagraphFormat(IReadOnlySet<string> paragraphStyleIds, ParagraphFormatSample format, out string error)
    {
        error = "";
        if (!string.IsNullOrWhiteSpace(format.StyleId) && !paragraphStyleIds.Contains(format.StyleId))
        {
            error = "paragraph_style_missing";
            return false;
        }

        if (format.RunFormat?.FontSizeHalfPoints is not null && !IsValidHalfPointSize(format.RunFormat.FontSizeHalfPoints))
        {
            error = "font_size_invalid";
            return false;
        }

        if (!IsValidAlignment(format.Alignment)
            || !IsValidLineSpacingRule(format.LineSpacingRule)
            || !IsValidTwips(format.SpacingBeforeTwips)
            || !IsValidTwips(format.SpacingAfterTwips)
            || !IsValidTwips(format.FirstLineIndentTwips)
            || !IsValidTwips(format.LeftIndentTwips)
            || !IsValidTwips(format.RightIndentTwips))
        {
            error = "format_value_invalid";
            return false;
        }

        return true;
    }

    private static bool IsValidAlignment(string? value)
    {
        return NormalizeAlignment(value) is not "\0";
    }

    private static bool IsValidLineSpacingRule(string? value)
    {
        return NormalizeLineSpacingRule(value) is not "\0";
    }

    private static bool IsValidTableCellMargins(TableCellMarginsSample? margins)
    {
        return margins is null
            || (IsValidTwips(margins.TopTwips)
                && IsValidTwips(margins.RightTwips)
                && IsValidTwips(margins.BottomTwips)
                && IsValidTwips(margins.LeftTwips));
    }

    private static bool IsValidTableBorderLine(TableBorderLineSample? line)
    {
        return line is null
            || (IsValidBorderString(line.Value)
                && IsValidBorderUInt(line.Size)
                && IsValidBorderColor(line.Color)
                && IsValidBorderUInt(line.Space));
    }

    private static bool IsValidBorderString(string? value)
    {
        return value is null || !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsValidBorderUInt(string? value)
    {
        return value is null || uint.TryParse(value, out _);
    }

    private static bool IsValidBorderColor(string? value)
    {
        return value is null
            || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
            || (value.Length == 6 && value.All(Uri.IsHexDigit));
    }

    private static bool IsValidTableWidthType(string? value)
    {
        return NormalizeTableWidthType(value) is not "\0";
    }

    private static string? NormalizeAlignment(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            null => null,
            "left" => "left",
            "center" => "center",
            "right" => "right",
            "both" => "both",
            "distribute" => "distribute",
            "mediumkashida" => "mediumKashida",
            "numtab" => "numTab",
            "highkashida" => "highKashida",
            "lowkashida" => "lowKashida",
            "thaidistribute" => "thaiDistribute",
            _ => "\0"
        };
    }

    private static string? NormalizeTableWidthType(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            null => null,
            "nil" => "nil",
            "pct" => "pct",
            "dxa" => "dxa",
            "auto" => "auto",
            _ => "\0"
        };
    }

    private static string? NormalizeLineSpacingRule(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            null => null,
            "auto" => "auto",
            "exact" => "exact",
            "atleast" => "atLeast",
            _ => "\0"
        };
    }

    private static string? GetString(JsonNode? node, string propertyName, out string? error)
    {
        error = null;
        var value = node?[propertyName];
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            error = "target_value_invalid";
            return null;
        }
        catch (FormatException)
        {
            error = "target_value_invalid";
            return null;
        }
    }

    private static bool? GetBool(JsonNode? node, string propertyName, out string? error)
    {
        error = null;
        var value = node?[propertyName];
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<bool>();
        }
        catch (InvalidOperationException)
        {
            error = "target_value_invalid";
            return null;
        }
        catch (FormatException)
        {
            error = "target_value_invalid";
            return null;
        }
    }

    private static int? GetInt(JsonNode? node, string propertyName, out string? error)
    {
        error = null;
        var value = node?[propertyName];
        if (value is null)
        {
            return null;
        }

        try
        {
            return value.GetValue<int>();
        }
        catch (InvalidOperationException)
        {
            error = "target_value_invalid";
            return null;
        }
        catch (FormatException)
        {
            error = "target_value_invalid";
            return null;
        }
    }

    private static bool TryGetJsonValue<T>(JsonNode? node, out T value)
    {
        value = default!;
        if (node is null)
        {
            return false;
        }

        try
        {
            value = node.GetValue<T>();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
