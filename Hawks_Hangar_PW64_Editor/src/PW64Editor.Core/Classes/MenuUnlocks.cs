using System.Text.Json.Serialization;
using PW64Editor.Core.Localization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Classes;

/// <summary>What a condition asks of a field.</summary>
public enum UnlockKind
{
    /// <summary>The sum of the field's points reaches the value.</summary>
    Points = 0,

    /// <summary>The field's medal reaches the value (<see cref="UnlockMedal"/>).</summary>
    Medal = 1,
}

/// <summary>The medals a condition can ask for.</summary>
public enum UnlockMedal
{
    Bronze = 1,
    Silver = 2,
    Gold = 3,
    Perfect = 4,
}

/// <summary>One condition of a menu field: a field (class, level or stage) with at least so many points or this medal.</summary>
public sealed class UnlockCondition
{
    /// <summary>Vehicle of the field the condition asks about.</summary>
    public int Vehicle { get; set; }

    /// <summary>Class (level, stage) of that field.</summary>
    public int Class { get; set; }

    /// <summary>Points or medal.</summary>
    public UnlockKind Kind { get; set; }

    /// <summary>The points, or the medal (<see cref="UnlockMedal"/> as a number).</summary>
    public int Value { get; set; }

    /// <summary>The field as an index (<see cref="MenuUnlocks.FieldIndex"/>).</summary>
    [JsonIgnore]
    public int Field => MenuUnlocks.FieldIndex((Vehicle)Vehicle, Class);

    /// <summary>A condition.</summary>
    public static UnlockCondition Of(Vehicle vehicle, int classIndex, UnlockKind kind, int value) =>
        new() { Vehicle = (int)vehicle, Class = classIndex, Kind = kind, Value = value };

    /// <summary>A copy.</summary>
    public UnlockCondition Clone() => new() { Vehicle = Vehicle, Class = Class, Kind = Kind, Value = Value };
}

/// <summary>The conditions of one menu field (all must be met; none = open from the start).</summary>
public sealed class FieldUnlock
{
    /// <summary>Vehicle of the field.</summary>
    public int Vehicle { get; set; }

    /// <summary>Class (level, stage) of the field.</summary>
    public int Class { get; set; }

    /// <summary>The conditions.</summary>
    public List<UnlockCondition> Conditions { get; set; } = [];

    /// <summary>The field as an index.</summary>
    [JsonIgnore]
    public int Field => MenuUnlocks.FieldIndex((Vehicle)Vehicle, Class);

    /// <summary>A copy.</summary>
    public FieldUnlock Clone() => new() { Vehicle = Vehicle, Class = Class, Conditions = [.. Conditions.Select(c => c.Clone())] };
}

/// <summary>
/// The unlock conditions of the 25 fields of the level select (Menu tab): 12 license classes, 9 bonus game levels and 4
/// Birdman stages. Without own rules the game's rules apply (<see cref="Original"/>); with own rules the editor writes the
/// conditions of every field into the data area (<see cref="Table"/>) and the game module decides what is open and moves
/// the menu cursor from open field to open field (Menu_Unlocks.md).
/// </summary>
public static class MenuUnlocks
{
    /// <summary>The fields in the order of the table (as <see cref="ClassLimits.All"/>).</summary>
    public static IReadOnlyList<(Vehicle Vehicle, int Class)> Fields => ClassLimits.All;

    /// <summary>Number of fields.</summary>
    public const int FieldCount = 25;

    /// <summary>Most conditions per field.</summary>
    public const int MaxConditions = 8;

    /// <summary>RAM address of the unlock table (data area, behind the class table).</summary>
    public const uint TableAddress = 0x801FDD00;

    /// <summary>Size of the unlock table.</summary>
    public const int TableSize = 0x20 + (FieldCount * MaxConditions * 3);

    /// <summary>Highest number of points a condition can ask for.</summary>
    public const int MaxPoints = 9999;

