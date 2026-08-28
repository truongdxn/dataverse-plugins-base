using System.Text.Json;
using System.Text.Json.Serialization;
using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Configuration;

/// <summary>Shared JSON settings and file helpers, so every file in the repo reads and writes alike.</summary>
public static class JsonConfig
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Config files carry "$comment" keys for documentation; ignore anything unmapped.
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // Enums round-trip as names, which keeps the generated manifest reviewable in a diff.
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static T Read<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new ToolException($"Configuration file not found: {path}");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                   ?? throw new ToolException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new ToolException($"{path} is not valid JSON: {ex.Message}", ex);
        }
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    }
}
