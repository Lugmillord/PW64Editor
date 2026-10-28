using System.Buffers.Binary;
using PW64Editor.Core.Localization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>
/// The objects a photo can show as its subject, with the numbers the game uses (unk43 of a photo in snap.c). 1–6 are the
/// game's photo subjects (each with a score curve in the mission file), 7–10 the moving objects the game only records in
/// a photo; the game module lets every type give points (stage 8).
/// </summary>
public enum PhotoSubjectKind
{
    Shuttle = 1,
    Ferry = 2,
    Missi = 3,
    Whale = 4,
    Fountain = 5,
    Flame = 6,
    WhalePod = 7,
    Plane = 8,
    GliderToy = 9,
    Boat = 10,
}

/// <summary>
/// A photo subject of a mission (stage 8): what has to be in the picture and what a photo of it gives. The best photo of
/// each subject counts: points × centring × curve(distance).
/// </summary>
public sealed class PhotoSubject
{
    /// <summary>The object type.</summary>
    public PhotoSubjectKind Kind { get; set; }

    /// <summary>Only this object of the type (0 = the first), or null for any of them.</summary>
    public int? Instance { get; set; }

    /// <summary>The most points a photo of the subject gives.</summary>
    public int Points { get; set; }

    /// <summary>The direction the camera has to look into (degrees, the game's heading), or null for any direction.</summary>
    public float? Direction { get; set; }

    /// <summary>With <see cref="Direction"/>: the whole width of the directions that count (degrees; ± half of it).</summary>
    public float Tolerance { get; set; } = 90;

    /// <summary>The distance between camera and subject (game units) to the share of the points (percent, 0–100).</summary>
    public List<CurvePoint> Curve { get; set; } = [];

    /// <summary>Where the camera of the subject's sample photo is, or null for the editor's choice (<see cref="PhotoSubjects.DefaultSample"/>).</summary>
    public PhotoSample? Sample { get; set; }

    /// <summary>A copy.</summary>
    public PhotoSubject Clone() => new()
    {
        Kind = Kind,
        Instance = Instance,
        Points = Points,
        Direction = Direction,
        Tolerance = Tolerance,
        Curve = [.. Curve],
        Sample = Sample,
    };

    /// <summary>True if both describe the same subject.</summary>
    public bool SameAs(PhotoSubject? other) =>
        other is not null && Kind == other.Kind && Instance == other.Instance && Points == other.Points && Direction == other.Direction
        && (Direction is null || Tolerance == other.Tolerance) && Curve.SequenceEqual(other.Curve) && Sample == other.Sample;
}

/// <summary>
/// The camera of a sample photo (Step 49), relative to the subject's object as it is at the start of the mission: the
/// camera looks at it from this distance, in this direction (the camera's heading, as <see cref="PhotoSubject.Direction"/>)
/// and from this height angle (degrees above the object; the camera looks down by as much).
/// </summary>
/// <param name="Distance">Game units.</param>
/// <param name="Heading">Degrees, the game's heading.</param>
/// <param name="Elevation">Degrees above the horizontal.</param>
public sealed record PhotoSample(float Distance, float Heading, float Elevation);

