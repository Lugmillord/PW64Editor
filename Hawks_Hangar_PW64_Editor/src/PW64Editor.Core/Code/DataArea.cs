using System.Buffers.Binary;

namespace PW64Editor.Core.Code;

/// <summary>
/// The editor's data area: RAM in front of the game's program block for new tables (US version).
/// So far it holds the mission settings table (one entry per mission with settings), which the game
/// module copies from the ROM at the start.
/// </summary>
/// <remarks>
/// <para><b>Memory</b>: the program block of the memory manager started at 0x80200000 (since the code
/// fix <see cref="CodeFixes.ExpandedTexts"/> at 0x801FE000, in front of the text tables). It now starts
/// at <see cref="Start"/>, so the heap ends there (uvMemInitBlocks), and the clearing of free memory
/// at every level start stops there (uvMemClearRegions). New data grows downwards from here.</para>
/// <para><b>Layout</b>: a header of <see cref="HeaderSize"/> bytes (magic 'HHMD', version, number of
/// missions, entry size), then one entry of <see cref="MissionEntrySize"/> bytes per mission:</para>
/// <code>
/// +0x00 u8  vehicle (COMM byte 1)      +0x01 u8 class (COMM byte 0)   +0x02 u8 test (COMM byte 2)
/// +0x03 u8  flags: 1 crash gives 0, 2 minimum altitude, 4 minimum distance, 8 unlimited fuel,
///           16 hard end time scored (like a flight ending in the air), 32 hard end time gives 0 points
/// +0x04 u8[7] required objects per type (0 = no, 0xFF = all, otherwise at least this many)
/// +0x0C f32 minimum altitude (game units)  +0x10 f32 minimum distance (game units)
/// +0x03 flags (continued): 64 time points after a crash, 128 own curve of the Gyrocopter's touchdown point
/// +0x14 u8  jingle when the hard end time is reached (music number, 0 = none)
/// +0x15 u8  landing flags: 1 pad radius, 2 speed limits of the vehicle
/// +0x18 curve (0x54 bytes, as in the mission file): the Gyrocopter's touchdown point
/// +0x6C f32 radius of the landing pads (game units)
/// +0x70 f32[4] speed limits of the vehicle (LandingValues.SpeedsOf; HG km/h, GC and RB units per second)
/// +0x80 u8  weather flags: 1 fog colour, 2 sky tinted, 4 view distance, 8 fog range, 16 snow/rain settings
/// +0x81 u8  1 snow, 2 rain                +0x82 u16 colour of the snow/rain (RGBA 5551)
/// +0x84 u8[3] fog colour r, g, b          +0x88 f32 view distance (game units)
/// +0x8C s32 fog multiplier, +0x90 s32 fog offset (gSPFogPosition: 128000 / (max − min), (500 − min) × 256 / (max − min))
/// +0x94 f32 fall speed (units per second) +0x98 u16 number of flakes/drops
/// +0x9A ... reserved (0)
/// </code>
/// <para>Header: +0x00 'HHMD', +0x04 version, +0x08 missions, +0x0C entry size, +0x10 size of the result tables
/// (0 = none), +0x14 u16 the Birdman missions with points (bit class × 4 + test), +0x18 RAM address of the class table
/// (medal limits per vehicle and class, <see cref="Classes.ClassLimits"/>; 0 = none), +0x1C RAM address of the unlock
/// table (conditions of the menu fields, <see cref="Classes.MenuUnlocks"/>; 0 = the game's rules). At +0x50 the Birdman's two landing
/// limits, which its patched code reads. At +0x60 the view distance for cameraInit (weather). At +0x28 the RAM address of a raised ring list and its limit, at +0x30 that of a raised balloon list (0 = the game's
/// lists; <see cref="HighDataArea.ObjectLists"/>), which the module's conditions count. At +0x34 the size of the photo table
/// (<see cref="Missions.PhotoSubjects"/>, 0xD000 bytes behind the image in the ROM; the module copies it to 0x807F1000). At +0x20 the RAM address of the island table of the environments (header +0x80, <see cref="Missions.WeatherEnvironments"/>; 0 = none). At +0x40 the values the game code reads while flying (written by the module in every flight frame,
/// see <see cref="LiveLandingAddress"/>).</para>
/// <para>Behind the room for <see cref="MissionCapacity"/> entries (at <see cref="Missions.ResultTables.Address"/>)
/// the result tables of the game patch <see cref="GamePatches.BonusGames"/>, when it is applied; the image then
/// reaches up to their end.</para>
/// <para><b>ROM</b>: the image lies in the <see cref="RomReserve"/> bytes in front of the game module
/// at the end of the ROM; the module header holds its offset and size.</para>
/// </remarks>
public static class DataArea
{
    /// <summary>Start of the data area (new start of the program block).</summary>
    public const uint Start = 0x801F5000;

