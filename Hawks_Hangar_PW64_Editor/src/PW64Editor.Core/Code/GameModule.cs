using System.Buffers.Binary;
using System.Text;

namespace PW64Editor.Core.Code;

/// <summary>A call of a startup step in the game code and the hook function that takes its place.</summary>
/// <param name="RomOffset">ROM offset of the "jal" instruction.</param>
/// <param name="OriginalWord">The original instruction (jal to the step).</param>
/// <param name="HookAddress">RAM address of the hook function in the game module.</param>
/// <param name="Name">Name of the step in the debug output.</param>
public sealed record GameModuleCallSite(int RomOffset, uint OriginalWord, uint HookAddress, string Name)
{
    /// <summary>The new instruction: jal to the hook function.</summary>
    public uint HookWord => CodeArea.Jal(HookAddress);
}

/// <summary>
/// The conditions of one mission as the game module checks them (one entry of its table).
/// </summary>
/// <param name="Class">COMM byte 0.</param>
/// <param name="Vehicle">COMM byte 1.</param>
/// <param name="Test">COMM byte 2.</param>
/// <param name="CrashGivesZero">A crash gives 0 points in total.</param>
/// <param name="MinimumAltitude">Time points only if this altitude was reached, or null.</param>
/// <param name="Required">Per object type (<see cref="GameModule.ObjectTypes"/>): 0 = not required,
/// <see cref="GameModule.RequiredAll"/> = all, otherwise at least this many.</param>
/// <param name="MinimumDistance">Time points only if the distance flown reached this, or null (game units).</param>
/// <param name="UnlimitedFuel">Rocket Belt and Gyrocopter: the module refills the fuel every flight frame.</param>
/// <param name="HardEnd">What the hard end time of the mission gives (only for missions that have one).</param>
/// <param name="HardEndMusic">The jingle played when the hard end time is reached (music number), 0 = none.</param>
public sealed record GameModuleCondition(byte Class, byte Vehicle, byte Test, bool CrashGivesZero, float? MinimumAltitude,
    IReadOnlyList<byte> Required, float? MinimumDistance = null, bool UnlimitedFuel = false,
    HardEndScoring HardEnd = HardEndScoring.Retail, byte HardEndMusic = 0)
{
    /// <summary>True if the entry has conditions for the points (otherwise the game's own condition counts).</summary>
    public bool HasConditions => CrashGivesZero || MinimumAltitude is not null || MinimumDistance is not null || Required.Any(r => r != 0);
}

/// <summary>How a mission is scored when its hard end time is reached (game module).</summary>
public enum HardEndScoring
{
    /// <summary>As the original code: only the object points (no entry needed).</summary>
    Retail = 0,

    /// <summary>Scored like a flight that ends in the air: everything but landing accuracy and impact.</summary>
    Scored = 1,

    /// <summary>0 points.</summary>
    Zero = 2,
}

/// <summary>The values the live display shows during missions (debug output).</summary>
[Flags]
public enum LiveValues
{
    None = 0,
    Distance = 1,
    HighestAltitude = 2,
    HorizontalSpeed = 4,
    VerticalSpeed = 8,
}

/// <summary>What the game module does in a build (nothing: no module).</summary>
/// <param name="DebugOutput">Debug output at all (messages to the IS-Viewer port and a log in RAM).</param>
/// <param name="StartupInfo">Report the steps of the game's startup.</param>
/// <param name="GameMessages">Pass on the debug messages of the original developers (_uvDebugPrintf).</param>
/// <param name="OnScreen">Also draw the editor's messages on the screen (15 seconds).</param>
public sealed record GameModuleOptions(bool DebugOutput, bool StartupInfo = false, bool GameMessages = false, bool OnScreen = false)
{
    /// <summary>Missions whose conditions the module checks (combinations the retail game cannot express).</summary>
    public IReadOnlyList<GameModuleCondition> Conditions { get; init; } = [];

    /// <summary>With <see cref="DebugOutput"/>: current values during missions.</summary>
    public LiveValues Live { get; init; }

    /// <summary>A mission shows distance points on its result sheet (row type 10, see <see cref="GameModule"/>).</summary>
    public bool DistanceRow { get; init; }