/// <summary>
/// The photo subjects of the missions (stage 8): which objects each island has, the game's subjects of a mission (its
/// PHTS entries with the score curves), and the table the game module reads (subjects.c).
/// </summary>
/// <remarks>
/// <para><b>The game</b> (snapPhoto, func_80338A14): of the six main subjects the nearest visible one becomes the photo's
/// subject; a PHTS entry of that type gives PHTS points × centring × the type's curve in the COMM block (distance
/// camera–subject). The original missions split the points like √points × √points (Mission_Scoring.md, Level_Editor_Prep.md
/// section 6.1).</para>
/// <para><b>The editor</b> stores a mission's own subjects in <see cref="MissionSettings.PhotoSubjects"/>: up to
/// <see cref="Max"/>, each with points, an optional single object and direction, and a curve in percent. The build writes
/// them into the photo table; for these missions the module takes the photos (module_snap_photo). Missions without own
/// subjects keep the game's code and their PHTS entries.</para>
/// <para><b>Table</b> (ROM: <see cref="RomOffset"/> bytes behind the data area's image, RAM: <see cref="Address"/>, its size in
/// the data area's header +0x34): 'HHPS', u32 number of missions, then per mission u8 vehicle, class, test, number of
/// subjects and <see cref="EntrySize"/> bytes per subject: u8 type, u8 object (0xFF = any), u8 1 = with direction, u8 0,
/// f32 direction, f32 tolerance (whole width), f32 points, the curve (0x54 bytes, distance → percent), f32 distance, heading and height angle of the sample photo's
/// camera.</para>
/// <para>Names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static class PhotoSubjects
{
    /// <summary>Subjects per mission.</summary>
    public const int Max = 10;

    /// <summary>Magic number of the table ('HHPS').</summary>
    public const uint Magic = 0x48485053;

    /// <summary>Where the module keeps the table (high data area).</summary>
    public const uint Address = 0x807F1000;

    /// <summary>Room for the table.</summary>
    public const int Room = 0x3000;

    /// <summary>Where the table lies in the ROM, behind the start of the data area's image.</summary>
    public const int RomOffset = 0xD000;

    /// <summary>Bytes per subject in the table.</summary>
    public const int EntrySize = 0x74;

    /// <summary>The points of the game's PHTS entries (+0x10) of a type and its curve give at most these points together.</summary>
    private const int PhtsSize = 0x14;

    /// <summary>The name of a type in the editor's language.</summary>
    public static string Name(PhotoSubjectKind kind) => kind switch
    {
        PhotoSubjectKind.Shuttle => CoreText.T("Space shuttle"),
        PhotoSubjectKind.Ferry => CoreText.T("Ferry"),
        PhotoSubjectKind.Missi => CoreText.T("Missi the lake monster"),
        PhotoSubjectKind.Whale => CoreText.T("Whale"),
        PhotoSubjectKind.Fountain => CoreText.T("Fountain"),
        PhotoSubjectKind.Flame => CoreText.T("Flame of the oil plant"),
        PhotoSubjectKind.WhalePod => CoreText.T("Whale pod"),
        PhotoSubjectKind.Plane => CoreText.T("Plane"),
        PhotoSubjectKind.GliderToy => CoreText.T("Model glider"),
        PhotoSubjectKind.Boat => CoreText.T("Boat"),
        _ => kind.ToString(),
    };

    /// <summary>How many objects of a type an island has (0 = none; level.c loads them per island).</summary>
    /// <param name="kind">The type.</param>
    /// <param name="island">0 Holiday, 1 Crescent, 2 Little States, 3 Ever-Frost.</param>
    public static int Count(PhotoSubjectKind kind, int island) => (kind, island) switch
    {
        (PhotoSubjectKind.Whale or PhotoSubjectKind.Fountain, 1) => 1,
        (PhotoSubjectKind.Shuttle or PhotoSubjectKind.Ferry or PhotoSubjectKind.Missi, 2) => 1,
        (PhotoSubjectKind.Flame, 3) => 1,
        (PhotoSubjectKind.WhalePod, 3) => 5,
        (PhotoSubjectKind.Plane, 2) => 2,
        (PhotoSubjectKind.GliderToy, 2) => 4,
        (PhotoSubjectKind.GliderToy, 0 or 1 or 3) => 3,
        (PhotoSubjectKind.Boat, 0 or 1) => 2,
        _ => 0,
    };

    /// <summary>The types an island has.</summary>
    public static IReadOnlyList<PhotoSubjectKind> OnIsland(int island) =>
        Enum.GetValues<PhotoSubjectKind>().Where(k => Count(k, island) > 0).ToList();

    /// <summary>True for the six types with a score curve in the mission file (the game's photo subjects).</summary>
    public static bool IsGameSubject(PhotoSubjectKind kind) => kind <= PhotoSubjectKind.Flame;

    /// <summary>The COMM curve of one of the six game subjects.</summary>
    public static PhotoCurveKind CurveOf(PhotoSubjectKind kind) => kind switch
    {
        PhotoSubjectKind.Ferry => PhotoCurveKind.Ferry,
        PhotoSubjectKind.Fountain => PhotoCurveKind.Fountain,
        PhotoSubjectKind.Missi => PhotoCurveKind.Missile,
        PhotoSubjectKind.Flame => PhotoCurveKind.Flame,
        PhotoSubjectKind.Shuttle => PhotoCurveKind.Shuttle,
        PhotoSubjectKind.Whale => PhotoCurveKind.Whale,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The subjects the game scores in a mission: its PHTS entries of the six game subjects (of several entries of a type
    /// the last one, as the game), each with the points PHTS points × the curve's maximum and its curve in percent.
    /// </summary>
    public static List<PhotoSubject> FromGame(MissionFile file)
    {
        ReadOnlySpan<byte> data = file.GetObjectData(MissionObjectKind.Photo);
        var result = new List<PhotoSubject>();
        for (int offset = 0; offset + PhtsSize <= data.Length; offset += PhtsSize)
        {
            int type = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
            if (type < 1 || type > (int)PhotoSubjectKind.Flame)
            {
                continue;
            }

            var kind = (PhotoSubjectKind)type;
            ScoreCurve curve = file.Comm.GetPhotoCurve(CurveOf(kind));
            float max = curve.Maximum;
            float points = BinaryPrimitives.ReadSingleBigEndian(data[(offset + 0x10)..]);
            bool direction = BinaryPrimitives.ReadInt32BigEndian(data[(offset + 4)..]) == 1;
            result.RemoveAll(s => s.Kind == kind);
            result.Add(new PhotoSubject
            {
                Kind = kind,
                Points = max > 0 ? (int)MathF.Round(points * max) : 0,
                Direction = direction ? BinaryPrimitives.ReadSingleBigEndian(data[(offset + 8)..]) : null,
                Tolerance = direction ? BinaryPrimitives.ReadSingleBigEndian(data[(offset + 0x0C)..]) : 90,
                Curve = curve.Points.Select(p => new CurvePoint(p.X, max > 0 ? MathF.Round(p.Y / max * 1000) / 10 : 0)).ToList(),
            });
        }

        return result;
    }

    /// <summary>The subjects of a mission: its own (settings) or the game's.</summary>
    public static IReadOnlyList<PhotoSubject> Effective(MissionSettings? settings, MissionFile file) =>
        settings?.PhotoSubjects ?? (IReadOnlyList<PhotoSubject>)FromGame(file);

    /// <summary>The most points the photos of a mission give (the best photo of each subject).</summary>
    public static int MaxPoints(IReadOnlyList<PhotoSubject> subjects) => subjects.Sum(s => Math.Max(0, s.Points));

    /// <summary>
    /// A new subject: full points from 0.6 to 1.6 times the distance of its sample photo (<see cref="SampleOf"/>), none
    /// closer than a quarter of it or farther than four times it.
    /// </summary>
    public static PhotoSubject Default(PhotoSubjectKind kind, int points)
    {
        float d = SampleOf(kind).Distance;
        return new PhotoSubject
        {
            Kind = kind,
            Points = points,
            Curve =
            [
                new CurvePoint(MathF.Round(d * 0.25f), 0),
                new CurvePoint(MathF.Round(d * 0.6f), 100),
                new CurvePoint(MathF.Round(d * 1.6f), 100),
                new CurvePoint(MathF.Round(d * 4), 0),
            ],
        };
    }

    /// <summary>
    /// The sample photo's camera of a type (game units, degrees): for the game's six subjects the game's sample photos
    /// (snapInit: whale 105 units from 145°, 25° high; fountain 85, 90°, 20°; Missi 70, 270°, 15°; ferry 230, 145°, 15°;
    /// shuttle 250, 180°, 0°; flame 120, 0°, 0°), for the others close enough that the object fills the picture.
    /// </summary>
    public static PhotoSample SampleOf(PhotoSubjectKind kind) => kind switch
    {
        PhotoSubjectKind.Whale => new PhotoSample(105, 145, 25),
        PhotoSubjectKind.Fountain => new PhotoSample(85, 90, 20),
        PhotoSubjectKind.Missi => new PhotoSample(70, 270, 15),
        PhotoSubjectKind.Ferry => new PhotoSample(230, 145, 15),
        PhotoSubjectKind.Shuttle => new PhotoSample(250, 180, 0),
        PhotoSubjectKind.Flame => new PhotoSample(120, 0, 0),
        PhotoSubjectKind.WhalePod => new PhotoSample(PodDistance, 0, 15),
        PhotoSubjectKind.Plane => new PhotoSample(PlaneDistance, 0, 15),
        PhotoSubjectKind.GliderToy => new PhotoSample(GliderDistance, 0, 15),
        _ => new PhotoSample(BoatDistance, 0, 15),
    };

    private const float PodDistance = 110, PlaneDistance = 55, GliderDistance = 18, BoatDistance = 45;

    /// <summary>
    /// The camera of a subject's sample photo when none is set: the type's (<see cref="SampleOf"/>) when that distance
    /// gives full points, otherwise the middle of the distances with full points; the subject's direction if it has one.
    /// </summary>
    public static PhotoSample DefaultSample(PhotoSubject subject)
    {
        PhotoSample type = SampleOf(subject.Kind);
        float best = subject.Curve.Count == 0 ? 0 : subject.Curve.Max(p => p.Y);
        float distance;
        if (subject.Curve.Count > 0 && ScoreCurve.Create(subject.Curve).Evaluate(type.Distance) >= best)
        {
            distance = type.Distance;
        }
        else
        {
            List<CurvePoint> full = subject.Curve.Where(p => p.Y >= best).ToList();
            distance = full.Count == 0 ? type.Distance : (full[0].X + full[^1].X) / 2;
        }

        return new PhotoSample(distance, subject.Direction ?? type.Heading, type.Elevation);
    }

    /// <summary>What is wrong with the subjects of a mission on an island, or null.</summary>
    public static string? Problem(IReadOnlyList<PhotoSubject> subjects, int island)
    {
        if (subjects.Count > Max)
        {
            return CoreText.F("A mission has at most {0} photo subjects.", Max);
        }

        foreach (PhotoSubject s in subjects)
        {
            int count = Count(s.Kind, island);
            if (!Enum.IsDefined(s.Kind) || count == 0)
            {
                return CoreText.F("{0}: the island of the mission has no such object.", Name(s.Kind));
            }

            if (s.Instance is { } instance && (instance < 0 || instance >= count))
            {
                return CoreText.F("{0}: the island has {1} of them.", Name(s.Kind), count);
            }

            if (s.Points is < 0 or > 126)
            {
                return CoreText.T("Points are whole numbers from 0 to 126 (the game stores at most 126 per test).");
            }

            if (s.Direction is not null && (s.Tolerance <= 0 || s.Tolerance > 360))
            {
                return CoreText.T("The range of directions is more than 0 and at most 360 degrees.");
            }

            if (s.Sample is { } sample && (sample.Distance is <= 0 or > 2000 || sample.Elevation is < -89 or > 89
                || !float.IsFinite(sample.Heading)))
            {
                return CoreText.T("The sample photo's camera needs a distance of more than 0 and up to 1400 m and a height angle from -89 to 89 degrees.");
            }

            if (s.Curve.Count > ScoreCurve.MaxPoints || s.Curve.Any(p => p.Y is < 0 or > 100 || p.X < 0 || !float.IsFinite(p.X))
                || s.Curve.Zip(s.Curve.Skip(1)).Any(p => p.Second.X < p.First.X))
            {
                return CoreText.F("The distance curve needs up to {0} points with rising distances and 0 to 100 %.", ScoreCurve.MaxPoints);
            }
        }

        return null;
    }

    /// <summary>The subjects with their points scaled to a new sum (rounded; the last one takes the rest).</summary>
    public static List<PhotoSubject> WithPoints(IReadOnlyList<PhotoSubject> subjects, int points)
    {
        List<PhotoSubject> result = subjects.Select(s => s.Clone()).ToList();
        int old = MaxPoints(subjects);
        if (result.Count == 0)
        {
            return result;
        }

        int given = 0;
        for (int i = 0; i < result.Count; i++)
        {
            result[i].Points = i == result.Count - 1
                ? Math.Max(0, points - given)
                : old > 0 ? (int)Math.Round((double)subjects[i].Points * points / old) : points / result.Count;
            given += result[i].Points;
        }

        return result;
    }

    /// <summary>The table for the game module (empty: no mission has own subjects; a mission with an empty list gives no photo points).</summary>
    /// <exception cref="InvalidDataException">The table does not fit into its room.</exception>
    public static byte[] CreateTable(IEnumerable<(TestId Id, IReadOnlyList<PhotoSubject> Subjects)> missions)
    {
        var list = missions.ToList();
        if (list.Count == 0)
        {
            return [];
        }

        int size = 8 + list.Sum(m => 4 + (m.Subjects.Count * EntrySize));
        size = (size + 7) & ~7;
        if (size > Room)
        {
            throw new InvalidDataException(CoreText.F("The photo subjects of the missions need {0} bytes, the game module has room for {1}.", size, Room));
        }

        byte[] table = new byte[size];
        BinaryPrimitives.WriteUInt32BigEndian(table, Magic);
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(4), (uint)list.Count);
        int offset = 8;
        foreach ((TestId id, IReadOnlyList<PhotoSubject> subjects) in list)
        {
            if (subjects.Count > Max)
            {
                throw new InvalidDataException(CoreText.F("A mission has at most {0} photo subjects.", Max));
            }

            table[offset] = (byte)id.Vehicle;
            table[offset + 1] = (byte)id.Class;
            table[offset + 2] = (byte)id.Test;
            table[offset + 3] = (byte)subjects.Count;
            offset += 4;
            foreach (PhotoSubject s in subjects)
            {
                Span<byte> e = table.AsSpan(offset, EntrySize);
                e[0] = (byte)s.Kind;
                e[1] = s.Instance is { } instance ? (byte)instance : (byte)0xFF;
                e[2] = s.Direction is null ? (byte)0 : (byte)1;
                BinaryPrimitives.WriteSingleBigEndian(e[4..], s.Direction ?? 0);
                BinaryPrimitives.WriteSingleBigEndian(e[8..], s.Direction is null ? 0 : s.Tolerance);
                BinaryPrimitives.WriteSingleBigEndian(e[0x0C..], s.Points);
                ScoreCurve.Create(s.Curve).Write(e.Slice(0x10, ScoreCurve.Size));
                PhotoSample sample = s.Sample ?? DefaultSample(s);
                BinaryPrimitives.WriteSingleBigEndian(e[0x64..], sample.Distance);
                BinaryPrimitives.WriteSingleBigEndian(e[0x68..], sample.Heading);
                BinaryPrimitives.WriteSingleBigEndian(e[0x6C..], sample.Elevation);
                offset += EntrySize;
            }
        }

        return table;
    }

    /// <summary>The missions with own subjects in a table of a built ROM (empty if it is none).</summary>
    public static List<(TestId Id, List<PhotoSubject> Subjects)> ReadTable(ReadOnlySpan<byte> table)
    {
        var result = new List<(TestId, List<PhotoSubject>)>();
        if (table.Length < 8 || BinaryPrimitives.ReadUInt32BigEndian(table) != Magic)
        {
            return result;
        }

        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(table[4..]);
        int offset = 8;
        for (int m = 0; m < count && offset + 4 <= table.Length; m++)
        {
            var id = new TestId(table[offset + 1], (Vehicle)table[offset], table[offset + 2]);
            int n = table[offset + 3];
            offset += 4;
            if (n > Max || offset + (n * EntrySize) > table.Length)
            {
                break;
            }

            var subjects = new List<PhotoSubject>();
            for (int i = 0; i < n; i++)
            {
                ReadOnlySpan<byte> e = table.Slice(offset, EntrySize);
                bool direction = e[2] == 1;
                subjects.Add(new PhotoSubject
                {
                    Kind = (PhotoSubjectKind)e[0],
                    Instance = e[1] == 0xFF ? null : e[1],
                    Direction = direction ? BinaryPrimitives.ReadSingleBigEndian(e[4..]) : null,
                    Tolerance = direction ? BinaryPrimitives.ReadSingleBigEndian(e[8..]) : 90,
                    Points = (int)MathF.Round(BinaryPrimitives.ReadSingleBigEndian(e[0x0C..])),
                    Curve = [.. ScoreCurve.Read(e.Slice(0x10, ScoreCurve.Size)).Points],
                });
                var sample = new PhotoSample(BinaryPrimitives.ReadSingleBigEndian(e[0x64..]), BinaryPrimitives.ReadSingleBigEndian(e[0x68..]),
                    BinaryPrimitives.ReadSingleBigEndian(e[0x6C..]));
                subjects[^1].Sample = sample == DefaultSample(subjects[^1]) ? null : sample;
                offset += EntrySize;
            }

            result.Add((id, subjects));
        }

        return result;
    }
}
