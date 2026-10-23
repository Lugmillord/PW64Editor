using System.Globalization;
using PW64Editor.Core.Code;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Missions;

/// <summary>Time of day and weather of a mission (COMM byte 8; the game picks the environment of the island with it).</summary>
public enum WeatherCondition
{
    /// <summary>Clear day with cumulus clouds.</summary>
    ClearDay = 0,

    /// <summary>Day with a sky of small clouds.</summary>
    CloudyDay = 1,

    /// <summary>Overcast (grey fog, colours paler and darker).</summary>
    Overcast = 2,

    /// <summary>Dark overcast.</summary>
    DarkOvercast = 3,

    /// <summary>Sunset (orange light; the fog colour follows the direction of view).</summary>
    Sunset = 4,

    /// <summary>Night (stars and moon, black fog, the island's night colours).</summary>
    Night = 5,
}

/// <summary>What falls from the sky in a mission.</summary>
public enum WeatherEffect
{
    None = 0,
    Snow = 1,
    Rain = 2,
}

/// <summary>A colour of the fog and effect lists.</summary>
/// <param name="Name">English name (the editor translates it).</param>
/// <param name="Rgb">0xRRGGBB.</param>
/// <param name="Original">One of the fog colours of the game's environments.</param>
public sealed record WeatherColor(string Name, uint Rgb, bool Original = false);

/// <summary>
/// The weather and view settings of a mission that are not in its mission file (stored in project.json, applied by the game
/// module, weather.c). Time of day, snow and wind are in the mission file (COMM bytes 8 and 9, COMM +0x10–0x2B).
/// </summary>
public sealed class MissionWeather
{
    /// <summary>With snow switched on in the mission file: rain instead of snow.</summary>
    public bool Rain { get; set; }

    /// <summary>Fall speed of the snow or rain (units per second), or null for the default (snow 15, rain 25).</summary>
    public float? FallSpeed { get; set; }

    /// <summary>Number of flakes or drops (200–1200), or null for the game's 800.</summary>
    public int? Density { get; set; }

    /// <summary>Colour of the snow or rain ("#RRGGBB"), or null for the default (snow white, rain light blue grey).</summary>
    public string? EffectColor { get; set; }

    /// <summary>Fog colour ("#RRGGBB"), or null for the environment's (not with a sunset, whose fog follows the view).</summary>
    public string? FogColor { get; set; }

    /// <summary>The sky is tinted with the fog colour too (<see cref="FogColor"/> or the environment's).</summary>
    public bool SkyTint { get; set; }

    /// <summary>View distance (m, 1000–2100), or null for the game's 2000.</summary>
    public float? ViewDistance { get; set; }

    /// <summary>Where the fog starts (m), or null: the fog follows the view distance as in the game.</summary>
    public float? FogStart { get; set; }

    /// <summary>With <see cref="FogStart"/>: how thick the fog is at the view distance (%, 10–100).</summary>
    public float? FogDensity { get; set; }

    /// <summary>True if nothing is set.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => !Rain && FallSpeed is null && Density is null && EffectColor is null && FogColor is null && !SkyTint
        && ViewDistance is null && FogStart is null && FogDensity is null;

    /// <summary>True if the snow or rain differs from the game's snow.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasOwnEffect => Rain || FallSpeed is not null || Density is not null || EffectColor is not null;

    /// <summary>A copy.</summary>
    public MissionWeather Clone() => (MissionWeather)MemberwiseClone();

    /// <summary>True if both say the same.</summary>
    public bool SameAs(MissionWeather? other) => other is not null && Rain == other.Rain && FallSpeed == other.FallSpeed
        && Density == other.Density && EffectColor == other.EffectColor && FogColor == other.FogColor && SkyTint == other.SkyTint
        && ViewDistance == other.ViewDistance && FogStart == other.FogStart && FogDensity == other.FogDensity;
}

/// <summary>The time of day, weather, effects, fog and view distance the game offers (Environment_Weather.md).</summary>
public static class WeatherCatalog
{
    /// <summary>The conditions in the order of the list.</summary>
    public static IReadOnlyList<WeatherCondition> Conditions { get; } = Enum.GetValues<WeatherCondition>();

    /// <summary>English name of a condition.</summary>
    public static string Name(WeatherCondition condition) => condition switch
    {
        WeatherCondition.ClearDay => CoreText.K("Clear day"),
        WeatherCondition.CloudyDay => CoreText.K("Day with small clouds"),
        WeatherCondition.Overcast => CoreText.K("Overcast"),
        WeatherCondition.DarkOvercast => CoreText.K("Dark overcast"),
        WeatherCondition.Sunset => CoreText.K("Sunset"),
        WeatherCondition.Night => CoreText.K("Night"),
        _ => condition.ToString(),
    };

    /// <summary>
    /// True if the editor has an environment for the condition on the island (0 Holiday, 1 Crescent, 2 Little States,
    /// 3 Ever-Frost): the game's own or one it adds (<see cref="WeatherEnvironments"/>).
    /// </summary>
    public static bool IsAvailable(int island, WeatherCondition condition) => WeatherEnvironments.Environment(island, condition) is not null;

