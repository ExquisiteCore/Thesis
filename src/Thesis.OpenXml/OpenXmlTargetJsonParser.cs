using System.Text.Json.Nodes;
using Thesis.Schema;

namespace Thesis.OpenXml;

internal sealed partial class OpenXmlTargetResolver
{
    private static ProfileRoleFormatMatch? CreateFormatMatch(JsonObject format, out string? error)
    {
        error = null;
        return new ProfileRoleFormatMatch
        {
            StyleId = GetString(format, "styleId", out error),
            Alignment = error is null ? GetString(format, "alignment", out error) : null,
            FontSizeHalfPoints = error is null ? GetString(format, "fontSizeHalfPoints", out error) : null,
            Bold = error is null ? GetBool(format, "bold", out error) : null,
            Italic = error is null ? GetBool(format, "italic", out error) : null,
            LineSpacing = error is null ? GetString(format, "lineSpacing", out error) : null,
            LineSpacingRule = error is null ? GetString(format, "lineSpacingRule", out error) : null,
            FirstLineIndentTwips = error is null ? CreateRange(format["firstLineIndentTwips"], out error) : null,
            LeftIndentTwips = error is null ? CreateRange(format["leftIndentTwips"], out error) : null,
            RightIndentTwips = error is null ? CreateRange(format["rightIndentTwips"], out error) : null
        };
    }

    private static IntRangeMatch? CreateRange(JsonNode? node, out string? error)
    {
        error = null;
        if (node is null)
        {
            return null;
        }

        try
        {
            if (node is JsonValue)
            {
                return new IntRangeMatch { Exact = node.GetValue<int>() };
            }

            if (node is not JsonObject obj)
            {
                error = "target_value_invalid";
                return null;
            }

            return new IntRangeMatch
            {
                Exact = GetInt(obj, "exact", out error),
                Min = error is null ? GetInt(obj, "min", out error) : null,
                Max = error is null ? GetInt(obj, "max", out error) : null
            };
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

    private static JsonObject? GetTargetObject(JsonNode? node, out string? error)
    {
        error = null;
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject targetObject)
        {
            return targetObject;
        }

        error = "target_value_invalid";
        return null;
    }

    private static string? GetString(JsonObject node, string propertyName, out string? error)
    {
        error = null;
        if (!node.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        return GetStringValue(value, out error);
    }

    private static string? GetStringValue(JsonNode value, out string? error)
    {
        error = null;
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

    private static int? GetInt(JsonObject node, string propertyName, out string? error)
    {
        error = null;
        if (!node.TryGetPropertyValue(propertyName, out var value) || value is null)
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

    private static bool? GetBool(JsonObject node, string propertyName, out string? error)
    {
        error = null;
        if (!node.TryGetPropertyValue(propertyName, out var value) || value is null)
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
}
