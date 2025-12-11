using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuackDuck;

/// <summary>
/// Loads skin configuration, slices the spritesheet, and exposes frames per animation.
/// </summary>
internal sealed class PetSkin : IDisposable
{
    private readonly Dictionary<string, Rectangle[]> animations;
    private bool disposed;

    private PetSkin(Bitmap spriteSheet, int frameWidth, int frameHeight, Dictionary<string, Rectangle[]> animations, string? soundPath)
    {
        SpriteSheet = spriteSheet;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        this.animations = animations;
        SoundPath = soundPath;
    }

    internal Bitmap SpriteSheet { get; }
    internal int FrameWidth { get; }
    internal int FrameHeight { get; }
    internal string? SoundPath { get; }

    internal static PetSkin Load(string skinName = "default")
    {
        var skinFolder = ResolveSkinPath(skinName);
        var configPath = Path.Combine(skinFolder, "config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Skin config not found at {configPath}");
        }

        var config = ReadConfig(configPath);
        var sheetPath = Path.Combine(skinFolder, config.Spritesheet);
        if (!File.Exists(sheetPath))
        {
            throw new FileNotFoundException($"Spritesheet '{config.Spritesheet}' not found at {sheetPath}");
        }

        var spriteSheet = new Bitmap(sheetPath);
        var animations = BuildAnimations(config, spriteSheet.Width, spriteSheet.Height);
        var soundPath = string.IsNullOrWhiteSpace(config.Sound) ? null : Path.Combine(skinFolder, config.Sound);

        return new PetSkin(spriteSheet, config.FrameWidth, config.FrameHeight, animations, soundPath);
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

    private static Rectangle ParseFrame(string token, int frameWidth, int frameHeight)
    {
        var parts = token.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var row) || !int.TryParse(parts[1], out var column))
        {
            throw new FormatException($"Frame '{token}' must be in 'row:column' format.");
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

        throw new DirectoryNotFoundException($"Skin '{skinName}' was not found under assets/skins.");
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
        public string? Sound { get; set; }

        [JsonPropertyName("frame_width")]
        public int FrameWidth { get; set; }

        [JsonPropertyName("frame_height")]
        public int FrameHeight { get; set; }

        [JsonPropertyName("animations")]
        public Dictionary<string, string[]> Animations { get; set; } = new();
    }
}

/// <summary>
/// Simple animation player that advances frames at a fixed rate and tracks the current animation.
/// </summary>
internal sealed class PetAnimator
{
    private readonly PetSkin skin;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly double frameDurationMs;
    private string currentAnimation;
    private double lastSwitchTimestampMs;
    private int frameIndex;

    internal PetAnimator(PetSkin skin, double frameDurationMs = 120d)
    {
        this.skin = skin;
        this.frameDurationMs = frameDurationMs;
        currentAnimation = skin.ResolveAnimationName("idle");
        lastSwitchTimestampMs = stopwatch.Elapsed.TotalMilliseconds;
    }

    internal void SetAnimation(string animationName, bool restartIfSame = false)
    {
        var resolved = skin.ResolveAnimationName(animationName);
        if (!restartIfSame && resolved == currentAnimation)
        {
            return;
        }

        currentAnimation = resolved;
        frameIndex = 0;
        lastSwitchTimestampMs = stopwatch.Elapsed.TotalMilliseconds;
    }

    internal void Update()
    {
        var frames = skin.GetFramesOrFallback(currentAnimation);
        if (frames.Length == 0)
        {
            return;
        }

        var elapsed = stopwatch.Elapsed.TotalMilliseconds;
        if (elapsed - lastSwitchTimestampMs >= frameDurationMs)
        {
            frameIndex = (frameIndex + 1) % frames.Length;
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
