using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuackDuck;

internal sealed class AppSettings
{
    [JsonPropertyName("skin")]
    public string Skin { get; init; } = "default";

    [JsonPropertyName("cursor_hunt_chance_percent")]
    public int CursorHuntChancePercent { get; init; } = 10;

    [JsonPropertyName("random_sound_chance_percent")]
    public int RandomSoundChancePercent { get; init; } = 25;

    [JsonPropertyName("debug")]
    public bool Debug { get; init; }

    internal static AppSettings Load()
    {
        var baseDir = AppContext.BaseDirectory;
        var runtimePath = Path.Combine(baseDir, "appsettings.json");
        var devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "appsettings.json"));
        var path = File.Exists(runtimePath) ? runtimePath : devPath;

        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var settings = JsonSerializer.Deserialize<AppSettings>(json, options);
            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}
