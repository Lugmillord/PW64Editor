using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Audio;

/// <summary>Kinds of instruments, for choosing them in two steps.</summary>
public enum InstrumentCategory
{
    Drums,
    Bass,
    ElectricGuitar,
    BrassAndWind,
    Mallets,
    Synthesizer,
    Effects,
}

/// <summary>A named instrument of the bank.</summary>
/// <param name="Program">Its number in the bank (program number).</param>
/// <param name="Name">English name; translated where it is shown (<see cref="CoreText.T"/>).</param>
/// <param name="Category">Its kind.</param>
public sealed record InstrumentInfo(int Program, string Name, InstrumentCategory Category);

/// <summary>
/// Names of the songs and instruments of Pilotwings 64 (USA). The game itself has no names:
/// the song names follow the decompilation (enum Music in snd.h), the instrument names were given
/// by listening to them.
/// </summary>
/// <remarks>All names are English and marked for the language files; they are translated where they are shown.</remarks>
public static class MusicCatalog
{
    /// <summary>The songs by number, in the game's order (also the order of the "Sound Track" option).</summary>
    public static IReadOnlyList<string> SongNames { get; } =
    [
        CoreText.K("Opening theme"),
        CoreText.K("Title demo"),
        CoreText.K("Game menu"),
        CoreText.K("Mission menu"),
        CoreText.K("Hang Glider"),
        CoreText.K("Hang Glider: good landing"),
        CoreText.K("Hang Glider: missed landing"),
        CoreText.K("Hang Glider: crash"),
        CoreText.K("Rocket Belt"),
        CoreText.K("Rocket Belt: good landing"),
        CoreText.K("Rocket Belt: missed landing"),
        CoreText.K("Rocket Belt: crash"),
        CoreText.K("Gyrocopter"),
        CoreText.K("Gyrocopter: good landing"),
        CoreText.K("Gyrocopter: missed landing"),
        CoreText.K("Gyrocopter: crash"),
        CoreText.K("Cannonball"),
        CoreText.K("Cannonball: hit"),
        CoreText.K("Cannonball: miss"),
        CoreText.K("Sky Diving"),
        CoreText.K("Sky Diving: good landing"),
        CoreText.K("Sky Diving: missed landing"),
        CoreText.K("Sky Diving: crash"),
        CoreText.K("Jumble Hopper"),
        CoreText.K("Jumble Hopper: goal"),
        CoreText.K("Birdman"),
        CoreText.K("Birdman: landing"),
        CoreText.K("Birdman: crash"),
        CoreText.K("Results and replay"),
        CoreText.K("Congratulations"),
        CoreText.K("Ending (Bravissimo!)"),
    ];

    /// <summary>English names of the categories.</summary>
    public static string CategoryName(InstrumentCategory category) => category switch
    {
        InstrumentCategory.Drums => CoreText.K("Drums"),
        InstrumentCategory.Bass => CoreText.K("Bass"),
        InstrumentCategory.ElectricGuitar => CoreText.K("Electric guitars"),
        InstrumentCategory.BrassAndWind => CoreText.K("Brass and wind"),
        InstrumentCategory.Mallets => CoreText.K("Xylophone and metallophone"),
        InstrumentCategory.Synthesizer => CoreText.K("Synthesizers"),
        _ => CoreText.K("Effects"),
    };

