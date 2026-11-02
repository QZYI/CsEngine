using System.Text.Json;

namespace Tiny3D;

/// <summary>Configuration values controlling the engine UI, renderer allocation, frame cap, and fade effect.</summary>
public sealed class EngineSettings
{
    private const int MinimumFontResolution = 8;
    private const int MaximumFontResolution = 24;
    private const long MinimumMemoryAllocation = 4096;
    private float _ssaaStrength = 1f;

    /// <summary>Rasterization resolution, in pixels, used for UI glyphs.</summary>
    public int FontResolution { get; init; } = 18;
    /// <summary>Maximum dynamic vertex-buffer allocation, in bytes.</summary>
    public long MemoryAllocation { get; init; } = 64 * 1024 * 1024;
    /// <summary>Target frame rate; zero disables frame limiting.</summary>
    public int FramerateCap { get; init; } = 60;
    /// <summary>Global post-process anti-aliasing strength in the range 0..1.</summary>
    public float SSAAStrength
    {
        get => _ssaaStrength;
        init => _ssaaStrength = ValidateSsaStrength(value, nameof(SSAAStrength));
    }
    /// <summary>Controls whether post-process anti-aliasing is active.</summary>
    public bool AntiAliasing
    {
        get => SSAAStrength > 0f;
        init => _ssaaStrength = value ? 1f : 0f;
    }
    /// <summary>Compatibility alias for SSAAStrength.</summary>
    public float AntiAliasingStrength
    {
        get => SSAAStrength;
        init => _ssaaStrength = ValidateSsaStrength(value, nameof(AntiAliasingStrength));
    }
    /// <summary>Enables depth-based dither fading for mesh surfaces near the camera.</summary>
    public bool DitherFade { get; init; } = false;

    public static EngineSettings Defaults { get; } = new();

    /// <summary>Loads and validates settings from a JSON file.</summary>
    public static EngineSettings Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<EngineSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        }) ?? throw new InvalidDataException($"Engine settings file '{path}' is empty.");
        return settings.Validate();
    }

    /// <summary>Loads the application-local settings file, or returns engine defaults.</summary>
    public static EngineSettings LoadDefault()
    {
        var outputSettings = Path.Combine(AppContext.BaseDirectory, "EngineSettings.json");
        if (File.Exists(outputSettings))
        {
            return Load(outputSettings);
        }

        var currentSettings = Path.Combine(Environment.CurrentDirectory, "Tiny3D", "EngineSettings.json");
        if (File.Exists(currentSettings))
        {
            return Load(currentSettings);
        }

        return Defaults;
    }

    /// <summary>Validates configured ranges and returns this settings instance.</summary>
    public EngineSettings Validate()
    {
        if (FontResolution is < MinimumFontResolution or > MaximumFontResolution)
        {
            throw new ArgumentOutOfRangeException(nameof(FontResolution),
                $"Font resolution must be between {MinimumFontResolution} and {MaximumFontResolution} pixels.");
        }

        if (MemoryAllocation < MinimumMemoryAllocation ||
            MemoryAllocation > int.MaxValue ||
            MemoryAllocation % sizeof(float) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MemoryAllocation),
                $"Memory allocation must be between {MinimumMemoryAllocation} and {int.MaxValue} bytes, " +
                "and divisible by four.");
        }

        if (FramerateCap < 0 || FramerateCap > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(FramerateCap), "Framerate cap must be between 0 and 1000.");
        }

        ValidateSsaStrength(SSAAStrength, nameof(SSAAStrength));
        return this;
    }

    private static float ValidateSsaStrength(float strength, string parameterName) =>
        strength is >= 0f and <= 1f
            ? strength
            : throw new ArgumentOutOfRangeException(parameterName, "Anti-aliasing strength must be between 0 and 1.");
}
