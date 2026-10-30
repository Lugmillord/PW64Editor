using PW64Editor.Core.Localization;
using PW64Editor.Core.Missions;

namespace PW64Editor.Core.Map;

/// <summary>How a field of a mission object is stored.</summary>
public enum MapFieldType
{
    /// <summary>A big-endian f32.</summary>
    Float,

    /// <summary>A byte.</summary>
    Byte,

    /// <summary>A big-endian s32.</summary>
    Int,

    /// <summary>Three big-endian f32.</summary>
    Vector,

    /// <summary>A byte holding a letter ('x', 'y', 'z', 'n').</summary>
    Axis,

    /// <summary>A zero-terminated name (16 bytes).</summary>
    Name,

    /// <summary>Up to five s32 object numbers; their count is the byte at <see cref="MapField.CountOffset"/>.</summary>
    IndexList,
}

/// <summary>What a value means, for showing it (game units become game meters, × 0.7).</summary>
public enum MapUnit
{
    /// <summary>A plain number.</summary>
    None,

    /// <summary>A distance or position in game units, shown in game meters.</summary>
    Meters,

    /// <summary>Degrees.</summary>
    Degrees,

    /// <summary>Seconds.</summary>
    Seconds,

    /// <summary>Points.</summary>
    Points,

    /// <summary>A share of 0 to 1.</summary>
    Fraction,

    /// <summary>Radians per second.</summary>
    RadiansPerSecond,

    /// <summary>A speed in game units per second, shown in game meters per second.</summary>
    MetersPerSecond,

    /// <summary>A percentage.</summary>
    Percent,

    /// <summary>A yes/no byte (0 = no).</summary>
    YesNo,

    /// <summary>A number from a list of choices (<see cref="MapField.Choices"/>).</summary>
    Choice,
}

/// <summary>One field of a mission object record.</summary>
/// <param name="Name">The English name (translated through <see cref="CoreText"/>).</param>
/// <param name="Offset">Offset in the record.</param>
/// <param name="Type">How it is stored.</param>
/// <param name="Unit">What it means.</param>
/// <param name="CountOffset">For <see cref="MapFieldType.IndexList"/>: the offset of the count byte.</param>
/// <param name="Choices">For <see cref="MapUnit.Choice"/>: the English names of the values 0, 1, 2 …</param>
public sealed record MapField(string Name, int Offset, MapFieldType Type, MapUnit Unit = MapUnit.None, int CountOffset = -1,
    IReadOnlyList<string>? Choices = null);

/// <summary>Where an object keeps its position and rotation.</summary>
public enum MapPlacement
{
    /// <summary>Position at <see cref="MapObjectType.PositionOffset"/>, rotation (if any) at <see cref="MapObjectType.RotationOffset"/>.</summary>
    Plain,

    /// <summary>A runway: two end points of its centre line; the position is their middle.</summary>
    Runway,

    /// <summary>A local wind: the shape byte decides (sphere at 0x00, cylinder between 0x00 and 0x0C, box at 0x18 turned by 0x24).</summary>
    LocalWind,

    /// <summary>No place in the world (photo subjects, formations).</summary>
    None,
}

/// <summary>A type of mission object: its record and its fields.</summary>
/// <param name="Kind">The object type.</param>
/// <param name="Name">The English name of one object (translated through <see cref="CoreText"/>).</param>
/// <param name="PluralName">The English name of the group in the object list.</param>
/// <param name="Size">The record size.</param>
/// <param name="Placement">Where position and rotation are.</param>
/// <param name="PositionOffset">The offset of the position (plain placement).</param>
/// <param name="RotationOffset">The offset of the angles (heading, pitch, roll), or −1 if the object has no rotation.</param>
/// <param name="Fields">The fields in record order.</param>
public sealed record MapObjectType(MissionObjectKind Kind, string Name, string PluralName, int Size, MapPlacement Placement,
    int PositionOffset, int RotationOffset, IReadOnlyList<MapField> Fields)
{
    /// <summary>True if the object has its own rotation.</summary>
    public bool CanRotate => RotationOffset >= 0 || Placement == MapPlacement.LocalWind;
}