    /// <summary>"Time requirement met" in the game's text codes as held in RAM (line break 0xFFE, end 0xFFFF), shown on the HUD; empty: nothing.</summary>
    public IReadOnlyList<ushort> RequirementMetText { get; init; } = [];

    /// <summary>The codes of the bold digits 0-9 (as in RAM): the penalty messages ("5 pts. deducted",
    /// "2 pts. deducted") show the mission's own number. Empty: the fixed texts stay.</summary>
    public IReadOnlyList<ushort> PenaltyDigits { get; init; } = [];

    /// <summary>Without the Expansion Pak the game shows "EXPANSION PAK REQUIRED" and stops (every hack built by a project).</summary>
    public bool RequireExpansionPak { get; init; }

    /// <summary>The game patch <see cref="GamePatches.GoalObjects"/> is applied: the module plays the goal jingles and lets
    /// hopper goals and ball targets count.</summary>
    public bool GoalObjects { get; init; }

    /// <summary>
    /// The countdown of the hard end time: "5", "4", "3", "2", "1" and "Time up!" in the game's text codes
    /// as held in RAM (end 0xFFFF), or empty for none.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ushort>> CountdownTexts { get; init; } = [];

    /// <summary>True if the module is built into the ROM.</summary>
    public bool IsNeeded => RequireExpansionPak || DebugOutput || Conditions.Count > 0 || DistanceRow || PenaltyDigits.Count > 0;
}

/// <summary>
/// The editor's new program code for the game (US version): built from
/// native/game_module/module.c (see <see cref="GameModuleImage"/>), loaded at the start into the
/// code area (<see cref="CodeArea"/>).
/// </summary>
/// <remarks>
/// <para>The module image is placed into the last <see cref="RomReserve"/> bytes of the ROM, which
/// are free in the original (padding), and the image of the data area (<see cref="DataArea"/>, the
/// mission settings) into the <see cref="DataArea.RomReserve"/> bytes in front of it. Before it is
/// written, the editor fills in the header: flags, ROM offsets, sizes and a line of information about
/// the build.</para>
/// <para><b>Expansion Pak</b>: with <see cref="GameModuleOptions.RequireExpansionPak"/> the module checks the
/// RAM size first; with only 4 MB it shows a black screen with "EXPANSION PAK REQUIRED" and stops.</para>
/// <para><b>Debug output</b> goes to the IS-Viewer port (0xB3FF0000), which emulators show (ares:
/// start it with --terminal, or Tools › Tracer "ISViewer"; mupen64plus and simple64: their log),
/// and into a ring buffer in RAM.</para>
/// <para><b>Startup information</b>: the calls of the startup steps (<see cref="GameModuleImage.StartupSites"/>)
/// go to hook functions that report start and end of each step, then call the original.</para>
/// <para><b>Game messages</b>: the first instructions of the game's empty _uvDebugPrintf jump to a
/// gate in the code cave, which passes the call on to the module once it is loaded.</para>
/// <para><b>On screen</b>: the module puts itself into the frame callback of uvGfxEnd while it
/// has messages to show (the game itself uses it only for snow) and draws them into the finished
/// frame with a small font of its own.</para>
/// <para><b>Scoring and conditions</b>: the scoring function (func_8032B560, ROM 0xB2AE4) asks
/// module_conditions instead of taskGet_80346364 for the condition mode. It adds the time points when
/// the goal ring or the ball target ended the mission, scores the hard end time as set per mission,
/// lets hopper goals and ball targets count (game patch <see cref="GamePatches.GoalObjects"/>), and for
/// the missions with conditions in the table it applies them itself and returns 0 (no retail
/// condition); for all others it returns the retail mode.</para>
/// <para><b>Live values</b> (with the debug output): the vehicles call func_802EDAF0 once per
/// flight frame (distance, highest altitude, end of the flight); with live values these calls go
/// through module_flight_frame, which shows distance, highest altitude and the speeds in a line on
/// the screen and once a second on the debug port.</para>
/// <para><b>Distance row</b>: the result sheet has no case for row type 10 (distance points); its
/// jump table entry then points to a two-instruction stub in the module that loads them.</para>
/// <para><b>"n to go"</b>: when a ring is passed or a target hit, the game asks func_8032C080 how many
/// objects are still needed and shows one less on the HUD; the retail game answers only in condition
/// mode 3. For missions in the table that need a number of rings or targets the module answers
/// instead, without ever reporting "reached" (no "please land").</para>
/// <para><b>Penalty messages</b>: the HUD texts "5 pts. deducted" (Gyrocopter hit) and "2 pts. deducted"
/// (Rocket Belt hit, Jumble Hopper leaving the area) are fixed. Their hudWarningText calls go to the
/// module, which shows the message with the mission's own number (COMM+0x3B4, COMM+0x414), or none
/// if nothing is deducted.</para>
/// </remarks>
public static class GameModule
{
    /// <summary>Room at the end of the ROM for the module image.</summary>
    public const int RomReserve = 0x10000;