    /// <summary>End of the data area: the text tables of <see cref="CodeFixes.ExpandedTexts"/>.</summary>
    public const uint End = CodeFixes.TextNameTableAddress;

    /// <summary>Header: magic number 'HHMD'.</summary>
    public const uint Magic = 0x48484D44;

    /// <summary>Layout version of the data area (2: the mission tables in the lower data area, <see cref="DataExtension"/>).</summary>
    public const uint Version = 2;

    /// <summary>Size of the header in front of the mission entries.</summary>
    public const int HeaderSize = 0x100;

    /// <summary>Bytes per mission entry (room for the settings of later patches).</summary>
    public const int MissionEntrySize = 0x100;

    /// <summary>Number of missions the table has room for.</summary>
    public const int MissionCapacity = 128;

    /// <summary>Room in the ROM for the image, in front of the game module.</summary>
    public const int RomReserve = 0x10000;

    /// <summary>
    /// RAM address of the Hang Glider's four current speed limits (f32: landing vertical, landing forward,
    /// good landing vertical, good landing forward, km/h), which its patched landing code loads instead of
    /// the immediates 50 and 15. The module writes the mission's values (or the original ones) there.
    /// </summary>
    public const uint LiveLandingAddress = Start + 0x40;

    /// <summary>Offset of the touchdown curve in an entry.</summary>
    public const int TouchdownCurveOffset = 0x18;

    /// <summary>RAM address of the first mission entry.</summary>
    public const uint MissionTableAddress = Start + HeaderSize;

    static DataArea()
    {
        if (HeaderSize + (MissionCapacity * MissionEntrySize) > End - Start)
        {
            throw new InvalidOperationException("The mission settings table does not fit into the data area.");
        }
    }

    /// <summary>Header offset of the size of the result tables (0 = none).</summary>
    public const int ResultTablesSizeOffset = 0x10;

    /// <summary>Header offset of the mask of the Birdman missions with points (u16, bit class × 4 + test; layout version 1 only,
    /// since version 2 they are in the lower data area, <see cref="DataExtension"/>).</summary>
    public const int BirdmanPointsOffset = 0x14;

    /// <summary>Header: RAM address of the class table (<see cref="Classes.ClassLimits"/>), 0 = none.</summary>
    public const int ClassTableOffset = 0x18;

    /// <summary>Header: RAM address of the unlock table (<see cref="Classes.MenuUnlocks"/>), 0 = the game's rules.</summary>
    public const int UnlockTableOffset = 0x1C;

    /// <summary>
    /// RAM address of the view distance (f32) cameraInit gives a camera when the weather patches are applied (its
    /// "2000.0f" loads it); the module writes the mission's view distance there at the start of a mission (weather.c).
    /// </summary>
    public const uint LiveViewAddress = Start + 0x60;

    /// <summary>
    /// RAM address of the Birdman's two current landing limits (f32 km/h: vertical, forward), which its patched landing
    /// code loads instead of the immediates 50 (module: the mission's values or the original ones).
    /// </summary>
    public const uint LiveBirdmanAddress = Start + 0x50;

    /// <summary>Header: size of the image of the lower data area (<see cref="DataExtension"/>, 0 = none; version 2).</summary>
    public const int ExtensionSizeOffset = 0x24;

    /// <summary>Header: RAM address of the raised ring list and its limit (0 = the game's gRings, 30).</summary>
    public const int RingListOffset = 0x28;

    /// <summary>Header: RAM address of the raised balloon list (0 = the game's gBalls).</summary>
    public const int BalloonListOffset = 0x30;

    /// <summary>Header: size of the photo table (<see cref="Missions.PhotoSubjects"/>, 0 = none), which lies
    /// <see cref="Missions.PhotoSubjects.RomOffset"/> bytes behind the image in the ROM.</summary>
    public const int PhotoTableSizeOffset = 0x34;

