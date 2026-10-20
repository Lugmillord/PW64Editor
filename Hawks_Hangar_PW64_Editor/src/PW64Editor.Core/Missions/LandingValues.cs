using PW64Editor.Core.Localization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>
/// A value that decides a good landing or a crash (stored per mission in
/// <see cref="MissionSettings.Landing"/>, applied by the game module).
/// </summary>
public enum LandingValue
{
    /// <summary>Radius of the landing pads for a good landing (game units; all vehicles with pads).</summary>
    PadRadius = 0,

    /// <summary>Hang Glider: landing instead of a crash below this vertical speed (km/h).</summary>
    HangGliderLandingVertical = 10,

    /// <summary>Hang Glider: landing instead of a crash below this forward speed (km/h).</summary>
    HangGliderLandingForward = 11,

    /// <summary>Hang Glider: good landing below this vertical speed (km/h).</summary>
    HangGliderGoodVertical = 12,

    /// <summary>Hang Glider: good landing below this forward speed (km/h).</summary>
    HangGliderGoodForward = 13,

    /// <summary>Gyrocopter: crash from this vertical speed of a wheel into the ground (game units per second).</summary>
    GyrocopterCrash = 20,

    /// <summary>Gyrocopter: crash from this speed when not on a pad or runway (game units per second).</summary>
    GyrocopterCrashOffPad = 21,

    /// <summary>Rocket Belt: crash above this speed into the ground (game units per second).</summary>
    RocketBeltCrash = 30,

    /// <summary>Rocket Belt: hard landing (no impact points) above this speed into the ground (game units per second).</summary>
    RocketBeltHard = 31,

    /// <summary>Rocket Belt: hard landing above this sideways speed (game units per second).</summary>
    RocketBeltHardSideways = 32,
}

/// <summary>The landing values: the original numbers, which vehicle uses them and how the editor shows them.</summary>
/// <remarks>
/// Where they are in the game (US): the pad radius is set to 30 for every pad when a mission loads
/// (pads.c, gLandingPads[].landingDistance); the Hang Glider compares its speeds (already in km/h,
/// × 2.52) with the immediates 50 and 15 (hangGlider_802F3A80); the Gyrocopter (func_80305DC0) and the
/// Rocket Belt (rocket_belt.c) compare their speeds in game units per second with constants of the
/// game's data (ROM 0xDACF8/0xDACFC and 0xDC964, 0xDC96C, 0xDC974, 0xDC978, 0xDC988). The HUD shows
/// speeds as units per second × 3.6 × 0.7 = km/h, so the editor shows these speeds in km/h too.
/// </remarks>
public static class LandingValues
{
    /// <summary>Game units per second to the km/h the HUD shows (3.6 × 0.7).</summary>
    public const float KilometersPerHourPerUnit = 2.52f;

    /// <summary>All values in the order the editor shows them.</summary>
    public static IReadOnlyList<LandingValue> All { get; } = Enum.GetValues<LandingValue>();

    /// <summary>The value of the original game (in its stored form).</summary>
    public static float Original(LandingValue value) => value switch
    {
        LandingValue.PadRadius => 30f,
        LandingValue.HangGliderLandingVertical or LandingValue.HangGliderLandingForward => 50f,
        LandingValue.HangGliderGoodVertical or LandingValue.HangGliderGoodForward => 15f,
        LandingValue.GyrocopterCrash => 12.698f,
        LandingValue.GyrocopterCrashOffPad => 7.937f,
        LandingValue.RocketBeltCrash => 21.8f,
        _ => 7.93f, // Rocket Belt hard landing, both
    };

    /// <summary>The vehicle whose speeds these are, or null for the pad radius (every vehicle with pads).</summary>
    public static Vehicle? VehicleOf(LandingValue value) => value switch
    {
        LandingValue.PadRadius => null,
        >= LandingValue.HangGliderLandingVertical and <= LandingValue.HangGliderGoodForward => Vehicle.HangGlider,
        LandingValue.GyrocopterCrash or LandingValue.GyrocopterCrashOffPad => Vehicle.Gyrocopter,
        _ => Vehicle.RocketBelt,
    };

    /// <summary>The speeds of a vehicle in the order the game module stores them (at most 4).</summary>
    public static IReadOnlyList<LandingValue> SpeedsOf(Vehicle vehicle) => vehicle switch
    {
        Vehicle.HangGlider =>
            [LandingValue.HangGliderLandingVertical, LandingValue.HangGliderLandingForward, LandingValue.HangGliderGoodVertical, LandingValue.HangGliderGoodForward],
        Vehicle.Gyrocopter => [LandingValue.GyrocopterCrash, LandingValue.GyrocopterCrashOffPad],
        Vehicle.RocketBelt => [LandingValue.RocketBeltCrash, LandingValue.RocketBeltHard, LandingValue.RocketBeltHardSideways],
        _ => [],
    };

    /// <summary>True if the vehicle lands on pads (all but Cannonball and the Jumble Hopper).</summary>
    public static bool UsesPads(Vehicle vehicle) => vehicle is not (Vehicle.Cannonball or Vehicle.JumbleHopper);

    /// <summary>Shown value per stored one: meters per game unit for the radius, km/h for the speeds.</summary>
    public static float ShownPerStored(LandingValue value) => value switch
    {
        LandingValue.PadRadius => GameUnits.MetersPerUnit,
        >= LandingValue.HangGliderLandingVertical and <= LandingValue.HangGliderGoodForward => 1f, // the Hang Glider compares km/h
        _ => KilometersPerHourPerUnit,
    };

    /// <summary>The value as the editor shows it (meters, km/h).</summary>
    public static float Shown(LandingValue value, float stored) => stored * ShownPerStored(value);

    /// <summary>The stored value for a shown one.</summary>
    public static float Stored(LandingValue value, float shown) => shown / ShownPerStored(value);

    /// <summary>The unit the editor shows ("m" or "km/h").</summary>
    public static string Unit(LandingValue value) => value == LandingValue.PadRadius ? "m" : "km/h";

    /// <summary>Highest value the editor accepts (shown form).</summary>
    public static float MaximumShown(LandingValue value) => value == LandingValue.PadRadius ? 1000 : 500;

    /// <summary>Name of a value in the editor's language.</summary>
    public static string Name(LandingValue value) => value switch
    {
        LandingValue.PadRadius => CoreText.T("Radius of the landing pads for a good landing"),
        LandingValue.HangGliderLandingVertical => CoreText.T("Landing (not a crash) below this vertical speed"),
        LandingValue.HangGliderLandingForward => CoreText.T("Landing (not a crash) below this forward speed"),
        LandingValue.HangGliderGoodVertical => CoreText.T("Good landing below this vertical speed"),
        LandingValue.HangGliderGoodForward => CoreText.T("Good landing below this forward speed"),
        LandingValue.GyrocopterCrash => CoreText.T("Crash from this speed into the ground"),
        LandingValue.GyrocopterCrashOffPad => CoreText.T("Crash from this speed into the ground beside a pad or runway"),
        LandingValue.RocketBeltCrash => CoreText.T("Crash above this speed into the ground"),
        LandingValue.RocketBeltHard => CoreText.T("Hard landing (no impact points) above this speed into the ground"),
        _ => CoreText.T("Hard landing (no impact points) above this sideways speed"),
    };

    /// <summary>The value a mission uses: its setting, or the original.</summary>
    public static float Effective(MissionSettings? settings, LandingValue value) =>
        settings?.Landing.TryGetValue(value, out float set) == true ? set : Original(value);
}