    /// <summary>
    /// True if the game itself has an environment for the condition on the island: Holiday has no dark overcast, Crescent
    /// no sunset, Ever-Frost neither (the editor adds them, <see cref="WeatherEnvironments.Added"/>).
    /// </summary>
    public static bool IsOriginal(int island, WeatherCondition condition) => WeatherEnvironments.GameEnvironment(island, condition) is not null;

    /// <summary>The environment's fog colour (0xRRGGBB) of a condition on an island (sunset: its colour away from the sun).</summary>
    public static uint OriginalFogColor(int island, WeatherCondition condition) => condition switch
    {
        WeatherCondition.ClearDay or WeatherCondition.CloudyDay => 0xD1E5FF,
        WeatherCondition.Overcast => 0x374242,
        WeatherCondition.DarkOvercast => island == 2 ? 0x0F1616u : 0x1E2D2Du,
        WeatherCondition.Sunset => 0x45515C,
        _ => 0x000000,
    };

    /// <summary>The colours of the fog and effect lists: the game's fog colours first.</summary>
    public static IReadOnlyList<WeatherColor> Colors { get; } =
    [
        new(CoreText.K("Day (light blue)"), 0xD1E5FF, true),
        new(CoreText.K("Overcast (grey)"), 0x374242, true),
        new(CoreText.K("Dark overcast (grey green)"), 0x1E2D2D, true),
        new(CoreText.K("Dark overcast, Little States (dark grey green)"), 0x0F1616, true),
        new(CoreText.K("Sunset (grey blue)"), 0x45515C, true),
        new(CoreText.K("Night (black)"), 0x000000, true),
        new(CoreText.K("White"), 0xFFFFFF),
        new(CoreText.K("Light grey"), 0xC0C0C0),
        new(CoreText.K("Dark grey"), 0x606060),
        new(CoreText.K("Light blue grey"), 0xB0C4DE),
        new(CoreText.K("Red"), 0xD02020),
        new(CoreText.K("Dark red"), 0x701010),
        new(CoreText.K("Orange"), 0xF08020),
        new(CoreText.K("Yellow"), 0xF0E040),
        new(CoreText.K("Sand"), 0xD8C090),
        new(CoreText.K("Brown"), 0x805030),
        new(CoreText.K("Green"), 0x40B040),
        new(CoreText.K("Dark green"), 0x1E5A1E),
        new(CoreText.K("Mint"), 0xA0F0C0),
        new(CoreText.K("Turquoise"), 0x30C0B0),
        new(CoreText.K("Cyan"), 0x40E0F0),
        new(CoreText.K("Sky blue"), 0x70B0F0),
        new(CoreText.K("Navy blue"), 0x102050),
        new(CoreText.K("Violet"), 0x8050D0),
        new(CoreText.K("Purple"), 0x702080),
        new(CoreText.K("Magenta"), 0xE040C0),
        new(CoreText.K("Pink"), 0xF0A0C8),
    ];

    /// <summary>Default number of flakes or drops (the game's snow).</summary>
    public const int DefaultDensity = 800;

    /// <summary>Fewest and most flakes or drops.</summary>
    public const int MinDensity = 200, MaxDensity = 1200;

    /// <summary>Highest fall speed (units per second).</summary>
    public const float MaxFallSpeed = 30;

    /// <summary>Default fall speed: snow as the game (15), rain 25.</summary>
    public static float DefaultFallSpeed(WeatherEffect effect) => effect == WeatherEffect.Rain ? 25 : 15;

    /// <summary>Default colour: snow white, rain light blue grey.</summary>
    public static uint DefaultEffectColor(WeatherEffect effect) => effect == WeatherEffect.Rain ? 0xB0C4DEu : 0xFFFFFFu;

    /// <summary>View distance of the game (camera's far distance).</summary>
    public const float DefaultViewDistance = 2000;

    /// <summary>Shortest and longest view distance.</summary>
    public const float MinViewDistance = 1000, MaxViewDistance = 2100;

    /// <summary>The fog factor the game uses in flight (env_802E0CF0).</summary>
    public const float GameFogFactor = 0.996f;

    /// <summary>
    /// The game's depth value (0 at the near distance 1, 1000 at the far distance) of a distance straight ahead: N64 fog is
    /// linear in this value (gSPFogPosition).
    /// </summary>
    public static double Depth(double distance, double far)
    {
        const double near = 1;
        double z = ((far + near) / (far - near)) - (2 * far * near / ((far - near) * Math.Max(distance, near)));
        return 500 * (1 + z);
    }

    /// <summary>The distance of a depth value (the reverse of <see cref="Depth"/>).</summary>
    public static double DistanceOf(double depth, double far)
    {
        const double near = 1;
        double z = (depth / 500) - 1;
        return 2 * far * near / ((far - near) * (((far + near) / (far - near)) - z));
    }

