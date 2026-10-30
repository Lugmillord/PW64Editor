using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using PW64Editor.Core.Localization;
using PW64Editor.Core.Missions;

namespace PW64Editor.Core.Map;

/// <summary>
/// One object of a mission (one record of a UPWT object chunk), with its position and rotation.
/// </summary>
public sealed class MapObject
{
    private readonly byte[] _data;

    /// <summary>Creates an object from its record.</summary>
    /// <exception cref="ArgumentException">The record has the wrong size.</exception>
    public MapObject(MissionObjectKind kind, int index, ReadOnlySpan<byte> record)
    {
        Type = MapObjectSchema.Of(kind);
        if (record.Length != Type.Size)
        {
            throw new ArgumentException($"A {kind} record has 0x{Type.Size:X} bytes.", nameof(record));
        }

        Index = index;
        _data = record.ToArray();
    }

    /// <summary>The object's type.</summary>
    public MapObjectType Type { get; }

    /// <summary>The object kind.</summary>
    public MissionObjectKind Kind => Type.Kind;

    /// <summary>The object's number among the objects of its kind in the mission (0 = first).</summary>
    public int Index { get; }

    /// <summary>The record as it is stored in the mission file.</summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>True if the object has a place in the world.</summary>
    public bool HasPosition => Type.Placement != MapPlacement.None;

    /// <summary>The shape of a local wind (0 sphere, 1 cylinder, 2 box).</summary>
    public int WindShape => Kind == MissionObjectKind.LocalWind ? _data[0x50] : -1;

    /// <summary>The object's position in game units (a runway: the middle of its centre line).</summary>
    public Vector3 Position => Type.Placement switch
    {
        MapPlacement.Runway => (Vector(0x00) + Vector(0x0C)) * 0.5f,
        MapPlacement.LocalWind => WindShape switch
        {
            2 => Vector(0x18),
            1 => (Vector(0x00) + Vector(0x0C)) * 0.5f,
            _ => Vector(0x00),
        },
        MapPlacement.None => Vector3.Zero,
        _ => Vector(Type.PositionOffset),
    };

    /// <summary>The game's angles in degrees (heading, pitch, roll); zero for objects without a rotation.</summary>
    public Vector3 Angles => Type.Placement switch
    {
        MapPlacement.LocalWind => WindShape == 2 ? Vector(0x24) : Vector3.Zero,
        _ => Type.RotationOffset >= 0 ? Vector(Type.RotationOffset) : Vector3.Zero,
    };

    /// <summary>Reads a byte of the record.</summary>
    public int Byte(int offset) => _data[offset];

    /// <summary>Reads an f32 of the record.</summary>
    public float Float(int offset) => BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(offset));

    /// <summary>Reads an s32 of the record.</summary>
    public int Int(int offset) => BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(offset));

    /// <summary>Reads three f32 of the record.</summary>
    public Vector3 Vector(int offset) => MapMath.ReadVector(_data.AsSpan(offset));

    /// <summary>The object numbers of an index list field (child rings, next pads).</summary>
    public IReadOnlyList<int> IndexList(MapField field)
    {
        int count = Math.Min((int)_data[field.CountOffset], 5);
        var list = new int[count];
        for (int i = 0; i < count; i++)
        {
            list[i] = Int(field.Offset + i * 4);
        }

        return list;
    }

    /// <summary>The name of the object in the object list, e.g. "Ring 3".</summary>
    public string DisplayName => $"{CoreText.T(Type.Name)} {Index + 1}";

    /// <summary>The value of a field as text, in the editor's units (game meters, degrees …).</summary>
    public string Format(MapField field)
    {
        CultureInfo culture = CoreText.Culture;
        string Number(float value) => ShownValue(value, field.Unit).ToString("0.###", culture) + UnitSuffix(field.Unit);
        switch (field.Type)
        {
            case MapFieldType.Float:
                return Number(Float(field.Offset));
            case MapFieldType.Int:
                return field.Unit == MapUnit.YesNo ? YesNo(Int(field.Offset)) : Int(field.Offset).ToString(culture) + UnitSuffix(field.Unit);
            case MapFieldType.Vector:
                Vector3 v = Vector(field.Offset);
                return string.Join(" / ", new[] { v.X, v.Y, v.Z }.Select(c => ShownValue(c, field.Unit).ToString("0.###", culture))) + UnitSuffix(field.Unit);
            case MapFieldType.Axis:
                char axis = (char)_data[field.Offset];
                return axis is 'x' or 'y' or 'z' ? axis.ToString() : CoreText.T("None");
            case MapFieldType.Name:
                int end = Array.IndexOf(_data, (byte)0, field.Offset, 16);
                return Encoding.ASCII.GetString(_data, field.Offset, (end < 0 ? field.Offset + 16 : end) - field.Offset);
            case MapFieldType.IndexList:
                IReadOnlyList<int> list = IndexList(field);
                return list.Count == 0 ? CoreText.T("None") : string.Join(", ", list.Select(i => (i + 1).ToString(culture)));
            default:
                int value = _data[field.Offset];
                if (field.Unit == MapUnit.YesNo)
                {
                    return YesNo(value);
                }

                if (field.Unit == MapUnit.Choice && field.Choices is { } choices)
                {
                    return value < choices.Count ? CoreText.T(choices[value]) : CoreText.F("Invalid ({0})", value);
                }

                return value.ToString(culture) + UnitSuffix(field.Unit);
        }
    }

    private static string YesNo(int value) => value != 0 ? CoreText.T("Yes") : CoreText.T("No");

    /// <summary>A stored value as the editor shows it (game units × 0.7 for meters).</summary>
    public static float ShownValue(float stored, MapUnit unit) => unit is MapUnit.Meters or MapUnit.MetersPerSecond
        ? stored * GameUnits.MetersPerUnit
        : unit == MapUnit.Fraction ? stored * 100f : stored;

    /// <summary>The unit's short name behind a value.</summary>
    public static string UnitSuffix(MapUnit unit) => unit switch
    {
        MapUnit.Meters => " m",
        MapUnit.Degrees => "°",
        MapUnit.Seconds => " s",
        MapUnit.Points => " " + CoreText.T("pts."),
        MapUnit.Fraction or MapUnit.Percent => " %",
        MapUnit.RadiansPerSecond => " rad/s",
        MapUnit.MetersPerSecond => " m/s",
        _ => string.Empty,
    };

    /// <summary>All objects of a mission with a place in the world, by kind and number.</summary>
    public static IReadOnlyList<MapObject> ReadAll(MissionFile mission)
    {
        var objects = new List<MapObject>();
        foreach (MapObjectType type in MapObjectSchema.Types)
        {
            if (type.Placement == MapPlacement.None)
            {
                continue;
            }

            ReadOnlySpan<byte> data = mission.GetObjectData(type.Kind);
            int count = Math.Min(mission.Comm.GetObjectCount(type.Kind), data.Length / type.Size);
            for (int i = 0; i < count; i++)
            {
                objects.Add(new MapObject(type.Kind, i, data.Slice(i * type.Size, type.Size)));
            }
        }

        return objects;
    }
}