/// <summary>
/// The records of the mission objects (UPWT chunks) with the meaning of their fields.
/// </summary>
/// <remarks>
/// <para>Worked out from the code that reads each record (task.c, rings.c, hover_pads.c, balls.c, pads.c, targets.c,
/// falco.c, wind.c, thermals.c, ball_target.c, code_82520.c, camera.c of the decompilation,
/// https://github.com/gcsmith/Pilotwings64Decomp, MIT license) and checked against the 61 retail missions; see the
/// project document Map_Editor.md, section 2.</para>
/// </remarks>
public static class MapObjectSchema
{
    private static MapField F(string name, int offset, MapUnit unit = MapUnit.None) => new(name, offset, MapFieldType.Float, unit);

    private static MapField B(string name, int offset, MapUnit unit = MapUnit.None) => new(name, offset, MapFieldType.Byte, unit);

    private static MapField I(string name, int offset, MapUnit unit = MapUnit.None) => new(name, offset, MapFieldType.Int, unit);

    private static MapField V(string name, int offset, MapUnit unit) => new(name, offset, MapFieldType.Vector, unit);

    private static MapField C(string name, int offset, params string[] choices) => new(name, offset, MapFieldType.Byte, MapUnit.Choice, Choices: choices);

    private static MapField L(string name, int offset, int countOffset) => new(name, offset, MapFieldType.IndexList, CountOffset: countOffset);

    private static MapField Axis(string name, int offset) => new(name, offset, MapFieldType.Axis);

