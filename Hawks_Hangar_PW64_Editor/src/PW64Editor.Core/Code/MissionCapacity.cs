namespace PW64Editor.Core.Code;

/// <summary>
/// The changes of the game code for up to 128 missions and up to five tests per class (US version), built into every
/// hack with the game module.
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><b>User files</b>: the kernel keeps the ROM address of every user file (all files that are no kernel type,
///         among them the missions) in gUVBlockOffsets.userFiles[128]; the game has 111, 128 missions need 178. The array
///         moves to the last KiB of the code area (<see cref="UserFilesAddress"/>, 256 entries). uvMemInitBlockHdr fills it
///         at the start, before the game module is loaded, so its store gets an instruction more: the instruction in front
///         of its default case is never run, and the two branches to the case now start there. uvUserFileRead reads the
///         new array and checks the index against 256.</item>
///   <item><b>Mission list</b>: taskInit loaded 0x3D (61) missions through sTaskUserFileIdxLookup (task.c); taskLoadCommObj
///         now reads the list of the lower data area (<see cref="DataExtension.MissionListAddress"/>) and the loop count is
///         the number of missions.</item>
///   <item><b>Photo missions</b>: testMenuShutterBug, snapPhoto's "Photo" message and the album's points (func_8033ADD4)
///         ask the module, which looks the mission up in the table of the lower data area.</item>
///   <item><b>Photos in the saved game</b>: saveFileWrite and saveFileLoad save 12 bits more behind the album (0x414 instead
///         of 0x408 bits; the buffer of saveFileLoad is cleared with 0x84 instead of 0x81 bytes), and the calls of
///         func_8033E860 and func_8033F050 go through the module, which writes and reads the third bit of each photo's test
///         and class there. Saved games of the original stay compatible (those bits are 0 there).</item>
///   <item><b>Total screen</b> (total_results.c): totResultInit and totResultDrawTally take five number buffers from the
///         module instead of four, and the header of the medal text moves one line up for five tests.</item>
/// </list>
/// <para>Names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static class MissionCapacity
{
    /// <summary>RAM address of the moved user file array: the last KiB of the code area.</summary>
    public const uint UserFilesAddress = CodeArea.End - (UserFiles * 4);

    /// <summary>Entries of the moved user file array.</summary>
    public const int UserFiles = 256;

    /// <summary>ROM offset of "addiu at, zero, 0x3D" (the number of missions) in taskInit (0x80344C94).</summary>
    public const int MissionCountRomOffset = 0xCC1C4;

    private static uint Hi(uint address) => (address + 0x8000) >> 16;

    private static uint Lo(uint address) => address & 0xFFFF;

    /// <summary>The changes for a game with this many missions.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No mission, or more than the game can hold.</exception>
    public static IReadOnlyList<WordPatch> Patches(int missions)
    {
        if (missions is < 1 or > DataExtension.MaxMissions)
        {
            throw new ArgumentOutOfRangeException(nameof(missions), $"The game has room for 1 to {DataExtension.MaxMissions} missions.");
        }

        // uvUserFileRead adds index × 4 to a base and reads +0x1838 from it (the offset of userFiles in gUVBlockOffsets).
        uint readBase = UserFilesAddress - 0x1838;
        uint list = DataExtension.MissionListAddress;
        uint points = GameModuleImage.TotalPointsAddress, units = GameModuleImage.TotalUnitsAddress;
        return
        [
            // 1. uvMemInitBlockHdr (kernel, 0x802246A0): the two branches to the default case of the file types start one
            //    instruction earlier (0x802249CC, never run before; their delay slot loads the count), where the case is now
            //    "sll t6, v0, 2; lui at, hi; addu t7, at, t6; sw v1, lo(t7); addiu t8, v0, 1; sh t8, 0x1C(s0)".
            new("user files, branch to the default case (uvMemInitBlockHdr)", 0x257B4, [0x10000072], [0x10000071]),
            new("user files, branch to the default case (uvMemInitBlockHdr)", 0x25800, [0x1000005F], [0x1000005E]),
            new("user files, the default case (uvMemInitBlockHdr)", 0x2597C,
                [0x9602001C, 0x00027080, 0x010E7821, 0xADE31838, 0x24580001, 0xA618001C],
                [0x00027080, 0x3C010000 | Hi(UserFilesAddress), 0x002E7821, 0xADE30000 | Lo(UserFilesAddress), 0x24580001, 0xA618001C]),
            // uvUserFileRead (0x802314D0): the base and the check "index < 256".
            new("user files, uvUserFileRead", 0x32480, [0x3C0F802B, 0x25EF53F0], [0x3C0F0000 | Hi(readBase), 0x25EF0000 | Lo(readBase)]),
            new("user files, index check (uvUserFileRead)", 0x324B0, [0x3C19802B, 0x273955F0],
                [0x3C190000 | Hi(readBase + (UserFiles * 4)), 0x27390000 | Lo(readBase + (UserFiles * 4))]),

            // 2. The mission list: taskLoadCommObj (0x80345CE4) "lui a0, 0x8035; ...; lbu a0, 0x7AC(a0)", taskInit's count.
            new("mission list, taskLoadCommObj", 0xCD21C, [0x3C048035], [0x3C040000 | Hi(list)]),
            new("mission list, taskLoadCommObj", 0xCD228, [0x908407AC], [0x90840000 | Lo(list)]),
            new("number of missions, taskInit", MissionCountRomOffset, [0x2401003D], [0x24010000 | (uint)missions]),

            // 3. Photo missions: testMenuShutterBug (0x80348710) jumps to the module; snapPhoto (0x80338B04) and the album
            //    (0x8033BA2C) call it with the photo record instead of comparing class and test.
            new("photo missions, testMenuShutterBug", 0xCFC40, [0x3C038036, 0x8C632690], [CodeArea.J(GameModuleImage.ShutterBugAddress), 0]),
            new("photo missions, snapPhoto", 0xC0034,
                [0x96220032, 0x8FA300DC, 0x2C440001, 0x50800007, 0x384E0001, 0x962B0030],
                [CodeArea.Jal(GameModuleImage.PhotoRecordAddress), 0x02202025, 0xAFA20078, 0x8FA300DC, 0x10000016, 0x00000000]),
            new("photo missions, album (func_8033ADD4)", 0xC2F5C,
                [0x94A40032, 0x2C830001, 0x50600005, 0x38830001, 0x94A30030],
                [CodeArea.Jal(GameModuleImage.PhotoAlbumAddress), 0x00A02025, 0x8FA500E4, 0x10000015, 0x00401825]),

            // 4. Photos in the saved game: saveFileWrite (0x802E8590, 0x802E85E0) and saveFileLoad (0x802E88F8, 0x802E8900,
            //    0x802E894C).
            new("photos, saveFileWrite", 0x6FAC0, [0x0C0CFA18], [CodeArea.Jal(GameModuleImage.PhotoPackAddress)]),
            new("photos (bits), saveFileWrite", 0x6FB10, [0x2A010408], [0x2A010414]),
            new("photos (buffer), saveFileLoad", 0x6FE28, [0x24060081], [0x24060084]),
            new("photos (bits), saveFileLoad", 0x6FE30, [0x24110408], [0x24110414]),
            new("photos, saveFileLoad", 0x6FE7C, [0x0C0CFC14], [CodeArea.Jal(GameModuleImage.PhotoUnpackAddress)]),

            // 5. The total screen: the buffers of totResultInit (0x803472E4 ...) and totResultDrawTally (0x80347AA4 ...), and
            //    the header's y (0x803479C8: "lui s0; lw s0, sMedalName; lui a0; li s1, 180").
            new("total screen, number buffers (totResultInit)", 0xCE814, [0x3C028035, 0x3C088035],
                [0x3C020000 | Hi(points), 0x3C080000 | Hi(points + 20)]),
            new("total screen, number buffers (totResultInit)", 0xCE830, [0x25080968, 0x24420958],
                [0x25080000 | Lo(points + 20), 0x24420000 | Lo(points)]),
            new("total screen, units (totResultInit)", 0xCE890, [0x3C0D8038, 0x25ADAD48], [0x3C0D0000 | Hi(units), 0x25AD0000 | Lo(units)]),
            new("total screen, number buffer (totResultInit)", 0xCE8C0, [0x3C048035], [0x3C040000 | Hi(points)]),
            new("total screen, number buffer (totResultInit)", 0xCE8CC, [0x8C840958], [0x8C840000 | Lo(points)]),
            new("total screen, units (totResultDrawTally)", 0xCEFD4, [0x3C198038, 0x2731AD48], [0x3C190000 | Hi(units), 0x27310000 | Lo(units)]),
            new("total screen, number buffers (totResultDrawTally)", 0xCEFE8, [0x3C128035, 0x26520958],
                [0x3C120000 | Hi(points), 0x26520000 | Lo(points)]),
            new("total screen, header (totResultDrawTally)", 0xCEEF8, [0x3C108038, 0x8E10AD1C, 0x3C048035, 0x241100B4],
                [CodeArea.Jal(GameModuleImage.TotalHeaderAddress), 0, 0x3C048035, 0]),
        ];
    }
}
