namespace PW64Editor.Core.Missions;

/// <summary>
/// ROM offsets of the tables in the game code that belong to the missions. They lie in the data
/// segment of the game code, which is stored uncompressed.
/// </summary>
/// <param name="ClassIslandsOffset">D_8034F420 in test_summary.c: u8[7 vehicles][4 classes], the
/// island the class menu shows (0 Holiday, 1 Crescent, 2 Little States, 3 Ever-Frost).</param>
/// <param name="MedalLimitsOffset">gMedalPointRequirements in code_B2900.c: 8 × (bronze, silver,
/// gold) as u16.</param>
/// <param name="ResultSheetNamesOffset">sResultTestNames2 in results.c: pointers [6 vehicles][4 classes][3 tests]
/// to the names of the result sheet texts ("…_S2", with slash).</param>
/// <param name="MissionScreenNamesOffset">sResultTestNames1 in results.c: the same for the texts of
/// the mission screen ("…_S1", without slash).</param>
/// <param name="ResultRowsOffset">sResultPtTypes in results.c: u8[6][4][3][4], the point rows of
/// the result sheet, bottom row first.</param>
/// <param name="DataRamDelta">RAM address minus ROM offset in this data segment, to follow the
/// pointers of the name tables.</param>
public sealed record MissionLayout(
    int ClassIslandsOffset,
    int MedalLimitsOffset,
    int ResultSheetNamesOffset,
    int MissionScreenNamesOffset,
    int ResultRowsOffset,
    uint DataRamDelta)
{
    /// <summary>Vehicles in the result tables (Birdman has no entry).</summary>
    public const int ResultVehicles = 6;

    /// <summary>Classes in the result tables.</summary>
    public const int ResultClasses = 4;

    /// <summary>Tests per class in the result tables.</summary>
    public const int ResultTests = 3;

    /// <summary>Point rows per test on the result sheet.</summary>
    public const int ResultRows = 4;

    /// <summary>Entries of the medal table.</summary>
    public const int MedalEntries = 8;

    /// <summary>
    /// Pilotwings 64 (USA). Found in the ROM by their contents and checked against the
    /// decompilation: the name pointers point to "HG_E_S2" etc. at 0xDCBE0.
    /// </summary>
    public static readonly MissionLayout PilotwingsUsa = new(
        ClassIslandsOffset: 0xD6950,
        MedalLimitsOffset: 0xD7104,
        ResultSheetNamesOffset: 0xD7170,
        MissionScreenNamesOffset: 0xD7290,
        ResultRowsOffset: 0xD73B0,
        DataRamDelta: 0x80278AD0);
}
