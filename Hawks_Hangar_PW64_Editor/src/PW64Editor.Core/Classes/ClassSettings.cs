using System.Text.Json.Serialization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Classes;

/// <summary>
/// The settings of one class of a vehicle (Classes tab, stored in project.json): its own medal limits and its island.
/// For the bonus games the class is the level, for the Birdman the stage. Only classes with a change are stored.
/// </summary>
public sealed class ClassSettings
{
    /// <summary>The vehicle (<see cref="SaveGame.Vehicle"/> as a number).</summary>
    public int Vehicle { get; set; }

    /// <summary>The class (0 Beginner … 3 Pilot), the level of a bonus game or the stage of the Birdman.</summary>
    public int Class { get; set; }

    /// <summary>Own medal limits (bronze, silver, gold: points of the whole class), or null for the original ones.</summary>
    public List<int>? Medals { get; set; }

    /// <summary>
    /// The island of the class (0 Holiday Island … 3 Ever-Frost Island), or null for the original one: the island the
    /// missions of the class are flown on (each mission's COMM byte 3) and the one the test screen shows.
    /// </summary>
    public int? Island { get; set; }

    /// <summary>The vehicle as an enum.</summary>
    [JsonIgnore]
    public Vehicle VehicleId => (Vehicle)Vehicle;

    /// <summary>True if nothing is changed.</summary>
    [JsonIgnore]
    public bool IsEmpty => Medals is null && Island is null;

    /// <summary>Settings for a class.</summary>
    public static ClassSettings For(Vehicle vehicle, int classIndex) => new() { Vehicle = (int)vehicle, Class = classIndex };

    /// <summary>A copy.</summary>
    public ClassSettings Clone() => new()
    {
        Vehicle = Vehicle,
        Class = Class,
        Medals = Medals is null ? null : [.. Medals],
        Island = Island,
    };
}