    /// <summary>Where the game's fog starts at a view distance (m, factor 0.996).</summary>
    public static float GameFogStart(float viewDistance) => (float)DistanceOf(GameFogFactor * 1000, viewDistance);

    /// <summary>
    /// The fog range in depth values for a start (m) and a density at the view distance (%): fog grows linearly in depth from
    /// the start and reaches the density at the view distance (100 %: full fog there).
    /// </summary>
    public static (double Min, double Max) FogRange(float start, float density, float viewDistance)
    {
        double min = Math.Clamp(Depth(Math.Clamp(start, 1, viewDistance), viewDistance), 0, 999);
        double d = Math.Clamp(density, 1, 100) / 100;
        return (min, min + Math.Max((1000 - min) / d, 128000.0 / 32767)); // the game's fog cannot be steeper
    }

    /// <summary>The fog range as gSPFogPosition writes it: multiplier and offset.</summary>
    public static (int Multiplier, int Offset) FogCommand(double min, double max)
    {
        double range = Math.Max(max - min, 128000.0 / 32767);
        return ((int)Math.Clamp(Math.Round(128000 / range), 1, 32767), (int)Math.Clamp(Math.Round((500 - min) * 256 / range), -32768, 32767));
    }

    /// <summary>The fog range of a gSPFogPosition multiplier and offset (the reverse of <see cref="FogCommand"/>).</summary>
    public static (double Min, double Max) FogRangeOf(int multiplier, int offset)
    {
        double range = 128000.0 / Math.Max(multiplier, 1);
        double min = 500 - (offset * range / 256);
        return (min, min + range);
    }

    /// <summary>The wind as direction (degrees where it blows to: 0 north = +y, 90 east = +x) and strength.</summary>
    public static (float Direction, float Strength) WindOf((float X, float Y, float Z) wind)
    {
        double strength = Math.Sqrt((wind.X * wind.X) + (wind.Y * wind.Y));
        double direction = strength == 0 ? 0 : Math.Atan2(wind.X, wind.Y) * 180 / Math.PI;
        if (direction < 0)
        {
            direction += 360;
        }

        return ((float)Math.Round(direction, 1) % 360, (float)strength);
    }

    /// <summary>The wind vector of a direction (degrees, where it blows to) and strength; the vertical part is kept.</summary>
    public static (float X, float Y, float Z) WindVector(float direction, float strength, float z)
    {
        double a = direction * Math.PI / 180;
        return ((float)Math.Round(strength * Math.Sin(a), 4), (float)Math.Round(strength * Math.Cos(a), 4), z);
    }

    /// <summary>"#RRGGBB" of a colour.</summary>
    public static string Hex(uint rgb) => "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    /// <summary>The colour of "#RRGGBB" (or "RRGGBB"), or null.</summary>
    public static uint? ParseHex(string? text)
    {
        string t = (text ?? string.Empty).Trim().TrimStart('#');
        return t.Length == 6 && uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v : null;
    }

    /// <summary>A colour as RGBA 5551 (the frame buffer).</summary>
    public static ushort ToRgba5551(uint rgb) =>
        (ushort)((((rgb >> 19) & 0x1F) << 11) | (((rgb >> 11) & 0x1F) << 6) | (((rgb >> 3) & 0x1F) << 1) | 1);

    /// <summary>
    /// The game module's settings of a mission (null if nothing is set): a fog colour (not with a sunset), the view
    /// distance, the fog range and the snow or rain (only with snow on in the mission file).
    /// </summary>
    public static GameModuleWeather? ToModule(MissionWeather? weather, MissionComm comm)
    {
        if (weather is null || weather.IsEmpty)
        {
            return null;
        }

        // The sky can also be tinted with the environment's own fog colour (then that colour is written).
        uint? fog = comm.Weather == WeatherCondition.Sunset ? null
            : ParseHex(weather.FogColor) ?? (weather.SkyTint ? OriginalFogColor(comm.Island, comm.Weather) : null);
        float view = Math.Clamp(weather.ViewDistance ?? DefaultViewDistance, MinViewDistance, MaxViewDistance);
        (int, int)? range = null;
        if (weather.FogStart is { } start)
        {
            (double min, double max) = FogRange(start, weather.FogDensity ?? 100, view);
            range = FogCommand(min, max);
        }

        GameModuleEffect? effect = null;
        if (comm.Snow && weather.HasOwnEffect)
        {
            WeatherEffect kind = weather.Rain ? WeatherEffect.Rain : WeatherEffect.Snow;
            effect = new GameModuleEffect(weather.Rain, ToRgba5551(ParseHex(weather.EffectColor) ?? DefaultEffectColor(kind)),
                Math.Clamp(weather.FallSpeed ?? DefaultFallSpeed(kind), 0, MaxFallSpeed),
                Math.Clamp(weather.Density ?? DefaultDensity, MinDensity, MaxDensity));
        }

        var result = new GameModuleWeather(fog, fog is not null && weather.SkyTint,
            weather.ViewDistance is not null && view != DefaultViewDistance ? view : null, range, effect);
        return result.IsEmpty ? null : result;
    }
}
