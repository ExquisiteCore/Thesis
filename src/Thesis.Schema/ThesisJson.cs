using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thesis.Schema;

public static class ThesisJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new JsonException($"Unable to deserialize {typeof(T).Name}.");
    }

    public static T DeserializeStrict<T>(string json)
    {
        var strict = new JsonSerializerOptions(Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        return JsonSerializer.Deserialize<T>(json, strict) ?? throw new JsonException($"Expected a {typeof(T).Name} object.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
