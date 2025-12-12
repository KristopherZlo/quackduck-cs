using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace QuackDuck;

/// <summary>
/// Loads skin configuration, slices the spritesheet, and exposes frames per animation.
/// </summary>
internal sealed class PetSkin : IDisposable
{
    private readonly Dictionary<string, Rectangle[]> animations;
    private bool disposed;

    private PetSkin(Bitmap spriteSheet, int frameWidth, int frameHeight, Dictionary<string, Rectangle[]> animations, IReadOnlyList<string> soundPaths)
    {
        SpriteSheet = spriteSheet;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        this.animations = animations;
        SoundPaths = soundPaths;
    }

    internal Bitmap SpriteSheet { get; }
    internal int FrameWidth { get; }
    internal int FrameHeight { get; }
    internal IReadOnlyList<string> SoundPaths { get; }

    internal static PetSkin Load(string skinName = "default")
    {
        var skinFolder = ResolveSkinPath(skinName);
        var configPath = Path.Combine(skinFolder, "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Skin config not found at {configPath}");
        }

        var config = ReadConfig(configPath);
        ValidateConfig(config);
        var sheetPath = Path.Combine(skinFolder, config.Spritesheet);
        if (!File.Exists(sheetPath))
        {
            throw new FileNotFoundException($"Spritesheet '{config.Spritesheet}' not found at {sheetPath}");
        }

        Bitmap? spriteSheet = null;
        try
        {
            spriteSheet = new Bitmap(sheetPath);
            var animations = BuildAnimations(config, spriteSheet.Width, spriteSheet.Height);
            var soundPaths = BuildSoundPaths(config, skinFolder);

            return new PetSkin(spriteSheet, config.FrameWidth, config.FrameHeight, animations, soundPaths);
        }
        catch
        {
            spriteSheet?.Dispose();
            throw;
        }
    }

    internal bool HasAnimation(string name) => animations.ContainsKey(name);

    internal string ResolveAnimationName(string requested)
    {
        if (animations.ContainsKey(requested))
        {
            return requested;
        }

        if (animations.ContainsKey("idle"))
        {
            return "idle";
        }

        return animations.Keys.First();
    }

    internal Rectangle[] GetFramesOrFallback(string animationName)
    {
        if (animations.TryGetValue(animationName, out var frames) && frames.Length > 0)
        {
            return frames;
        }

        var firstAvailable = animations.Values.FirstOrDefault(f => f.Length > 0);
        return firstAvailable ?? Array.Empty<Rectangle>();
    }

    private static Dictionary<string, Rectangle[]> BuildAnimations(SkinConfig config, int sheetWidth, int sheetHeight)
    {
        var animations = new Dictionary<string, Rectangle[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, frameTokens) in config.Animations)
        {
            if (frameTokens is null || frameTokens.Length == 0)
            {
                throw new InvalidOperationException($"Animation '{name}' must contain at least one frame.");
            }

            var frames = frameTokens.Select(token => ParseFrame(token, config.FrameWidth, config.FrameHeight)).ToArray();
            foreach (var frame in frames)
            {
                if (frame.Right > sheetWidth || frame.Bottom > sheetHeight)
                {
                    throw new InvalidOperationException($"Frame {frame} for animation '{name}' goes outside the spritesheet bounds.");
                }
            }

            animations[name] = frames;
        }

        return animations;
    }

    private static void ValidateConfig(SkinConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Spritesheet))
        {
            throw new InvalidOperationException("Skin config must specify a spritesheet file name.");
        }

        if (config.FrameWidth <= 0 || config.FrameHeight <= 0)
        {
            throw new InvalidOperationException("Frame width and height must be positive non-zero values.");
        }

        if (config.Animations.Count == 0)
        {
            throw new InvalidOperationException("Skin config must declare at least one animation.");
        }

        foreach (var (name, frames) in config.Animations)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Animation name cannot be empty.");
            }

            if (frames is null || frames.Length == 0)
            {
                throw new InvalidOperationException($"Animation '{name}' must list at least one frame.");
            }
        }
    }

    private static IReadOnlyList<string> BuildSoundPaths(SkinConfig config, string skinFolder)
    {
        var sounds = new List<string>();
        foreach (var file in config.SoundFiles)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                continue;
            }

            var path = Path.Combine(skinFolder, file);
            if (File.Exists(path))
            {
                sounds.Add(path);
            }
        }

        return sounds;
    }

    private static Rectangle ParseFrame(string token, int frameWidth, int frameHeight)
    {
        var parts = token.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var row) || !int.TryParse(parts[1], out var column))
        {
            throw new FormatException($"Frame '{token}' must be in 'row:column' format.");
        }

        if (row < 0 || column < 0)
        {
            throw new FormatException($"Frame '{token}' cannot use negative indices.");
        }

        return new Rectangle(column * frameWidth, row * frameHeight, frameWidth, frameHeight);
    }

    private static SkinConfig ReadConfig(string path)
    {
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        var config = JsonSerializer.Deserialize<SkinConfig>(json, options);
        if (config is null)
        {
            throw new InvalidOperationException("Skin config could not be parsed.");
        }

        return config;
    }

    private static string ResolveSkinPath(string skinName)
    {
        var baseDir = AppContext.BaseDirectory;
        var runtimePath = Path.Combine(baseDir, "assets", "skins", skinName);
        if (Directory.Exists(runtimePath))
        {
            return runtimePath;
        }

        var devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "assets", "skins", skinName));
        if (Directory.Exists(devPath))
        {
            return devPath;
        }

        var readyRuntime = Path.Combine(baseDir, "READY", skinName);
        if (Directory.Exists(readyRuntime))
        {
            return readyRuntime;
        }

        var readyDev = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "READY", skinName));
        if (Directory.Exists(readyDev))
        {
            return readyDev;
        }

        throw new DirectoryNotFoundException($"Skin '{skinName}' was not found under assets/skins or READY.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        SpriteSheet.Dispose();
        disposed = true;
    }

    private sealed class SkinConfig
    {
        [JsonPropertyName("spritesheet")]
        public string Spritesheet { get; set; } = string.Empty;

        [JsonPropertyName("sound")]
        public JsonNode? Sound { get; set; }

        [JsonPropertyName("frame_width")]
        public int FrameWidth { get; set; }

        [JsonPropertyName("frame_height")]
        public int FrameHeight { get; set; }

        [JsonPropertyName("animations")]
        public Dictionary<string, string[]> Animations { get; set; } = new();

        [JsonIgnore]
        public IReadOnlyList<string> SoundFiles => ParseSound(Sound);

        private static IReadOnlyList<string> ParseSound(JsonNode? node)
        {
            if (node is null)
            {
                return Array.Empty<string>();
            }

            if (node is JsonValue value && value.TryGetValue<string>(out var str))
            {
                return string.IsNullOrWhiteSpace(str) ? Array.Empty<string>() : new[] { str };
            }

            if (node is JsonArray arr)
            {
                var list = new List<string>();
                foreach (var element in arr)
                {
                    if (element is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                    {
                        list.Add(s);
                    }
                }

                return list;
            }

            return Array.Empty<string>();
        }
    }
}

