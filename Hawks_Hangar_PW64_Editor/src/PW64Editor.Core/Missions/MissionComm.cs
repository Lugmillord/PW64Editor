using System.Buffers.Binary;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>The five score curves of a mission, in the order of the mission file.</summary>
public enum ScoreCurveKind
{
    /// <summary>Distance from the landing point → Landing Accuracy.</summary>
    LandingAccuracy = 0,

    /// <summary>Distance travelled (never shown on the score sheet, unused in the original game).</summary>
    Distance = 1,

    /// <summary>Highest altitude → Altitude Pts.</summary>
    Altitude = 2,

    /// <summary>Elapsed time → Time Pts.</summary>
    Time = 3,

    /// <summary>Landing impact (measured by the vehicle code) → Landing Impact.</summary>
    Impact = 4,
}

/// <summary>
/// The photo subjects with their own score curve in the six slots behind the five curves. The
/// game evaluates them when a photo is taken (snapPhoto in src/app/snap.c), with the timing of the
/// subject's animation as input. Only the photo missions of the Hang Glider fill them.
/// </summary>
public enum PhotoCurveKind
{
    Ferry = 0,
    Fountain = 1,
    Missile = 2,
    Flame = 3,
    Shuttle = 4,
    Whale = 5,
}

/// <summary>
/// The condition byte of a mission (COMM byte 4, read by taskGet_80346364): when time points count,
/// or whether a crash costs everything.
/// </summary>
public enum MissionCondition
{
    /// <summary>No condition.</summary>
    None = 0,

    /// <summary>A crash gives 0 points in total.</summary>
    CrashGivesZero = 1,

    /// <summary>Time points only if all objects are cleared.</summary>
    AllObjects = 2,

    /// <summary>Time points only if at least <see cref="MissionComm.RequiredObjects"/> objects are cleared.</summary>
    SomeObjects = 3,

    /// <summary>Time points only if the highest altitude reached <see cref="MissionComm.MinimumAltitude"/>.</summary>
    MinimumAltitude = 4,
}

/// <summary>
/// The object types of a mission, in the order of the counts in the COMM block. Each has a chunk
/// of the same name in the mission file.
/// </summary>
public enum MissionObjectKind
{
    Thermal = 0,        // THER
    LocalWind = 1,      // LWIN
    TakeoffPad = 2,     // TPAD
    LandingPad = 3,     // LPAD
    Runway = 4,         // LSTP
    Ring = 5,           // RNGS
    Balloon = 6,        // BALS
    Target = 7,         // TARG
    HoverPad = 8,       // HPAD
    BallTarget = 9,     // BTGT
    Photo = 10,         // PHTS
    MecaHawk = 11,     // FALC
    Formation = 12,     // SDFM
    CannonTarget = 13,  // CNTG
    HopperGoal = 14,    // HOPD
    Observer = 15,      // OBSV
}

/// <summary>
/// The COMM block of a mission file (UPWT): class, vehicle, test, island, condition, the score
/// curves and the other scoring values, and how many objects of each type the mission has.
/// </summary>
/// <remarks>
/// <para>Layout (TaskObjects.comm in src/app/task.h, all big endian):</para>
/// <code>
/// 0x000  u8   class (bonus games: level, Birdman: stage)
/// 0x001  u8   vehicle
/// 0x002  u8   test
/// 0x003  u8   island (0 Holiday, 1 Crescent, 2 Little States, 3 Ever-Frost)
/// 0x004  u8   condition (MissionCondition)
/// 0x048       scoring (Unk80345C80):
///   +0x000      5 score curves of 0x54 bytes (ScoreCurveKind)
///   +0x1A4      6 photo curves of 0x54 bytes (PhotoCurveKind)
///   +0x39C s16[4] ring points per ring type
///   +0x3A4 f32[4] extra ring value
///   +0x3B4 s32  deduction per hit
///   +0x3B8 s32  threshold for the pilot's good/bad reaction
///   +0x3BC f32  "Land now" time in seconds (0 = off)
///   +0x3C0 f32  hard end time in seconds (0 = off)
///   +0x3C4 f32  required object count (condition 3)
///   +0x3C8 f32  minimum altitude (condition 4)
/// 0x414  f32  Jumble Hopper: points taken for leaving the area (2 in the original missions)
/// 0x41C  u8[16] object counts (MissionObjectKind)
/// </code>
/// <para>
/// All other bytes are kept as they are, so an unchanged block is written back byte for byte.
/// </para>
/// </remarks>
public sealed class MissionComm
{
    /// <summary>Size of the COMM block in the retail mission files.</summary>
    public const int Size = 1072;

    /// <summary>Bytes the game reads (sizeof comm in TaskObjects); the rest is padding.</summary>
    public const int MinimumSize = 0x42C;

    /// <summary>Start of the scoring block.</summary>
    public const int ScoringOffset = 0x48;