    /// <summary>All types, by <see cref="MissionObjectKind"/>.</summary>
    public static IReadOnlyList<MapObjectType> Types { get; } =
    [
        new(MissionObjectKind.Thermal, CoreText.K("Thermal"), CoreText.K("Thermals"), 0x28, MapPlacement.Plain, 0x00, -1,
        [
            V(CoreText.K("Base (centre at the bottom)"), 0x00, MapUnit.Meters),
            F(CoreText.K("Radius"), 0x0C, MapUnit.Meters),
            F(CoreText.K("Height"), 0x10, MapUnit.Meters),
            I(CoreText.K("Area (sound)"), 0x14),
            F(CoreText.K("Height of the lift ramp at the bottom"), 0x18, MapUnit.Meters),
            F(CoreText.K("Height where the lift starts to fade"), 0x1C, MapUnit.Meters),
            F(CoreText.K("Swirl"), 0x20, MapUnit.RadiansPerSecond),
            F(CoreText.K("Lift in the centre"), 0x24),
        ]),
        new(MissionObjectKind.LocalWind, CoreText.K("Local wind"), CoreText.K("Local winds"), 0x54, MapPlacement.LocalWind, 0x00, 0x24,
        [
            V(CoreText.K("Centre of the sphere / end A of the cylinder"), 0x00, MapUnit.Meters),
            V(CoreText.K("End B of the cylinder"), 0x0C, MapUnit.Meters),
            V(CoreText.K("Centre of the box"), 0x18, MapUnit.Meters),
            V(CoreText.K("Rotation of the box"), 0x24, MapUnit.Degrees),
            V(CoreText.K("Size of the box"), 0x30, MapUnit.Meters),
            I(CoreText.K("Area (sound, 10 = all)"), 0x3C),
            V(CoreText.K("Wind"), 0x40, MapUnit.MetersPerSecond),
            F(CoreText.K("Radius (sphere, cylinder)"), 0x4C, MapUnit.Meters),
            C(CoreText.K("Shape"), 0x50, CoreText.K("Sphere"), CoreText.K("Cylinder"), CoreText.K("Box")),
            C(CoreText.K("Falloff"), 0x51, CoreText.K("None"), CoreText.K("Linear"), CoreText.K("Quadratic"), CoreText.K("Flat, then linear")),
        ]),
        new(MissionObjectKind.TakeoffPad, CoreText.K("Start"), CoreText.K("Start"), 0x30, MapPlacement.Plain, 0x00, 0x0C,
        [
            V(CoreText.K("Position"), 0x00, MapUnit.Meters),
            V(CoreText.K("Rotation"), 0x0C, MapUnit.Degrees),
            V(CoreText.K("Start speed (sideways, forward, up)"), 0x1C, MapUnit.MetersPerSecond),
            C(CoreText.K("Start"), 0x28, CoreText.K("On the ground"), CoreText.K("In the air")),
            F(CoreText.K("Start fuel"), 0x2C, MapUnit.Fraction),
        ]),
        new(MissionObjectKind.LandingPad, CoreText.K("Landing pad"), CoreText.K("Landing pads"), 0x30, MapPlacement.Plain, 0x00, -1,
        [
            V(CoreText.K("Position (snaps to an island pad within 100 units)"), 0x00, MapUnit.Meters),
            C(CoreText.K("Target circle"), 0x2C, CoreText.K("Small"), CoreText.K("Middle"), CoreText.K("Large")),
        ]),
        new(MissionObjectKind.Runway, CoreText.K("Runway"), CoreText.K("Runways"), 0x24, MapPlacement.Runway, 0x00, -1,
        [
            V(CoreText.K("End A of the centre line"), 0x00, MapUnit.Meters),
            V(CoreText.K("End B of the centre line"), 0x0C, MapUnit.Meters),
            B(CoreText.K("Landing checked"), 0x1C, MapUnit.YesNo),
            F(CoreText.K("Width"), 0x20, MapUnit.Meters),
        ]),
        new(MissionObjectKind.Ring, CoreText.K("Ring"), CoreText.K("Rings"), 0x84, MapPlacement.Plain, 0x00, 0x0C,
        [
            V(CoreText.K("Position"), 0x00, MapUnit.Meters),
            V(CoreText.K("Rotation"), 0x0C, MapUnit.Degrees),
            I(CoreText.K("Area"), 0x18),
            L(CoreText.K("Child rings"), 0x20, 0x1D),
            L(CoreText.K("Timed child rings"), 0x38, 0x34),
            B(CoreText.K("Points while its timer runs"), 0x4C, MapUnit.Points),
            B(CoreText.K("Can be a timed ring"), 0x4D, MapUnit.YesNo),
            F(CoreText.K("Timer"), 0x50, MapUnit.Seconds),
            C(CoreText.K("Size (radius)"), 0x54, "5.25 m", "7 m", "8.75 m", "12.25 m", "17.5 m"),
            B(CoreText.K("Active at the start"), 0x55, MapUnit.YesNo),
            F(CoreText.K("Movement speed"), 0x58, MapUnit.RadiansPerSecond),
            F(CoreText.K("Movement radius"), 0x5C, MapUnit.Meters),
            Axis(CoreText.K("Movement axis"), 0x60),
            F(CoreText.K("Spin before a timer"), 0x64, MapUnit.RadiansPerSecond),
            F(CoreText.K("Spin while the timer runs"), 0x68, MapUnit.RadiansPerSecond),
            F(CoreText.K("Spin after the timer"), 0x6C, MapUnit.RadiansPerSecond),
            Axis(CoreText.K("Spin axis"), 0x70),
            C(CoreText.K("Direction"), 0x71, CoreText.K("One way only"), CoreText.K("Both ways")),
            C(CoreText.K("Ring type"), 0x72, CoreText.K("Normal"), CoreText.K("Bonus"), CoreText.K("Invisible check ring"), CoreText.K("Goal ring")),
            new(CoreText.K("Name"), 0x74, MapFieldType.Name),
        ]),
        new(MissionObjectKind.Balloon, CoreText.K("Balloon"), CoreText.K("Balloons and balls"), 0x68, MapPlacement.Plain, 0x00, -1,
        [
            V(CoreText.K("Position"), 0x00, MapUnit.Meters),
            I(CoreText.K("Area"), 0x18),
            F(CoreText.K("Sideways speed of the parts"), 0x1C, MapUnit.MetersPerSecond),
            C(CoreText.K("Colour"), 0x20, CoreText.K("Orange"), CoreText.K("Green"), CoreText.K("Blue")),
            B(CoreText.K("Splits into 5"), 0x21, MapUnit.YesNo),
            F(CoreText.K("Pop speed"), 0x24, MapUnit.MetersPerSecond),
            F(CoreText.K("Bounce (0 = does not move)"), 0x28),
            F(CoreText.K("Mass"), 0x2C),
            F(CoreText.K("Scale"), 0x30),
            I(CoreText.K("Points"), 0x34, MapUnit.Points),
            F(CoreText.K("Drag"), 0x3C),
            F(CoreText.K("Gravity (0 = 9.8)"), 0x40),
            C(CoreText.K("Parts: colour"), 0x44, CoreText.K("Orange"), CoreText.K("Green"), CoreText.K("Blue")),
            F(CoreText.K("Parts: pop speed"), 0x48, MapUnit.MetersPerSecond),
            F(CoreText.K("Parts: bounce"), 0x4C),
            F(CoreText.K("Parts: mass"), 0x50),
            I(CoreText.K("Parts: points"), 0x54, MapUnit.Points),
            F(CoreText.K("Parts: scale"), 0x5C),
            F(CoreText.K("Parts: drag"), 0x60),
            F(CoreText.K("Parts: gravity"), 0x64),
        ]),
        new(MissionObjectKind.Target, CoreText.K("Target"), CoreText.K("Targets"), 0x20, MapPlacement.Plain, 0x00, 0x0C,
        [
            V(CoreText.K("Position"), 0x00, MapUnit.Meters),
            V(CoreText.K("Rotation"), 0x0C, MapUnit.Degrees),
            C(CoreText.K("Type"), 0x18, CoreText.K("Ball"), CoreText.K("Disc"), CoreText.K("Billboard")),
            B(CoreText.K("Points"), 0x19, MapUnit.Points),
        ]),
        new(MissionObjectKind.HoverPad, CoreText.K("Floating Pad"), CoreText.K("Floating Pads"), 0x40, MapPlacement.Plain, 0x00, 0x0C,
        [
            V(CoreText.K("Position"), 0x00, MapUnit.Meters),
            V(CoreText.K("Rotation"), 0x0C, MapUnit.Degrees),
            I(CoreText.K("Area"), 0x18),
            C(CoreText.K("Size"), 0x1C, CoreText.K("Large"), CoreText.K("Standard")),
            B(CoreText.K("Points"), 0x1D, MapUnit.Points),
            F(CoreText.K("Fuel added"), 0x20, MapUnit.Fraction),
            L(CoreText.K("Pads activated next"), 0x28, 0x24),
            B(CoreText.K("Active at the start"), 0x3C, MapUnit.YesNo),
        ]),
        new(MissionObjectKind.BallTarget, CoreText.K("Ball target"), CoreText.K("Ball targets"), 0x1C, MapPlacement.Plain, 0x00, -1,
        [
            V(CoreText.K("Base"), 0x00, MapUnit.Meters),
            I(CoreText.K("Area"), 0x0C),
            F(CoreText.K("Radius"), 0x10, MapUnit.Meters),
            F(CoreText.K("Height"), 0x14, MapUnit.Meters),
            B(CoreText.K("Points"), 0x19, MapUnit.Points),
        ]),
        new(MissionObjectKind.Photo, CoreText.K("Photo subject"), CoreText.K("Photo subjects"), 0x14, MapPlacement.None, 0, -1, []),
        new(MissionObjectKind.MecaHawk, CoreText.K("Meca Hawk domain"), CoreText.K("Meca Hawk"), 0xAC, MapPlacement.Plain, 0x00, -1,
        [
            V(CoreText.K("Centre of the domain"), 0x00, MapUnit.Meters),
            F(CoreText.K("Radius of the domain"), 0x0C, MapUnit.Meters),
            B(CoreText.K("The Meca Hawk starts here"), 0x10, MapUnit.YesNo),
            B(CoreText.K("Chance to walk"), 0x11, MapUnit.Percent),
            B(CoreText.K("Chance to run or swim"), 0x12, MapUnit.Percent),
            B(CoreText.K("Chance to dive"), 0x13, MapUnit.Percent),
            F(CoreText.K("Walking time, from"), 0x18, MapUnit.Seconds),
            F(CoreText.K("Walking time, to"), 0x1C, MapUnit.Seconds),
            F(CoreText.K("Running time, from"), 0x20, MapUnit.Seconds),
            F(CoreText.K("Running time, to"), 0x24, MapUnit.Seconds),
            F(CoreText.K("Walking in water, from"), 0x28, MapUnit.Seconds),
            F(CoreText.K("Walking in water, to"), 0x2C, MapUnit.Seconds),
            F(CoreText.K("Swimming time, from"), 0x30, MapUnit.Seconds),
            F(CoreText.K("Swimming time, to"), 0x34, MapUnit.Seconds),
            F(CoreText.K("Staggering after a hit, from"), 0x38, MapUnit.Seconds),
            F(CoreText.K("Staggering after a hit, to"), 0x3C, MapUnit.Seconds),
            F(CoreText.K("Diving time, from"), 0x40, MapUnit.Seconds),
            F(CoreText.K("Diving time, to"), 0x44, MapUnit.Seconds),
            F(CoreText.K("Standing time, from"), 0x48, MapUnit.Seconds),
            F(CoreText.K("Standing time, to"), 0x4C, MapUnit.Seconds),
            F(CoreText.K("Walking speed, from"), 0x50, MapUnit.MetersPerSecond),
            F(CoreText.K("Walking speed, to"), 0x54, MapUnit.MetersPerSecond),
            F(CoreText.K("Running speed, from"), 0x58, MapUnit.MetersPerSecond),
            F(CoreText.K("Running speed, to"), 0x5C, MapUnit.MetersPerSecond),
            F(CoreText.K("Speed in water, from"), 0x60, MapUnit.MetersPerSecond),
            F(CoreText.K("Speed in water, to"), 0x64, MapUnit.MetersPerSecond),
            F(CoreText.K("Swimming speed, from"), 0x68, MapUnit.MetersPerSecond),
            F(CoreText.K("Swimming speed, to"), 0x6C, MapUnit.MetersPerSecond),
            F(CoreText.K("Stagger speed, from"), 0x70, MapUnit.MetersPerSecond),
            F(CoreText.K("Stagger speed, to"), 0x74, MapUnit.MetersPerSecond),
            F(CoreText.K("Diving speed, from"), 0x78, MapUnit.MetersPerSecond),
            F(CoreText.K("Diving speed, to"), 0x7C, MapUnit.MetersPerSecond),
            F(CoreText.K("Rock speed, from"), 0x80, MapUnit.MetersPerSecond),
            F(CoreText.K("Rock speed, to"), 0x84, MapUnit.MetersPerSecond),
            F(CoreText.K("Rock lifetime, from"), 0x88, MapUnit.Seconds),
            F(CoreText.K("Rock lifetime, to"), 0x8C, MapUnit.Seconds),
            F(CoreText.K("Throws from a distance of"), 0x90, MapUnit.Meters),
            F(CoreText.K("Throws up to a distance of"), 0x94, MapUnit.Meters),
            F(CoreText.K("Pause between throws"), 0x98, MapUnit.Seconds),
            F(CoreText.K("Points per missile hit"), 0xA4, MapUnit.Points),
            F(CoreText.K("Missile hits needed (0 = 5)"), 0xA8),
        ]),
        new(MissionObjectKind.Formation, CoreText.K("Formation"), CoreText.K("Formations"), 0x4C, MapPlacement.None, 0, -1, []),
        new(MissionObjectKind.CannonTarget, CoreText.K("Cannonball target"), CoreText.K("Cannonball target"), 0x1C, MapPlacement.Plain, 0x00, 0x0C,
        [
            V(CoreText.K("Position"), 0x00, MapUnit.Meters),
            V(CoreText.K("Rotation"), 0x0C, MapUnit.Degrees),
            C(CoreText.K("Size"), 0x18, CoreText.K("Small"), CoreText.K("Middle"), CoreText.K("Large")),
        ]),
        new(MissionObjectKind.HopperGoal, CoreText.K("Hopper goal"), CoreText.K("Hopper goals"), 0x20, MapPlacement.Plain, 0x04, -1,
        [
            I(CoreText.K("Area"), 0x00),
            V(CoreText.K("Position"), 0x04, MapUnit.Meters),
            I(CoreText.K("Ends the mission"), 0x10, MapUnit.YesNo),
            F(CoreText.K("Radius"), 0x14, MapUnit.Meters),
            F(CoreText.K("Height of the model"), 0x18, MapUnit.Meters),
        ]),
        new(MissionObjectKind.Observer, CoreText.K("Camera point"), CoreText.K("Camera points"), 0x10, MapPlacement.Plain, 0x00, -1,
        [
            V(CoreText.K("Position"), 0x00, MapUnit.Meters),
            F(CoreText.K("Radius"), 0x0C, MapUnit.Meters),
        ]),
    ];

    /// <summary>The type of an object kind.</summary>
    public static MapObjectType Of(MissionObjectKind kind) => Types[(int)kind];
}