/// <summary>
/// Simple animation player that advances frames at a fixed rate and tracks the current animation.
/// </summary>
internal sealed class PetAnimator
{
    internal const double DefaultFrameDurationMs = 70d;

    private readonly PetSkin skin;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly double baseFrameDurationMs;
    private string currentAnimation;
    private double lastSwitchTimestampMs;
    private int frameIndex;
    private double speedMultiplier = 1d;
    private bool looping = true;
    private bool holdLastFrame;
    private bool finishedOnce;

    internal PetAnimator(PetSkin skin, double frameDurationMs = DefaultFrameDurationMs)
    {
        this.skin = skin;
        baseFrameDurationMs = frameDurationMs;
        currentAnimation = skin.ResolveAnimationName("idle");
        lastSwitchTimestampMs = stopwatch.Elapsed.TotalMilliseconds;
    }

    internal void SetAnimation(string animationName, bool restartIfSame = false, bool loop = true, bool holdOnLastFrame = false)
    {
        var resolved = skin.ResolveAnimationName(animationName);
        if (!restartIfSame && resolved == currentAnimation)
        {
            return;
        }

        currentAnimation = resolved;
        frameIndex = 0;
        finishedOnce = false;
        looping = loop;
        holdLastFrame = holdOnLastFrame;
        lastSwitchTimestampMs = stopwatch.Elapsed.TotalMilliseconds;
    }

    internal void SetSpeedMultiplier(double multiplier)
    {
        speedMultiplier = Math.Max(0.1d, multiplier);
    }

    internal void Update()
    {
        var frames = skin.GetFramesOrFallback(currentAnimation);
        if (frames.Length == 0)
        {
            return;
        }

        if (!looping && finishedOnce && holdLastFrame)
        {
            frameIndex = frames.Length - 1;
            return;
        }

        var elapsed = stopwatch.Elapsed.TotalMilliseconds;
        var effectiveFrameDuration = Math.Max(20d, baseFrameDurationMs / speedMultiplier);
        if (elapsed - lastSwitchTimestampMs >= effectiveFrameDuration)
        {
            if (frameIndex >= frames.Length - 1)
            {
                finishedOnce = true;
                if (looping)
                {
                    frameIndex = 0;
                }
                else if (!holdLastFrame)
                {
                    frameIndex = frames.Length - 1;
                }
            }
            else
            {
                frameIndex = frameIndex + 1;
            }

            lastSwitchTimestampMs = elapsed;
        }
    }

    internal Rectangle CurrentSourceFrame
    {
        get
        {
            var frames = skin.GetFramesOrFallback(currentAnimation);
            if (frames.Length == 0)
            {
                return Rectangle.Empty;
            }

            frameIndex = Math.Clamp(frameIndex, 0, frames.Length - 1);
            return frames[frameIndex];
        }
    }
}