    /// <summary>Room at the end of the ROM for the module and the data area in front of it.</summary>
    public const int TotalRomReserve = RomReserve + DataArea.RomReserve;

    /// <summary>Header: magic number 'HHGM'.</summary>
    public const uint Magic = 0x4848474D;

    private const int FlagsOffset = 0x0C;
    private const int RomOffsetOffset = 0x10;
    private const int ImageSizeOffset = 0x14;
    private const int ConditionCountOffset = 0x2C;
    private const int InfoOffset = 0x30;
    private const int InfoLength = 96;
    private const int LiveOffset = 0x90;
    private const int MetTextOffset = 0x94;
    private const int MetTextLength = 32;
    private const int DigitsOffset = 0xD4;
    private const int DataRomOffset = 0xE8;
    private const int DataSizeOffset = 0xEC;
    private const int CountdownOffset = 0xF0;
    private const int CountdownTexts = 6;
    private const int CountdownLength = 12;

    /// <summary>Header flag: stop with a message without the Expansion Pak.</summary>
    private const uint FlagRequirePak = 32;

    /// <summary>Header flag: the game patch "goal objects" is applied.</summary>
    private const uint FlagGoalObjects = 64;

    /// <summary>jal hudWarningText.</summary>
    private const uint HudWarningCall = 0x0C0C766E;

    /// <summary>The hudWarningText calls of the hit messages: Gyrocopter "5 pts. deducted" (0x802F6C10), Rocket Belt "2 pts. deducted" (0x80325B94).</summary>
    private static readonly int[] HitMessageCalls = [0x7E140, 0xAD0C4];

    /// <summary>The hudWarningText call of the Jumble Hopper's "2 pts. deducted" when it leaves the area (0x802FBF30).</summary>
    private const int AreaMessageCall = 0x83460;

    /// <summary>The calls of func_802EDAF0 (once per flight frame) in the vehicles' code, as RAM addresses.</summary>
    private static readonly uint[] FlightFrameCalls =
        [0x802D7D2C, 0x802D8A18, 0x802ED288, 0x802ED370, 0x802ED45C, 0x802ED53C, 0x802ED5CC, 0x802ED68C];

    /// <summary>The calls of hudDrawFuel (in hudDrawRocketPack and hudDrawGyrocopter), as RAM addresses.</summary>
    private static readonly uint[] HudFuelCalls = [0x80318860, 0x8031A1B4];

    /// <summary>"addiu t9, zero, 1; sb t9, 0xA1(t2)" (hard end reached) in the flight update of game.c (0x802EDAD4).</summary>
    public const int HardEndRomOffset = 0x75004;

    /// <summary>The calls of sndSetMusic of the Hang Glider's landing jingle (func_802EEB00).</summary>
    private static readonly int[] HangGliderMusicCalls = [0x760D4, 0x76118, 0x76128];

    /// <summary>The calls of sndSetMusicState after them.</summary>
    private static readonly int[] HangGliderMusicStateCalls = [0x760DC, 0x76130];

    /// <summary>The call of hudDrawStartText in hudMainRender (0x803186A4).</summary>
    public const int HudStartTextCallRomOffset = 0x9FBD4;

    /// <summary>jal sndSetMusic.</summary>
    private const uint SetMusicCall = 0x0C0CFDD2;

    /// <summary>jal sndSetMusicState.</summary>
    private const uint SetMusicStateCall = 0x0C0CFE59;

    /// <summary>jal hudDrawFuel.</summary>
    private const uint HudFuelCall = 0x0C0C707A;

    /// <summary>jal func_802EDAF0.</summary>
    private const uint FlightFrameCall = 0x0C0BB6BC;