    /// <summary>Layout version 1 (until Step 40): the result tables [7][4][5] at this address of the data area.</summary>
    public const uint Version1ResultTablesAddress = MissionTableAddress + (MissionCapacity * MissionEntrySize);

    /// <summary>Layout version 1: the class table [7][4] at this address.</summary>
    public const uint Version1ClassTableAddress = 0x801FDC00;

    /// <summary>
    /// The images of the data area and of its lower part for a build: the mission entries, the unlock and environment
    /// tables in the data area; the mission list, the Birdman missions with points, the photo missions, the result tables
    /// and the class table in the lower part (<see cref="DataExtension"/>).
    /// </summary>
    /// <exception cref="InvalidDataException">A table does not fit.</exception>
    public static DataImages CreateImages(GameModuleOptions options)
    {
        IReadOnlyList<int> files = options.MissionFiles ?? DataExtension.GameMissionFiles;
        byte[] extension = DataExtension.Create(files, options.BirdmanPointMissions, options.PhotoMissions ?? DataExtension.GamePhotoMissions,
            options.ResultTables, options.ClassTable);
        byte[] main = CreateImage(options.Conditions, options.UnlockTable, options.EnvironmentTable,
            options.ResultTables?.Length ?? 0, options.ClassTable is not null, extension.Length);

        // The raised lists the module counts itself (rings and balloons for the conditions, module_conditions).
        IReadOnlyDictionary<Missions.MissionObjectKind, int> raised = options.ObjectLimits ?? new Dictionary<Missions.MissionObjectKind, int>();
        IReadOnlyDictionary<Missions.MissionObjectKind, uint> lists = HighDataArea.ObjectLists(raised);
        if (lists.TryGetValue(Missions.MissionObjectKind.Ring, out uint rings))
        {
            BinaryPrimitives.WriteUInt32BigEndian(main.AsSpan(RingListOffset), rings);
            BinaryPrimitives.WriteUInt32BigEndian(main.AsSpan(RingListOffset + 4), (uint)raised[Missions.MissionObjectKind.Ring]);
        }

        if (lists.TryGetValue(Missions.MissionObjectKind.Balloon, out uint balloons))
        {
            BinaryPrimitives.WriteUInt32BigEndian(main.AsSpan(BalloonListOffset), balloons);
        }

        byte[] photos = Missions.PhotoSubjects.CreateTable(options.PhotoSubjects);
        BinaryPrimitives.WriteUInt32BigEndian(main.AsSpan(PhotoTableSizeOffset), (uint)photos.Length);
        return new DataImages(main, extension, photos);
    }