    /// <summary>Index of a field (license 0-11: vehicle × 4 + class; bonus 12-20; Birdman 21-24), or -1.</summary>
    public static int FieldIndex(Vehicle vehicle, int classIndex)
    {
        for (int i = 0; i < Fields.Count; i++)
        {
            if (Fields[i].Vehicle == vehicle && Fields[i].Class == classIndex)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The game's rules as conditions: Class A with a medal in Beginner for all three license vehicles; Class B and
    /// Pilot also with a medal in every class before (per vehicle); a bonus game with silver in Class A/B/Pilot for all
    /// three, its levels 2 and 3 also with a medal in every level before; Birdman stage 1 with silver in Beginner for all
    /// three, stages 2-4 with silver in Class A/B/Pilot for all three and silver in every level of the matching bonus game.
    /// </summary>
    public static List<FieldUnlock> Original()
    {
        var result = new List<FieldUnlock>();
        foreach ((Vehicle vehicle, int classIndex) in Fields)
        {
            var field = new FieldUnlock { Vehicle = (int)vehicle, Class = classIndex };
            if (Vehicles.IsMain(vehicle))
            {
                if (classIndex > 0)
                {
                    field.Conditions.AddRange(Vehicles.Main.Select(v => UnlockCondition.Of(v, 0, UnlockKind.Medal, (int)UnlockMedal.Bronze)));
                    for (int c = 1; c < classIndex; c++)
                    {
                        field.Conditions.Add(UnlockCondition.Of(vehicle, c, UnlockKind.Medal, (int)UnlockMedal.Bronze));
                    }
                }
            }
            else if (vehicle == Vehicle.Birdman)
            {
                field.Conditions.AddRange(Vehicles.Main.Select(v => UnlockCondition.Of(v, classIndex, UnlockKind.Medal, (int)UnlockMedal.Silver)));
                if (classIndex > 0)
                {
                    Vehicle game = Vehicles.BonusGames[classIndex - 1];
                    field.Conditions.AddRange(Enumerable.Range(0, 3).Select(l => UnlockCondition.Of(game, l, UnlockKind.Medal, (int)UnlockMedal.Silver)));
                }
            }
            else
            {
                int required = vehicle - Vehicle.Cannonball + 1; // Class A, B, Pilot
                field.Conditions.AddRange(Vehicles.Main.Select(v => UnlockCondition.Of(v, required, UnlockKind.Medal, (int)UnlockMedal.Silver)));
                for (int l = 0; l < classIndex; l++)
                {
                    field.Conditions.Add(UnlockCondition.Of(vehicle, l, UnlockKind.Medal, (int)UnlockMedal.Bronze));
                }
            }

            result.Add(field);
        }

        return result;
    }

    /// <summary>The rules that count: the project's, or the game's.</summary>
    public static List<FieldUnlock> Effective(IReadOnlyList<FieldUnlock>? own)
    {
        List<FieldUnlock> result = Original();
        if (own is null)
        {
            return result;
        }

        foreach (FieldUnlock field in own)
        {
            int index = result.FindIndex(f => f.Vehicle == field.Vehicle && f.Class == field.Class);
            if (index >= 0)
            {
                result[index] = field.Clone();
            }
        }

        return result;
    }

    /// <summary>What is wrong with the rules (the first problem), or null.</summary>
    /// <param name="rules">The rules.</param>
    /// <param name="layout">The project's missions (their tests), or null for the game's.</param>
    public static string? Problem(IReadOnlyList<FieldUnlock> rules, SaveGameLayout? layout = null)
    {
        foreach (FieldUnlock field in rules)
        {
            string name = FieldName((Vehicle)field.Vehicle, field.Class);
            if (field.Conditions.Count > MaxConditions)
            {
                return CoreText.F("{0}: at most {1} conditions.", name, MaxConditions);
            }

            foreach (UnlockCondition condition in field.Conditions)
            {
                if (condition.Field < 0)
                {
                    return CoreText.F("{0}: a condition names a field the menu does not have.", name);
                }

                if (condition.Field == field.Field)
                {
                    return CoreText.F("{0}: a field cannot need itself.", name);
                }

                if (condition.Kind == UnlockKind.Points ? condition.Value is < 0 or > MaxPoints : condition.Value is < 1 or > 4)
                {
                    return CoreText.F("{0}: a condition has a value the game cannot use.", name);
                }

                if (condition.Kind == UnlockKind.Points && condition.Value > HighestPoints((Vehicle)condition.Vehicle, condition.Class, layout))
                {
                    return CoreText.F("{0}: {1} can give at most {2} points, so the condition can never be met.", name,
                        FieldName((Vehicle)condition.Vehicle, condition.Class), HighestPoints((Vehicle)condition.Vehicle, condition.Class, layout));
                }
            }
        }

        return rules.Any(f => f.Conditions.Count == 0)
            ? null
            : CoreText.T("No field is open from the start, so the player could not start any test. Leave at least one field without conditions.");
    }

    /// <summary>The most points a field can have: 100 per test, 100 per bonus game level.</summary>
    /// <param name="vehicle">The vehicle.</param>
    /// <param name="classIndex">The class.</param>
    /// <param name="layout">The project's missions (their tests), or null for the game's.</param>
    public static int HighestPoints(Vehicle vehicle, int classIndex, SaveGameLayout? layout = null) =>
        vehicle is Vehicle.Cannonball or Vehicle.SkyDiving or Vehicle.JumbleHopper
            ? SaveProgress.MaxPoints
            : SaveProgress.MaxPoints * (layout ?? SaveGameLayout.RetailWithBirdmanStage4).TestCount(classIndex, vehicle);

    /// <summary>
    /// The unlock table for the data area, or null for the game's rules (no own rules and not every field open).
    /// </summary>
    /// <param name="own">The project's rules, or null.</param>
    /// <param name="allOpen">True to open every field (Features tab, for testing).</param>
    public static byte[]? Table(IReadOnlyList<FieldUnlock>? own, bool allOpen)
    {
        if (own is null && !allOpen)
        {
            return null;
        }

        byte[] table = new byte[TableSize];
        if (allOpen)
        {
            return table; // no field has conditions
        }

        foreach (FieldUnlock field in Effective(own))
        {
            int f = field.Field;
            if (f < 0)
            {
                continue;
            }

            List<UnlockCondition> conditions = [.. field.Conditions.Where(c => c.Field >= 0).Take(MaxConditions)];
            table[f] = (byte)conditions.Count;
            for (int i = 0; i < conditions.Count; i++)
            {
                int at = 0x20 + (((f * MaxConditions) + i) * 3);
                UnlockCondition c = conditions[i];
                int value = Math.Clamp(c.Value, 0, MaxPoints);
                table[at] = (byte)(c.Field | (c.Kind == UnlockKind.Medal ? 0x80 : 0));
                table[at + 1] = (byte)(value >> 8);
                table[at + 2] = (byte)value;
            }
        }

        return table;
    }

    /// <summary>Reads the rules of an unlock table (for the import), or null if every field is open (testing).</summary>
    public static List<FieldUnlock>? ReadTable(ReadOnlySpan<byte> table)
    {
        if (table.Length < TableSize)
        {
            return null;
        }

        var result = new List<FieldUnlock>();
        for (int f = 0; f < FieldCount; f++)
        {
            var field = new FieldUnlock { Vehicle = (int)Fields[f].Vehicle, Class = Fields[f].Class };
            for (int i = 0; i < Math.Min((int)table[f], MaxConditions); i++)
            {
                int at = 0x20 + (((f * MaxConditions) + i) * 3);
                int g = table[at] & 0x7F;
                if (g < FieldCount)
                {
                    field.Conditions.Add(UnlockCondition.Of(Fields[g].Vehicle, Fields[g].Class,
                        (table[at] & 0x80) != 0 ? UnlockKind.Medal : UnlockKind.Points, (table[at + 1] << 8) | table[at + 2]));
                }
            }

            result.Add(field);
        }

        return result;
    }

    /// <summary>True if the rules are the game's (same conditions in the same order).</summary>
    public static bool IsOriginal(IReadOnlyList<FieldUnlock> rules)
    {
        List<FieldUnlock> original = Original();
        return rules.Count == original.Count && rules.Zip(original).All(p => p.First.Vehicle == p.Second.Vehicle && p.First.Class == p.Second.Class
            && p.First.Conditions.Count == p.Second.Conditions.Count
            && p.First.Conditions.Zip(p.Second.Conditions).All(c => c.First.Vehicle == c.Second.Vehicle && c.First.Class == c.Second.Class
                && c.First.Kind == c.Second.Kind && c.First.Value == c.Second.Value));
    }

    /// <summary>Name of a field ("Hang Glider Class A", "Sky Diving Level 2", "Birdman Stage 3").</summary>
    public static string FieldName(Vehicle vehicle, int classIndex) => $"{Vehicles.Name(vehicle)} {Missions.MissionCatalog.ClassName(vehicle, classIndex)}";

    /// <summary>A condition in words ("Hang Glider Beginner: silver", "Sky Diving Level 1: 80 points").</summary>
    public static string Describe(UnlockCondition condition) =>
        condition.Kind == UnlockKind.Medal
            ? CoreText.F("{0}: {1}", FieldName((Vehicle)condition.Vehicle, condition.Class), MedalName(condition.Value))
            : CoreText.F("{0}: {1} points", FieldName((Vehicle)condition.Vehicle, condition.Class), condition.Value);

    /// <summary>Name of a medal value.</summary>
    public static string MedalName(int medal) => medal switch
    {
        1 => CoreText.T("bronze"),
        2 => CoreText.T("silver"),
        3 => CoreText.T("gold"),
        _ => CoreText.T("perfect"),
    };
}