    /// <summary>All 48 instruments of the bank.</summary>
    public static IReadOnlyList<InstrumentInfo> Instruments { get; } =
    [
        new(0, CoreText.K("Electric guitar 1"), InstrumentCategory.ElectricGuitar),
        new(1, CoreText.K("Drums 1"), InstrumentCategory.Drums),
        new(3, CoreText.K("Trumpet 1"), InstrumentCategory.BrassAndWind),
        new(4, CoreText.K("Drums 2"), InstrumentCategory.Drums),
        new(5, CoreText.K("Synth 1"), InstrumentCategory.Synthesizer),
        new(9, CoreText.K("Bubbling water"), InstrumentCategory.Effects),
        new(10, CoreText.K("Bass 1"), InstrumentCategory.Bass),
        new(11, CoreText.K("Bass 2"), InstrumentCategory.Bass),
        new(12, CoreText.K("Bass 3"), InstrumentCategory.Bass),
        new(13, CoreText.K("Bass 4"), InstrumentCategory.Bass),
        new(14, CoreText.K("Soft bass 1"), InstrumentCategory.Bass),
        new(15, CoreText.K("Bass 5"), InstrumentCategory.Bass),
        new(16, CoreText.K("Soft bass 2"), InstrumentCategory.Bass),
        new(17, CoreText.K("Electric guitar 2"), InstrumentCategory.ElectricGuitar),
        new(18, CoreText.K("Electric guitar 3"), InstrumentCategory.ElectricGuitar),
        new(20, CoreText.K("Synth 2"), InstrumentCategory.Synthesizer),
        new(21, CoreText.K("Xylophone"), InstrumentCategory.Mallets),
        new(22, CoreText.K("Synth 3 (low)"), InstrumentCategory.Synthesizer),
        new(24, CoreText.K("Synth 4 (funky)"), InstrumentCategory.Synthesizer),
        new(25, CoreText.K("Synth 5 (low)"), InstrumentCategory.Synthesizer),
        new(27, CoreText.K("Synth 6"), InstrumentCategory.Synthesizer),
        new(29, CoreText.K("Synth 7"), InstrumentCategory.Synthesizer),
        new(50, CoreText.K("Metallophone 1"), InstrumentCategory.Mallets),
        new(51, CoreText.K("Synth 8 (brass)"), InstrumentCategory.Synthesizer),
        new(53, CoreText.K("Metallophone 2"), InstrumentCategory.Mallets),
        new(55, CoreText.K("Electric guitar 4 (synth)"), InstrumentCategory.ElectricGuitar),
        new(58, CoreText.K("Synth 9 (boing)"), InstrumentCategory.Synthesizer),
        new(59, CoreText.K("Synth 10 (high)"), InstrumentCategory.Synthesizer),
        new(60, CoreText.K("Flute"), InstrumentCategory.BrassAndWind),
        new(61, CoreText.K("Harmonica"), InstrumentCategory.BrassAndWind),
        new(65, CoreText.K("Electric guitar 5"), InstrumentCategory.ElectricGuitar),
        new(70, CoreText.K("Electric guitar 6"), InstrumentCategory.ElectricGuitar),
        new(71, CoreText.K("Trumpet 2"), InstrumentCategory.BrassAndWind),
        new(72, CoreText.K("Electric guitar 7 (high)"), InstrumentCategory.ElectricGuitar),
        new(73, CoreText.K("Electric guitar 8 (high)"), InstrumentCategory.ElectricGuitar),
        new(75, CoreText.K("Synth 11 (boing)"), InstrumentCategory.Synthesizer),
        new(78, CoreText.K("Synth 12"), InstrumentCategory.Synthesizer),
        new(79, CoreText.K("Synth 13"), InstrumentCategory.Synthesizer),
        new(80, CoreText.K("Synth 14 (low)"), InstrumentCategory.Synthesizer),
        new(81, CoreText.K("Metallophone 3 (high)"), InstrumentCategory.Mallets),
        new(82, CoreText.K("Electric guitar 9 (soft)"), InstrumentCategory.ElectricGuitar),
        new(87, CoreText.K("Synth 15 (strong)"), InstrumentCategory.Synthesizer),
        new(88, CoreText.K("Synth 16 (strong, high)"), InstrumentCategory.Synthesizer),
        new(90, CoreText.K("Synth 17 (organ)"), InstrumentCategory.Synthesizer),
        new(91, CoreText.K("Electric guitar 10"), InstrumentCategory.ElectricGuitar),
        new(95, CoreText.K("Synth 18 (boing)"), InstrumentCategory.Synthesizer),
        new(98, CoreText.K("Synth 19 (soft)"), InstrumentCategory.Synthesizer),
        new(99, CoreText.K("Synth 20"), InstrumentCategory.Synthesizer),
    ];

    /// <summary>The instrument with a program number, or null.</summary>
    public static InstrumentInfo? Find(int program) => Instruments.FirstOrDefault(i => i.Program == program);
}