    /// <summary>
    /// The image of the data area as the module copies it: header and the used entries, and the unlock table at its place.
    /// </summary>
    /// <param name="missions">Missions with settings.</param>
    /// <param name="unlockTable">The unlock table, or null.</param>
    /// <param name="environmentTable">The island table of the environments, or null.</param>
    /// <param name="resultTablesSize">Size of the result tables in the lower data area (0 = none).</param>
    /// <param name="classTable">True if the lower data area has the class table.</param>
    /// <param name="extensionSize">Size of the image of the lower data area.</param>
    /// <exception cref="InvalidDataException">More missions than the table has room for, or a table does not fit.</exception>
    public static byte[] CreateImage(IReadOnlyList<GameModuleCondition> missions, byte[]? unlockTable = null, byte[]? environmentTable = null,
        int resultTablesSize = 0, bool classTable = false, int extensionSize = 0)
    {
        if (missions.Count > MissionCapacity)
        {
            throw new InvalidDataException($"The mission settings table has room for {MissionCapacity} missions.");
        }

        int unlockOffset = (int)(Classes.MenuUnlocks.TableAddress - Start);
        if (unlockTable is not null && (unlockTable.Length != Classes.MenuUnlocks.TableSize
            || Classes.MenuUnlocks.TableAddress < MissionTableAddress + (MissionCapacity * MissionEntrySize)
            || Classes.MenuUnlocks.TableAddress + Classes.MenuUnlocks.TableSize > End))
        {
            throw new InvalidDataException("The unlock table does not fit into the data area.");
        }

        byte[] image = new byte[unlockTable is not null ? unlockOffset + unlockTable.Length : HeaderSize + (missions.Count * MissionEntrySize)];
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x00), Magic);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x04), Version);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x08), (uint)missions.Count);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x0C), MissionEntrySize);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(ResultTablesSizeOffset), (uint)resultTablesSize);
        BinaryPrimitives.WriteSingleBigEndian(image.AsSpan((int)(LiveViewAddress - Start)), 2000f); // the game's view distance
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(ExtensionSizeOffset), (uint)((extensionSize + 7) & ~7));
        if (classTable)
        {
            BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(ClassTableOffset), DataExtension.ClassTableAddress);
        }

        if (unlockTable is not null)
        {
            BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(UnlockTableOffset), Classes.MenuUnlocks.TableAddress);
            unlockTable.CopyTo(image, unlockOffset);
        }

        if (environmentTable is not null)
        {
            if (environmentTable.Length != Missions.WeatherEnvironments.TableSize)
            {
                throw new InvalidDataException("The environment table has the wrong size.");
            }

            BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(Missions.WeatherEnvironments.TableAddressOffset), Missions.WeatherEnvironments.TableAddress);
            environmentTable.CopyTo(image, (int)(Missions.WeatherEnvironments.TableAddress - Start));
        }

        for (int i = 0; i < missions.Count; i++)
        {
            WriteEntry(image.AsSpan(HeaderSize + (i * MissionEntrySize), MissionEntrySize), missions[i]);
        }

        return image;
    }

    private static void WriteEntry(Span<byte> entry, GameModuleCondition c)
    {
        entry[0] = c.Vehicle;
        entry[1] = c.Class;
        entry[2] = c.Test;
        entry[3] = (byte)((c.CrashGivesZero ? 1 : 0) | (c.MinimumAltitude is not null ? 2 : 0)
            | (c.MinimumDistance is not null ? 4 : 0) | (c.UnlimitedFuel ? 8 : 0)
            | (c.HardEnd == HardEndScoring.Scored ? 16 : 0) | (c.HardEnd == HardEndScoring.Zero ? 32 : 0)
            | (c.CrashTimePoints ? 64 : 0) | (c.TouchdownCurve is not null ? 128 : 0));
        entry[0x15] = (byte)((c.PadRadius is not null ? 1 : 0) | (c.VehicleLimits.Count > 0 ? 2 : 0));
        if (c.TouchdownCurve is { } curve)
        {
            if (curve.Count is < 1 or > Missions.ScoreCurve.MaxPoints)
            {
                throw new InvalidDataException("A touchdown curve needs 1 to 10 points.");
            }

            BinaryPrimitives.WriteInt32BigEndian(entry[TouchdownCurveOffset..], curve.Count);
            for (int i = 0; i < curve.Count; i++)
            {
                BinaryPrimitives.WriteSingleBigEndian(entry[(TouchdownCurveOffset + 4 + (i * 8))..], curve[i].X);
                BinaryPrimitives.WriteSingleBigEndian(entry[(TouchdownCurveOffset + 8 + (i * 8))..], curve[i].Y);
            }
        }

        BinaryPrimitives.WriteSingleBigEndian(entry[0x6C..], c.PadRadius ?? 0);
        if (c.VehicleLimits.Count > 4)
        {
            throw new InvalidDataException("A vehicle has at most four speed limits.");
        }

        for (int i = 0; i < c.VehicleLimits.Count; i++)
        {
            BinaryPrimitives.WriteSingleBigEndian(entry[(0x70 + (i * 4))..], c.VehicleLimits[i]);
        }
        for (int t = 0; t < GameModule.ObjectTypes; t++)
        {
            entry[4 + t] = t < c.Required.Count ? c.Required[t] : (byte)0;
        }

        if (c.Weather is { IsEmpty: false } w)
        {
            entry[0x80] = (byte)((w.FogColor is not null ? 1 : 0) | (w.FogColor is not null && w.SkyTint ? 2 : 0)
                | (w.ViewDistance is not null ? 4 : 0) | (w.FogRange is not null ? 8 : 0) | (w.Effect is not null ? 16 : 0));
            if (w.FogColor is { } fog)
            {
                entry[0x84] = (byte)(fog >> 16);
                entry[0x85] = (byte)(fog >> 8);
                entry[0x86] = (byte)fog;
            }

            BinaryPrimitives.WriteSingleBigEndian(entry[0x88..], w.ViewDistance ?? 0);
            if (w.FogRange is { } range)
            {
                BinaryPrimitives.WriteInt32BigEndian(entry[0x8C..], range.Multiplier);
                BinaryPrimitives.WriteInt32BigEndian(entry[0x90..], range.Offset);
            }

            if (w.Effect is { } effect)
            {
                entry[0x81] = (byte)(effect.Rain ? 2 : 1);
                BinaryPrimitives.WriteUInt16BigEndian(entry[0x82..], effect.Color);
                BinaryPrimitives.WriteSingleBigEndian(entry[0x94..], effect.FallSpeed);
                BinaryPrimitives.WriteUInt16BigEndian(entry[0x98..], (ushort)Math.Clamp(effect.Count, 1, 1200));
            }
        }

        BinaryPrimitives.WriteSingleBigEndian(entry[0x0C..], c.MinimumAltitude ?? 0);
        BinaryPrimitives.WriteSingleBigEndian(entry[0x10..], c.MinimumDistance ?? 0);
        entry[0x14] = c.HardEndMusic;
    }

    private static bool IsImage(ReadOnlySpan<byte> image) => image.Length >= HeaderSize && BinaryPrimitives.ReadUInt32BigEndian(image) == Magic;

    private static uint VersionOf(ReadOnlySpan<byte> image) => IsImage(image) ? BinaryPrimitives.ReadUInt32BigEndian(image[0x04..]) : 0;

    /// <summary>
    /// The result tables of a build ([7][8][5], <see cref="Missions.ResultTables"/>), or null if it has none. Tables of layout
    /// version 1 ([7][4][5] in the data area) come back in the current layout.
    /// </summary>
    public static byte[]? ReadResultTables(DataImages images)
    {
        if (!IsImage(images.Main))
        {
            return null;
        }

        int size = (int)BinaryPrimitives.ReadUInt32BigEndian(images.Main.AsSpan(ResultTablesSizeOffset));
        if (size <= 0)
        {
            return null;
        }

        if (VersionOf(images.Main) >= 2)
        {
            return images.Extension is { } ext ? DataExtension.ReadTable(ext, DataExtension.ResultTablesAddress, size) : null;
        }

        int offset = (int)(Version1ResultTablesAddress - Start);
        return offset + (long)size <= images.Main.Length
            ? Missions.ResultTables.FromVersion1(images.Main.AsSpan(offset, size), Version1ResultTablesAddress)
            : null;
    }

    /// <summary>The class table of a build ([7][8], <see cref="Classes.ClassLimits"/>), or null; version 1 tables ([7][4]) are widened.</summary>
    public static byte[]? ReadClassTable(DataImages images)
    {
        if (!IsImage(images.Main))
        {
            return null;
        }

        uint address = BinaryPrimitives.ReadUInt32BigEndian(images.Main.AsSpan(ClassTableOffset));
        if (VersionOf(images.Main) >= 2)
        {
            return address == DataExtension.ClassTableAddress && images.Extension is { } ext
                ? DataExtension.ReadTable(ext, DataExtension.ClassTableAddress, Classes.ClassLimits.TableSize)
                : null;
        }

        int offset = (int)(Version1ClassTableAddress - Start);
        return address == Version1ClassTableAddress && offset + Classes.ClassLimits.Version1TableSize <= images.Main.Length
            ? Classes.ClassLimits.FromVersion1(images.Main.AsSpan(offset, Classes.ClassLimits.Version1TableSize))
            : null;
    }

    /// <summary>The Birdman missions with points of a build (version 1: the mask of header +0x14, bit class × 4 + test).</summary>
    public static IReadOnlyList<SaveGame.TestId> ReadBirdmanPoints(DataImages images)
    {
        if (!IsImage(images.Main))
        {
            return [];
        }

        if (VersionOf(images.Main) >= 2)
        {
            return images.Extension is { } ext ? DataExtension.ReadBirdmanPoints(ext) : [];
        }

        ushort mask = BinaryPrimitives.ReadUInt16BigEndian(images.Main.AsSpan(BirdmanPointsOffset));
        return [.. Enumerable.Range(0, 16).Where(bit => (mask & (1 << bit)) != 0)
            .Select(bit => new SaveGame.TestId(bit / 4, SaveGame.Vehicle.Birdman, bit % 4))];
    }

    /// <summary>The unlock table of an image made by <see cref="CreateImage"/>, or null if it has none.</summary>
    public static byte[]? ReadUnlockTable(ReadOnlySpan<byte> image)
    {
        if (image.Length < HeaderSize || BinaryPrimitives.ReadUInt32BigEndian(image) != Magic
            || BinaryPrimitives.ReadUInt32BigEndian(image[UnlockTableOffset..]) != Classes.MenuUnlocks.TableAddress)
        {
            return null;
        }

        int offset = (int)(Classes.MenuUnlocks.TableAddress - Start);
        return offset + Classes.MenuUnlocks.TableSize <= image.Length ? image.Slice(offset, Classes.MenuUnlocks.TableSize).ToArray() : null;
    }

    /// <summary>The environment table of an image made by <see cref="CreateImage"/>, or null if it has none.</summary>
    public static byte[]? ReadEnvironmentTable(ReadOnlySpan<byte> image)
    {
        int offset = (int)(Missions.WeatherEnvironments.TableAddress - Start);
        return image.Length >= HeaderSize && BinaryPrimitives.ReadUInt32BigEndian(image) == Magic
            && BinaryPrimitives.ReadUInt32BigEndian(image[Missions.WeatherEnvironments.TableAddressOffset..]) == Missions.WeatherEnvironments.TableAddress
                ? image.Slice(offset, Missions.WeatherEnvironments.TableSize).ToArray()
                : null;
    }

    /// <summary>Reads the mission entries of an image made by <see cref="CreateImage"/>.</summary>
    /// <exception cref="InvalidDataException">Not a data area image.</exception>
    public static IReadOnlyList<GameModuleCondition> ReadImage(ReadOnlySpan<byte> image)
    {
        if (image.Length < HeaderSize || BinaryPrimitives.ReadUInt32BigEndian(image) != Magic)
        {
            throw new InvalidDataException("This is not an image of the data area.");
        }

        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(image[0x08..]);
        int entrySize = (int)BinaryPrimitives.ReadUInt32BigEndian(image[0x0C..]);
        if (count < 0 || count > MissionCapacity || entrySize < 0x15 || HeaderSize + ((long)count * entrySize) > image.Length)
        {
            throw new InvalidDataException("The mission table of the data area is damaged.");
        }

        var result = new List<GameModuleCondition>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add(ReadEntry(image.Slice(HeaderSize + (i * entrySize), entrySize)));
        }

        return result;
    }

    /// <summary>
    /// Reads one mission entry (the reverse of WriteEntry). Also reads the shorter entries of older module
    /// versions (0x14 bytes, without the jingle).
    /// </summary>
    public static GameModuleCondition ReadEntry(ReadOnlySpan<byte> entry)
    {
        byte flags = entry[3];
        byte[] required = entry.Slice(4, GameModule.ObjectTypes).ToArray();
        float altitude = BinaryPrimitives.ReadSingleBigEndian(entry[0x0C..]);
        float distance = BinaryPrimitives.ReadSingleBigEndian(entry[0x10..]);
        HardEndScoring hardEnd = (flags & 32) != 0 ? HardEndScoring.Zero : (flags & 16) != 0 ? HardEndScoring.Scored : HardEndScoring.Retail;
        bool full = entry.Length >= 0x80; // entries of module versions before 6 end at 0x14
        byte landing = full ? entry[0x15] : (byte)0;
        List<Missions.CurvePoint>? curve = null;
        if (full && (flags & 128) != 0)
        {
            int count = Math.Clamp(BinaryPrimitives.ReadInt32BigEndian(entry[TouchdownCurveOffset..]), 0, Missions.ScoreCurve.MaxPoints);
            curve = [];
            for (int i = 0; i < count; i++)
            {
                curve.Add(new Missions.CurvePoint(BinaryPrimitives.ReadSingleBigEndian(entry[(TouchdownCurveOffset + 4 + (i * 8))..]),
                    BinaryPrimitives.ReadSingleBigEndian(entry[(TouchdownCurveOffset + 8 + (i * 8))..])));
            }
        }

        var limits = new List<float>();
        if ((landing & 2) != 0)
        {
            int count = Missions.LandingValues.SpeedsOf((SaveGame.Vehicle)entry[0]).Count;
            for (int i = 0; i < count; i++)
            {
                limits.Add(BinaryPrimitives.ReadSingleBigEndian(entry[(0x70 + (i * 4))..]));
            }
        }

        return new GameModuleCondition(entry[1], entry[0], entry[2], (flags & 1) != 0,
            (flags & 2) != 0 ? altitude : null, required, (flags & 4) != 0 ? distance : null,
            UnlimitedFuel: (flags & 8) != 0, HardEnd: hardEnd, HardEndMusic: entry.Length > 0x14 ? entry[0x14] : (byte)0)
        {
            CrashTimePoints = full && (flags & 64) != 0,
            TouchdownCurve = curve,
            PadRadius = (landing & 1) != 0 ? BinaryPrimitives.ReadSingleBigEndian(entry[0x6C..]) : null,
            VehicleLimits = limits,
            Weather = full ? ReadWeather(entry) : null,
        };
    }

    /// <summary>The weather fields of an entry (+0x80), or null if it has none.</summary>
    private static GameModuleWeather? ReadWeather(ReadOnlySpan<byte> entry)
    {
        byte flags = entry[0x80];
        if (flags == 0)
        {
            return null;
        }

        return new GameModuleWeather(
            (flags & 1) != 0 ? ((uint)entry[0x84] << 16) | ((uint)entry[0x85] << 8) | entry[0x86] : null,
            (flags & 2) != 0,
            (flags & 4) != 0 ? BinaryPrimitives.ReadSingleBigEndian(entry[0x88..]) : null,
            (flags & 8) != 0 ? (BinaryPrimitives.ReadInt32BigEndian(entry[0x8C..]), BinaryPrimitives.ReadInt32BigEndian(entry[0x90..])) : null,
            (flags & 16) != 0
                ? new GameModuleEffect(entry[0x81] == 2, BinaryPrimitives.ReadUInt16BigEndian(entry[0x82..]),
                    BinaryPrimitives.ReadSingleBigEndian(entry[0x94..]), BinaryPrimitives.ReadUInt16BigEndian(entry[0x98..]))
                : null);
    }

    /// <summary>
    /// The changes of the memory manager that keep the heap out of the data area. They work on the
    /// original game code and on the code of <see cref="CodeFixes.ExpandedTexts"/>.
    /// </summary>
    public static IReadOnlyList<WordPatch> Patches()
    {
        uint hi = (DataExtension.Start + 0x8000) >> 16;   // lui/addiu pair: addiu adds a signed value
        uint lo = DataExtension.Start & 0xFFFF;
        return
        [
            // uvMemInitBlocks: start of the program block. The original loads 0x80200000 with one lui and needs
            // "lui t3, 0x8000" for block 5; ExpandedTexts already freed that instruction (block 5 starts at 0,
            // "sw zero"). Here the same is done with the start of the data area.
            new WordPatch("data area, program block start in uvMemInitBlocks", 0x2B3AC,
                [0x3C098020, 0x254A05E0, 0x3C0B8000, 0xAC4E0000, 0xAC440004, 0xAC440008, 0xAC4F000C, 0xAC580010,
                    0xAC590014, 0xAC450018, 0xAC48001C, 0xAC490020, 0xAC4A0024, 0xAC4B0028, 0xAC45002C],
                [0x3C090000 | hi, 0x254A05E0, 0x25290000 | lo, 0xAC4E0000, 0xAC440004, 0xAC440008, 0xAC4F000C, 0xAC580010,
                    0xAC590014, 0xAC450018, 0xAC48001C, 0xAC490020, 0xAC4A0024, 0xAC400028, 0xAC45002C],
                [0x3C098020, 0x254A05E0, 0x2529E000, 0xAC4E0000, 0xAC440004, 0xAC440008, 0xAC4F000C, 0xAC580010,
                    0xAC590014, 0xAC450018, 0xAC48001C, 0xAC490020, 0xAC4A0024, 0xAC400028, 0xAC45002C]),
            // uvMemClearRegions: the clearing of free memory stops in front of the data area.
            new WordPatch("data area, memory clearing in uvMemClearRegions", 0x2B44C,
                [0x3C038020, 0x246300A0, 0x3C028012, 0x10600005, 0x34425800],
                [0x3C030000 | hi, 0x24630000 | lo, 0x3C028012, 0x10600005, 0x34425800],
                [0x3C038020, 0x2463E000, 0x3C028012, 0x10600005, 0x34425800]),
        ];
    }
}

/// <summary>The image of the data area and the image of its lower part (<see cref="DataExtension"/>) of a build.</summary>
/// <param name="Main">The data area (header, mission entries, unlock table).</param>
/// <param name="Extension">The lower data area, or null (builds before layout version 2).</param>
public sealed record DataImages(byte[] Main, byte[]? Extension, byte[]? Photos = null);