    /// <summary>Offset of the photo curves behind the five curves, relative to the block.</summary>
    public const int PhotoCurvesOffset = ScoringOffset + 0x1A4;

    /// <summary>Number of photo curves.</summary>
    public const int PhotoCurveCount = 6;

    private const int RingPointsOffset = ScoringOffset + 0x39C;
    private const int RingExtraOffset = ScoringOffset + 0x3A4;
    private const int DeductionOffset = ScoringOffset + 0x3B4;
    private const int ReactionOffset = ScoringOffset + 0x3B8;
    private const int LandNowOffset = ScoringOffset + 0x3BC;
    private const int HardEndOffset = ScoringOffset + 0x3C0;
    private const int RequiredObjectsOffset = ScoringOffset + 0x3C4;
    private const int MinimumAltitudeOffset = ScoringOffset + 0x3C8;
    private const int OutOfAreaOffset = 0x414;
    private const int CountsOffset = 0x41C;

    /// <summary>Number of ring types (index of <see cref="RingPoints"/>; type 3 is the goal ring).</summary>
    public const int RingTypeCount = 4;

    private readonly byte[] _data;

    private MissionComm(byte[] data) => _data = data;

    /// <summary>Reads a COMM block (a copy is kept).</summary>
    /// <exception cref="InvalidDataException">The block is too short.</exception>
    public static MissionComm Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinimumSize)
        {
            throw new InvalidDataException($"The COMM block of a mission needs at least {MinimumSize} bytes, but has {data.Length}.");
        }

        var comm = new MissionComm(data.ToArray());

        // Reading every curve once checks that they are valid.
        foreach (ScoreCurveKind kind in Enum.GetValues<ScoreCurveKind>())
        {
            _ = comm.GetCurve(kind);
        }

        foreach (PhotoCurveKind kind in Enum.GetValues<PhotoCurveKind>())
        {
            _ = comm.GetPhotoCurve(kind);
        }

        return comm;
    }

    /// <summary>The block as stored in the mission file.</summary>
    public byte[] ToArray() => (byte[])_data.Clone();

    /// <summary>A copy that can be changed without changing this one.</summary>
    public MissionComm Clone() => new((byte[])_data.Clone());

    /// <summary>Class, vehicle and test of the mission.</summary>
    public TestId Id
    {
        get => new(_data[0], (Vehicle)_data[1], _data[2]);
        set
        {
            _data[0] = checked((byte)value.Class);
            _data[1] = checked((byte)value.Vehicle);
            _data[2] = checked((byte)value.Test);
        }
    }

    /// <summary>The vehicle number of a mission file the game does not load any more (a removed mission): no vehicle.</summary>
    public const byte NoVehicle = 0xFF;

    /// <summary>The island (0 Holiday, 1 Crescent, 2 Little States, 3 Ever-Frost).</summary>
    public int Island
    {
        get => _data[3];
        set => _data[3] = checked((byte)value);
    }

    /// <summary>The condition byte.</summary>
    public MissionCondition Condition
    {
        get => (MissionCondition)_data[4];
        set => _data[4] = checked((byte)value);
    }

    /// <summary>Time of day and weather (COMM byte 8, envGetCurrentId).</summary>
    public WeatherCondition Weather
    {
        get => (WeatherCondition)_data[8];
        set => _data[8] = checked((byte)value);
    }

    /// <summary>Snow (or the rain of the game module) falls (COMM byte 9 = 1).</summary>
    public bool Snow
    {
        get => _data[9] == 1;
        set => _data[9] = value ? (byte)1 : (byte)0;
    }

    /// <summary>The wind (units per second: x, y, z; COMM +0x10, env_802E14E8).</summary>
    public (float X, float Y, float Z) Wind
    {
        get => (ReadFloat(0x10), ReadFloat(0x14), ReadFloat(0x18));
        set
        {
            WriteFloat(0x10, value.X);
            WriteFloat(0x14, value.Y);
            WriteFloat(0x18, value.Z);
        }
    }

    /// <summary>How far the wind's strength may change (± units per second, COMM +0x1C).</summary>
    public float WindSpeedRange
    {
        get => ReadFloat(0x1C);
        set => WriteFloat(0x1C, value);
    }

    /// <summary>How fast the wind's strength changes (COMM +0x20; each frame a random step of up to half of it × the frame time).</summary>
    public float WindSpeedRate
    {
        get => ReadFloat(0x20);
        set => WriteFloat(0x20, value);
    }

    /// <summary>How far the wind's direction may change (± degrees, COMM +0x24).</summary>
    public float WindDirectionRange
    {
        get => ReadFloat(0x24);
        set => WriteFloat(0x24, value);
    }

    /// <summary>How fast the wind's direction changes (degrees, COMM +0x28; as <see cref="WindSpeedRate"/>).</summary>
    public float WindDirectionRate
    {
        get => ReadFloat(0x28);
        set => WriteFloat(0x28, value);
    }

    /// <summary>One of the five score curves.</summary>
    public ScoreCurve GetCurve(ScoreCurveKind kind) => ScoreCurve.Read(_data.AsSpan(CurveOffset((int)kind)));

    /// <summary>Replaces one of the five score curves.</summary>
    public void SetCurve(ScoreCurveKind kind, ScoreCurve curve) => curve.Write(_data.AsSpan(CurveOffset((int)kind), ScoreCurve.Size));

    /// <summary>The score curve of a photo subject.</summary>
    public ScoreCurve GetPhotoCurve(PhotoCurveKind kind) =>
        ScoreCurve.Read(_data.AsSpan(PhotoCurvesOffset + ((int)kind * ScoreCurve.Size)));

    /// <summary>Replaces the score curve of a photo subject.</summary>
    public void SetPhotoCurve(PhotoCurveKind kind, ScoreCurve curve) =>
        curve.Write(_data.AsSpan(PhotoCurvesOffset + ((int)kind * ScoreCurve.Size), ScoreCurve.Size));

    /// <summary>True if no photo subject gives points (all photo curves are "0 points").</summary>
    public bool PhotoCurvesAreEmpty => Enum.GetValues<PhotoCurveKind>().All(k => !GetPhotoCurve(k).GivesPoints);

    /// <summary>Jumble Hopper: points taken each time the hopper leaves the area (func_802FBEFC in code_82B90.c).</summary>
    public float OutOfAreaDeduction
    {
        get => ReadFloat(OutOfAreaOffset);
        set => WriteFloat(OutOfAreaOffset, value);
    }

    /// <summary>Points of a ring of the given type (0-3).</summary>
    public int GetRingPoints(int type) => BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(RingPointsOffset + (CheckRingType(type) * 2)));

    /// <summary>Sets the points of a ring type (0-3).</summary>
    public void SetRingPoints(int type, int points) =>
        BinaryPrimitives.WriteInt16BigEndian(_data.AsSpan(RingPointsOffset + (CheckRingType(type) * 2)), checked((short)points));

    /// <summary>The extra value of a ring type (rarely used; added to a separate counter of the results).</summary>
    public float GetRingExtra(int type) => BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(RingExtraOffset + (CheckRingType(type) * 4)));

    /// <summary>Points per hit (negative: a deduction). Gyrocopter −5, Rocket Belt −2, others 0.</summary>
    public int HitDeduction
    {
        get => BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(DeductionOffset));
        set => BinaryPrimitives.WriteInt32BigEndian(_data.AsSpan(DeductionOffset), value);
    }

    /// <summary>Above this score the pilot cheers at the end, otherwise he is disappointed (mostly 70).</summary>
    public int ReactionThreshold
    {
        get => BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(ReactionOffset));
        set => BinaryPrimitives.WriteInt32BigEndian(_data.AsSpan(ReactionOffset), value);
    }

    /// <summary>Seconds after which "Land now" is shown and the mission objects are removed (0 = off).</summary>
    public float LandNowTime
    {
        get => ReadFloat(LandNowOffset);
        set => WriteFloat(LandNowOffset, value);
    }

    /// <summary>Seconds after which the mission ends at once (0 = off; unused in the original game).</summary>
    public float HardEndTime
    {
        get => ReadFloat(HardEndOffset);
        set => WriteFloat(HardEndOffset, value);
    }

    /// <summary>Objects that must be cleared for time points (condition 3).</summary>
    public float RequiredObjects
    {
        get => ReadFloat(RequiredObjectsOffset);
        set => WriteFloat(RequiredObjectsOffset, value);
    }

    /// <summary>Altitude that must be reached for time points (condition 4).</summary>
    public float MinimumAltitude
    {
        get => ReadFloat(MinimumAltitudeOffset);
        set => WriteFloat(MinimumAltitudeOffset, value);
    }

    /// <summary>How many objects of a type the mission has (must match the size of its chunk).</summary>
    public int GetObjectCount(MissionObjectKind kind) => _data[CountsOffset + (int)kind];

    /// <summary>Changes the number of objects of a kind (the data chunk must match it).</summary>
    public void SetObjectCount(MissionObjectKind kind, int count) => _data[CountsOffset + (int)kind] = (byte)Math.Clamp(count, 0, 255);

    private static int CurveOffset(int index) => ScoringOffset + (index * ScoreCurve.Size);

    private static int CheckRingType(int type) =>
        type is >= 0 and < RingTypeCount ? type : throw new ArgumentOutOfRangeException(nameof(type), type, "Ring types are 0-3.");

    private float ReadFloat(int offset) => BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(offset));

    private void WriteFloat(int offset, float value) => BinaryPrimitives.WriteSingleBigEndian(_data.AsSpan(offset), value);
}
