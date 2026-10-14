namespace PW64Editor.Core.Missions;

/// <summary>
/// Converts heights between the game's own units (positions in the mission files and in RAM) and
/// the meters the game shows to the player.
/// </summary>
/// <remarks>
/// <para>The altimeter shows the height above the ground times 0.7 (hud->altitude = height × 0.7 in
/// every vehicle's HUD code). The original texts use the same meters: Hang Glider Class B 1 says
/// "ascend to 400m" and its condition value is 571 (571 × 0.7 = 399.7).</para>
/// <para>The highest altitude of a flight (altitude points, the altitude condition) is the height
/// above sea level (the vehicle's z position), not above the ground: a flight that starts on a
/// tower or a mountain already has that height. The HUD shows this value as well, under "SEA LEVEL".</para>
/// </remarks>
public static class GameUnits
{
    /// <summary>Meters shown in the game per unit of position.</summary>
    public const float MetersPerUnit = 0.7f;

    /// <summary>Game units to the meters the game shows.</summary>
    public static float ToMeters(float units) => units * MetersPerUnit;

    /// <summary>Meters as the game shows them to game units.</summary>
    public static float FromMeters(float meters) => meters / MetersPerUnit;
}
