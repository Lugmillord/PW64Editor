using System.Buffers.Binary;
using PW64Editor.Core.Build;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>
/// The "View Demo" button on the hint screen of the Beginner missions (test_menu.c). The game
/// shows it for class Beginner of Hang Glider, Rocket Belt and Gyrocopter; the editor can hide it
/// per vehicle, so the demo flight cannot be chosen.
/// </summary>
/// <remarks>
/// <para>Three places in the game code check "class Beginner and vehicle below 3": the cursor
/// limits of the hint screen and the A button (testMenuHandler) and the drawing of the button
/// (testMenuDraw). Each check <c>slti at, veh, 3; beqz at, …</c> becomes
/// <c>lui at, mask; sllv at, at, veh; bgez at, …</c>: the mask has bit 31 − vehicle set for every
/// vehicle that keeps its button, so the shifted sign bit says "show". The <c>lui</c> goes into
/// the delay slot (a nop) of the class check before it; the branch targets write <c>at</c> before
/// reading it. With all three bits set the result equals the original check.</para>
/// <para>Without the button, the hint screen works like those of the other classes: only
/// "Return" can be chosen.</para>
/// </remarks>
public static class DemoButton
{
    private const uint RamToRom = 0x80278AD0;
    private const uint Nop = 0x00000000;

    /// <summary>One check: the delay slot for the lui, the slti and the beqz, and the register of the vehicle.</summary>
    private sealed record Site(string Name, uint DelaySlot, uint Slti, uint Beqz, uint OriginalSlti, uint OriginalBeqz, int VehicleRegister);

    private static readonly Site[] Sites =
    [
        new("cursor limits, testMenuHandler", 0x80349368, 0x80349374, 0x80349378, 0x2B210003, 0x10200005, 25), // t9
        new("A button, testMenuHandler", 0x80349978, 0x80349984, 0x80349988, 0x2B210003, 0x10200017, 25),      // t9
        new("button drawing, testMenuDraw", 0x80349E74, 0x80349E7C, 0x80349E80, 0x29810003, 0x10200003, 12),   // t4
    ];

    /// <summary>True if the mission's hint screen has the button (Beginner of the three license vehicles).</summary>
    public static bool HasButton(TestId id) => id.Class == 0 && id.Vehicle is Vehicle.HangGlider or Vehicle.RocketBelt or Vehicle.Gyrocopter;

    /// <summary>The changes of the game code that hide the button for the given vehicles (none: no changes).</summary>
    public static IReadOnlyList<DataPatch> Patches(IReadOnlyCollection<Vehicle> hidden)
    {
        if (hidden.Count == 0)
        {
            return [];
        }

        uint mask = 0;
        foreach (Vehicle vehicle in new[] { Vehicle.HangGlider, Vehicle.RocketBelt, Vehicle.Gyrocopter })
        {
            if (!hidden.Contains(vehicle))
            {
                mask |= 1u << (31 - (int)vehicle);
            }
        }

        var patches = new List<DataPatch>();
        foreach (Site site in Sites)
        {
            uint lui = 0x3C010000 | (mask >> 16);                                          // lui at, mask
            uint sllv = ((uint)site.VehicleRegister << 21) | (1u << 16) | (1u << 11) | 0x04; // sllv at, at, veh
            uint bgez = 0x04210000 | (site.OriginalBeqz & 0xFFFF);                          // bgez at, (same target)
            patches.Add(Word(site.DelaySlot, lui, $"demo button ({site.Name})"));
            patches.Add(Word(site.Slti, sllv, $"demo button ({site.Name})"));
            patches.Add(Word(site.Beqz, bgez, $"demo button ({site.Name})"));
        }

        return patches;
    }

    /// <summary>True if the ROM has the original instructions at all places (a US ROM).</summary>
    public static bool MatchesRom(ReadOnlySpan<byte> rom)
    {
        foreach (Site site in Sites)
        {
            if (Read(rom, site.DelaySlot) != Nop || Read(rom, site.Slti) != site.OriginalSlti || Read(rom, site.Beqz) != site.OriginalBeqz)
            {
                return false;
            }
        }

        return true;
    }

    private static uint Read(ReadOnlySpan<byte> rom, uint ram) => BinaryPrimitives.ReadUInt32BigEndian(rom[(int)(ram - RamToRom)..]);

    private static DataPatch Word(uint ram, uint value, string description)
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(data, value);
        return new DataPatch((int)(ram - RamToRom), data, description);
    }
}