    /// <summary>ROM offset of the jump table entry of result row type 10 (resultInit, table at 0x80355CA8).</summary>
    public const int DistanceRowTableRomOffset = (int)(0x80355CA8u + (9 * 4) - 0x80278AD0u);

    /// <summary>The original entry: the "no points" case.</summary>
    private const uint DistanceRowOriginal = 0x8032E598;

    /// <summary>Number of object types of a condition: rings, balloons, targets, hover pads, Meca Hawks, ball targets, hopper goals.</summary>
    public const int ObjectTypes = 7;

    /// <summary>"All objects of this type" in <see cref="GameModuleCondition.Required"/>.</summary>
    public const byte RequiredAll = 0xFF;

    /// <summary>ROM offset of "jal taskGet_80346364" in the scoring function (func_8032B560).</summary>
    public const int ConditionModeCallRomOffset = 0xB2AE4;

    /// <summary>The call of func_8032C080 ("objects still needed") when a ring is passed (rings_80323FFC).</summary>
    public const int RingsLeftCallRomOffset = 0xAB55C;

    /// <summary>The call of func_8032C080 when a target is hit (targets_8034473C).</summary>
    public const int TargetsLeftCallRomOffset = 0xCBD10;

    /// <summary>jal func_8032C080.</summary>
    private const uint ObjectsLeftCall = 0x0C0CB020;

    /// <summary>ROM offset of the game's _uvDebugPrintf (kernel, 0x8022ED14): two instructions are replaced.</summary>
    public const int DebugPrintfRomOffset = 0x2FCC4;

