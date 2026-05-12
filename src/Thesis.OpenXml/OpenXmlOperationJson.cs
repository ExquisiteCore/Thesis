using System.Text.Json.Nodes;

namespace Thesis.OpenXml;

internal static class OpenXmlOperationJson
{
    public static string? GetString(JsonNode? node, string propertyName)
    {
        return GetString(node, propertyName, out _);
    }

    public static string? GetString(JsonNode? node, string propertyName, out string? error)
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

    public static bool? GetBool(JsonNode? node, string propertyName, out string? error)
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

    public static int? GetInt(JsonNode? node, string propertyName, out string? error)
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

    public static string GetPosition(JsonNode? node, string defaultValue, out string? error)
    {
        var position = GetString(node, "position", out error) ?? defaultValue;
        if (error is not null)
        {
            return defaultValue;
        }

        if (position is not "before" and not "after")
        {
            error = "target_value_invalid";
            return defaultValue;
        }

        return position;
    }

    public static List<string> GetStringArray(JsonNode? node, string propertyName, out string? error)
    {
        error = null;
        var value = node?[propertyName];
        if (value is null)
        {
            return [];
        }

        if (value is not JsonArray array)
        {
            error = "target_value_invalid";
            return [];
        }

        var result = new List<string>();
        foreach (var item in array)
        {
            try
            {
                result.Add(item?.GetValue<string>() ?? "");
            }
            catch (InvalidOperationException)
            {
                error = "target_value_invalid";
                return [];
            }
            catch (FormatException)
            {
                error = "target_value_invalid";
                return [];
            }
        }

        return result;
    }
}
