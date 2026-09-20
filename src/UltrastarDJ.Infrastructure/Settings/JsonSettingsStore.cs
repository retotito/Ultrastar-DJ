using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;

namespace UltrastarDJ.Infrastructure.Settings;

/// <summary>One JSON file per document in <see cref="AppPaths.Settings"/>.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppPaths _paths;
    private readonly ILogger<JsonSettingsStore> _log;

    public JsonSettingsStore(AppPaths paths, ILogger<JsonSettingsStore> log)
    {
        _paths = paths;
        _log = log;
    }

    public T Load<T>(string name, T defaults) where T : class
    {
        string path = PathFor(name);
        if (!File.Exists(path))
        {
            return defaults;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options) ?? defaults;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt settings file must never prevent startup; the user gets defaults and a log entry.
            _log.LogWarning(ex, "Settings document {Name} unreadable, using defaults", name);
            return defaults;
        }
    }

    public void Save<T>(string name, T value) where T : class
    {
        string path = PathFor(name);
        string temp = path + ".tmp";
        using (FileStream stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, value, Options);
        }

        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(string name) => Path.Combine(_paths.Settings, name + ".json");
}