    /// <summary>The module image with its header filled in.</summary>
    /// <param name="options">What the module does.</param>
    /// <param name="romOffset">Where the image is placed in the ROM.</param>
    /// <param name="info">A line about the build (project, time); only ASCII is kept.</param>
    /// <param name="dataRomOffset">Where the image of the data area lies in the ROM.</param>
    /// <param name="dataSize">Size of that image (0: none).</param>
    public static byte[] CreateImage(GameModuleOptions options, int romOffset, string info, int dataRomOffset = 0, int dataSize = 0)
    {
        byte[] image = GameModuleImage.Image;
        if (BinaryPrimitives.ReadUInt32BigEndian(image) != Magic)
        {
            throw new InvalidDataException("The game module has no valid header.");
        }

        if (image.Length > RomReserve || GameModuleImage.EndAddress > CodeArea.End)
        {
            throw new InvalidDataException("The game module is too large for its room.");
        }

        bool live = options.DebugOutput && options.Live != LiveValues.None;
        uint flags = (options.StartupInfo ? 1u : 0) | (options.GameMessages ? 2u : 0)
            | (options.DebugOutput ? 4u : 0) | (options.OnScreen ? 8u : 0) | (live ? 16u : 0)
            | (options.RequireExpansionPak ? FlagRequirePak : 0) | (options.GoalObjects ? FlagGoalObjects : 0);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(FlagsOffset), flags);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(LiveOffset), live ? (uint)options.Live : 0);
        if (options.RequirementMetText.Count > MetTextLength)
        {
            throw new InvalidDataException($"The HUD text of the game module has room for {MetTextLength - 1} characters.");
        }

        for (int i = 0; i < options.RequirementMetText.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(MetTextOffset + (i * 2)), options.RequirementMetText[i]);
        }
        if (options.PenaltyDigits.Count is not (0 or 10))
        {
            throw new InvalidDataException("The game module needs the codes of the ten digits.");
        }

        for (int i = 0; i < options.PenaltyDigits.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(DigitsOffset + (i * 2)), options.PenaltyDigits[i]);
        }

        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(RomOffsetOffset), (uint)romOffset);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(ImageSizeOffset), (uint)AlignedSize(image.Length));

        var ascii = new StringBuilder();
        foreach (char c in info)
        {
            if (c is >= ' ' and <= '~' && ascii.Length < InfoLength - 1)
            {
                ascii.Append(c);
            }
        }

        Span<byte> infoField = image.AsSpan(InfoOffset, InfoLength);
        infoField.Clear();
        Encoding.ASCII.GetBytes(ascii.ToString()).CopyTo(infoField);

        if (options.Conditions.Count > GameModuleImage.ConditionsCapacity)
        {
            throw new InvalidDataException($"The game module has room for {GameModuleImage.ConditionsCapacity} missions with conditions.");
        }

        if (options.CountdownTexts.Count is not (0 or CountdownTexts)
            || options.CountdownTexts.Any(t => t.Count > CountdownLength || t.Count == 0 || t[^1] != 0xFFFF))
        {
            throw new InvalidDataException($"The countdown of the game module needs {CountdownTexts} texts of up to {CountdownLength - 1} characters.");
        }

        for (int i = 0; i < options.CountdownTexts.Count; i++)
        {
            for (int k = 0; k < options.CountdownTexts[i].Count; k++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(CountdownOffset + (((i * CountdownLength) + k) * 2)), options.CountdownTexts[i][k]);
            }
        }

        // The mission settings themselves are in the data area (DataArea.CreateImage).
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(ConditionCountOffset), (uint)options.Conditions.Count);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(DataRomOffset), (uint)dataRomOffset);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(DataSizeOffset), (uint)AlignedSize(dataSize));
        return image;
    }

    /// <summary>Size of the image as loaded (rounded up to 8 bytes for the DMA).</summary>
    public static int AlignedSize(int length) => (length + 7) & ~7;

    /// <summary>The changes of the game code for the module: code area, loader, gate and the startup hooks.</summary>
    /// <param name="options">What the module does.</param>
    /// <param name="romOffset">Where the image lies in the ROM.</param>
    /// <param name="imageLength">Length of the image.</param>
    public static IReadOnlyList<WordPatch> Patches(GameModuleOptions options, int romOffset, int imageLength)
    {
        var patches = new List<WordPatch>(CodeArea.Patches(romOffset, AlignedSize(imageLength)));
        patches.AddRange(DataArea.Patches());

        // The developers' messages: _uvDebugPrintf jumps to the gate (which returns while the module is not loaded).
        if (options.DebugOutput && options.GameMessages)
        {
            patches.Add(new WordPatch("debug messages, _uvDebugPrintf", DebugPrintfRomOffset,
                [0x27BDFFF8, 0xAFA40008], [CodeArea.J(CodeArea.PrintfGateAddress), 0x00000000]));
        }

        // Scoring: the scoring function always asks the module (a0 = the flight result, s0): time points at the
        // goal, hard end time, goal objects and the conditions of the missions in the table.
        patches.Add(new WordPatch("scoring and conditions, func_8032B560", ConditionModeCallRomOffset,
            [0x0C0D18D9, 0x00000000], [CodeArea.Jal(GameModuleImage.ConditionsHookAddress), 0x02002025]));

        if (options.Conditions.Any(c => c.HasConditions))
        {

            // "n to go" on the HUD: the ring and target code ask the module how many are still needed.
            patches.Add(new WordPatch("rings left, rings_80323FFC", RingsLeftCallRomOffset,
                [ObjectsLeftCall], [CodeArea.Jal(GameModuleImage.RingsLeftHookAddress)]));
            patches.Add(new WordPatch("targets left, targets_8034473C", TargetsLeftCallRomOffset,
                [ObjectsLeftCall], [CodeArea.Jal(GameModuleImage.TargetsLeftHookAddress)]));
        }

        // Live values and unlimited fuel: the vehicles' flight frame goes through the module.
        if ((options.DebugOutput && options.Live != LiveValues.None) || options.Conditions.Any(c => c.UnlimitedFuel))
        {
            foreach (uint call in FlightFrameCalls)
            {
                patches.Add(new WordPatch($"live values, flight frame at 0x{call:X8}", (int)(call - 0x80278AD0),
                    [FlightFrameCall], [CodeArea.Jal(GameModuleImage.FlightFrameHookAddress)]));
            }
        }

        // Unlimited fuel: no fuel gauge on the HUD.
        if (options.Conditions.Any(c => c.UnlimitedFuel))
        {
            foreach (uint call in HudFuelCalls)
            {
                patches.Add(new WordPatch($"fuel gauge, hudDrawFuel call at 0x{call:X8}", (int)(call - 0x80278AD0),
                    [HudFuelCall], [CodeArea.Jal(GameModuleImage.HudFuelHookAddress)]));
            }
        }

        // Penalty messages with the mission's number.
        if (options.PenaltyDigits.Count > 0)
        {
            foreach (int call in HitMessageCalls)
            {
                patches.Add(new WordPatch($"penalty message, hit at 0x{call:X6}", call,
                    [HudWarningCall], [CodeArea.Jal(GameModuleImage.PenaltyHitHookAddress)]));
            }

            patches.Add(new WordPatch("penalty message, Jumble Hopper out of the area", AreaMessageCall,
                [HudWarningCall], [CodeArea.Jal(GameModuleImage.PenaltyAreaHookAddress)]));
        }

        // Hard end time with a setting: the moment it is reached goes through the module (jingle), and the
        // Hang Glider's landing jingle in its result frames is skipped then.
        if (options.Conditions.Any(c => c.HardEnd != HardEndScoring.Retail))
        {
            patches.Add(new WordPatch("hard end reached, game.c", HardEndRomOffset,
                [0x24190001, 0xA15900A1], [CodeArea.Jal(GameModuleImage.HardEndAddress), 0x01402025]));
            foreach (int call in HangGliderMusicCalls)
            {
                patches.Add(new WordPatch($"Hang Glider landing jingle at 0x{call:X6}", call,
                    [SetMusicCall], [CodeArea.Jal(GameModuleImage.HangGliderMusicAddress)]));
            }

            foreach (int call in HangGliderMusicStateCalls)
            {
                patches.Add(new WordPatch($"Hang Glider landing jingle (play) at 0x{call:X6}", call,
                    [SetMusicStateCall], [CodeArea.Jal(GameModuleImage.HangGliderMusicStateAddress)]));
            }

            // The countdown of the last 5 seconds: hudMainRender calls hudDrawStartText through the module.
            if (options.CountdownTexts.Count > 0)
            {
                patches.Add(new WordPatch("hard end countdown, hudMainRender", HudStartTextCallRomOffset,
                    [0x0C0C7700], [CodeArea.Jal(GameModuleImage.HudTextAddress)]));
            }
        }

        // Distance points on the result sheet: the jump table entry of row type 10.
        if (options.DistanceRow)
        {
            patches.Add(new WordPatch("distance row, resultInit jump table", DistanceRowTableRomOffset,
                [DistanceRowOriginal], [GameModuleImage.DistanceRowAddress]));
        }

        if (options.DebugOutput && options.StartupInfo)
        {
            foreach (GameModuleCallSite site in GameModuleImage.StartupSites)
            {
                patches.Add(new WordPatch($"startup step {site.Name}", site.RomOffset, [site.OriginalWord], [site.HookWord]));
            }
        }

        return patches;
    }

    /// <summary>
    /// Writes the module into a ROM: the image at its place, the image of the data area in front of it
    /// and all patches of the game code.
    /// </summary>
    /// <param name="rom">The ROM.</param>
    /// <param name="options">What the module does.</param>
    /// <param name="romOffset">Where the module goes; the data area goes <see cref="DataArea.RomReserve"/> bytes in front.</param>
    /// <param name="info">A line about the build.</param>
    /// <exception cref="InvalidDataException">The game code is not the expected one.</exception>
    public static void Apply(byte[] rom, GameModuleOptions options, int romOffset, string info)
    {
        int dataRomOffset = romOffset - DataArea.RomReserve;
        byte[] data = DataArea.CreateImage(options.Conditions);
        byte[] image = CreateImage(options, romOffset, info, dataRomOffset, data.Length);
        if (dataRomOffset < 0 || romOffset + RomReserve > rom.Length)
        {
            throw new InvalidDataException("The game module does not fit into the ROM.");
        }

        IReadOnlyList<WordPatch> patches = Patches(options, romOffset, image.Length);
        foreach (WordPatch patch in patches)
        {
            patch.Check(rom);
        }

        rom.AsSpan(dataRomOffset, DataArea.RomReserve).Fill(0xFF);
        data.CopyTo(rom, dataRomOffset);
        rom.AsSpan(dataRomOffset + data.Length, AlignedSize(data.Length) - data.Length).Clear();

        rom.AsSpan(romOffset, RomReserve).Fill(0xFF);
        image.CopyTo(rom, romOffset);
        rom.AsSpan(romOffset + image.Length, AlignedSize(image.Length) - image.Length).Clear();
        foreach (WordPatch patch in patches)
        {
            patch.Write(rom);
        }
    }
}
