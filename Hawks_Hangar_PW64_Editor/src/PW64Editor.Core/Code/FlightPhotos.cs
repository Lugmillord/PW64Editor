namespace PW64Editor.Core.Code;

/// <summary>
/// Ten photos per flight (stage 8, built into every hack with the game module). The album stays at six photos: the save
/// file has no room for more.
/// </summary>
/// <remarks>
/// <para>snap.c keeps the photos of a flight in D_80373390[6] (PHOTO_COUNT_MAX, shared with the album D_80373060[6]).
/// The list moves to <see cref="HighDataArea.FlightPhotoTable"/> with room for <see cref="Count"/>: the references of
/// snapInit, func_80338614, snapPhoto (func_80338A14) and func_8033E3A8 get the new address (one of them indexed:
/// "lui at, hi; addu at, at, index; sw t5, lo(at)"), snapInit's loop clears ten, and the checks of snapPhoto, the Hang
/// Glider's and the Birdman's shutter (func_80339C8C, func_80339E1C) and snapGetPhotoCount count to ten. The album reset
/// (func_80337D50) also cleared six photos of D_80373390; the code fix <see cref="CodeFixes.PhotoAlbum"/> points it to the
/// album itself, without the fix it clears memory that is no longer used.</para>
/// <para>The photo grid (func_8033D3EC) is replaced by the module's (photos.c, <see cref="GameModuleImage.PhotoGridAddress"/>):
/// pages of six photos, and "Please wait..." while the photos are drawn. The two single photo views (func_8033DDD8, the
/// example photos, and func_8033DFD0, one photo of the album or the flight) call the module's
/// <see cref="GameModuleImage.PhotoBusyAddress"/> instead of func_8033A610, which shows the text in the photo area first.
/// When the photo's test has to be loaded (func_8033A128 → gameUpdateStateTestSetup), that function clears the photo area
/// twice before it loads the island for seconds; the two calls of func_8033A128 go through
/// <see cref="GameModuleImage.PhotoSetupAddress"/>, and the second uvGfxEnd of gameUpdateStateTestSetup through
/// <see cref="GameModuleImage.SetupFrameAddress"/>, which then draws the text again (only while the photo is set up).</para>
/// <para>Photo subjects (<see cref="Missions.PhotoSubjects"/>): the two calls of snapPhoto (the shutters of the Hang Glider
/// and the Birdman) go to the module, which scores a mission with own subjects itself and calls the game's function for
/// the others; the six loaders of the main subjects ask the module (instead of taskGetClsVehTest) whether the subject gets a
/// radar mark (the game: only in the Hang Glider's photo missions Class A test 1, Class B test 2, Pilot test 3).</para>
/// <para>Sample photos (Step 49): the two calls of the sample photo view (func_8033E3A8 mode 3 on the test screen, mode 1
/// in the pause menu) go to the module, which first makes the sample photos of a mission with own subjects (one per
/// subject with points, the camera placed as set for the subject, from the object where it is at that moment). The sample photo view of
/// func_8033E3A8 (its switch over the island) calls the module, which shows the mission's sample photos or the island's as
/// the game; the captions of the types the game has no text for come from the module; the pause menu offers the photo
/// item for every Hang Glider photo mission and the Birdman photo missions with own subjects (the game: Hang Glider
/// Class A test 1, Class B test 2, Pilot test 3), as the test screen's button.</para>
/// <para>Names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static class FlightPhotos
{
    /// <summary>Photos per flight (the game: 6).</summary>
    public const int Count = 10;

    /// <summary>Photos of the album (PHOTO_COUNT_MAX, unchanged).</summary>
    public const int AlbumCount = 6;

    /// <summary>Bytes per photo (Unk80373060).</summary>
    public const int PhotoSize = 0x70;

    /// <summary>The game's list of the flight's photos (D_80373390).</summary>
    public const uint GameTable = 0x80373390;

    private const uint Table = HighDataArea.FlightPhotoTable;

    private static uint Hi(uint address) => (address + 0x8000) >> 16;

    private static uint Lo(uint address) => address & 0xFFFF;

    private static WordPatch Lui(string description, int rom, uint original, uint address) =>
        new(description, rom, [original], [(original & 0xFFFF0000) | Hi(address)]);

    private static WordPatch Low(string description, int rom, uint original, uint address) =>
        new(description, rom, [original], [(original & 0xFFFF0000) | Lo(address)]);

    /// <summary>The changes of the game code.</summary>
    public static IReadOnlyList<WordPatch> Patches()
    {
        uint end10 = Table + (Count * PhotoSize);
        uint grid = CodeArea.Jal(GameModuleImage.PhotoGridAddress), busy = CodeArea.Jal(GameModuleImage.PhotoBusyAddress);
        uint setup = CodeArea.Jal(GameModuleImage.PhotoSetupAddress), frame = CodeArea.Jal(GameModuleImage.SetupFrameAddress);
        uint snap = CodeArea.Jal(GameModuleImage.SnapPhotoAddress);
        const uint ClsVehTest = 0x0C0D18CF; // jal taskGetClsVehTest
        const uint TextGet = 0x0C0D0866;    // jal textGetDataByIdx
        return
        [
            // snapInit: all ten photos cleared.
            Lui("flight photos, snapInit", 0xBF300, 0x3C0E8037, Table),
            Low("flight photos, snapInit", 0xBF308, 0x25C63390, Table),
            Lui("flight photos, end (snapInit)", 0xBF310, 0x3C088037, end10),
            Low("flight photos, end (snapInit)", 0xBF328, 0x25083630, end10),

            // func_80338614 (the points of the photos).
            Lui("flight photos, func_80338614", 0xBFB6C, 0x3C108037, Table),
            Low("flight photos, func_80338614", 0xBFB70, 0x26103390, Table),

            // snapPhoto: the new photo, the best photo per subject (indexed store of unk6C).
            new("flight photos, count (snapPhoto)", 0xBFFB8, [0x28A10006], [0x28A10000 | Count]),
            Lui("flight photos, snapPhoto", 0xBFFF0, 0x3C098037, Table),
            Low("flight photos, snapPhoto", 0xC0004, 0x25293390, Table),
            Lui("flight photos, best photo (snapPhoto)", 0xC0ED0, 0x3C0A8037, Table),
            Low("flight photos, best photo (snapPhoto)", 0xC0ED4, 0x25423390, Table),
            Lui("flight photos, best photo (snapPhoto)", 0xC0F40, 0x3C018037, Table + 0x6C),
            Low("flight photos, best photo (snapPhoto)", 0xC0F4C, 0xAC2D33FC, Table + 0x6C),

            // The shutter of the Hang Glider (two checks) and of the Birdman (one), and snapGetPhotoCount.
            new("flight photos, count (Hang Glider shutter)", 0xC1270, [0x29810006], [0x29810000 | Count]),
            new("flight photos, count (Hang Glider shutter)", 0xC12E4, [0x2B010006], [0x2B010000 | Count]),
            new("flight photos, count (Birdman shutter)", 0xC1400, [0x29A10006], [0x29A10000 | Count]),
            new("flight photos, count (snapGetPhotoCount)", 0xC6B50, [0x240F0006], [0x240F0000 | Count]),

            // func_8033E3A8: the grid (album and flight) through the module, the flight's photos from the new list.
            new("photo grid, album (func_8033E3A8)", 0xC5A30, [0x0C0CF4FB], [grid]),
            new("photo grid, flight (func_8033E3A8)", 0xC5B30, [0x0C0CF4FB], [grid]),
            Lui("flight photos, grid (func_8033E3A8)", 0xC5B20, 0x3C068037, Table),
            Low("flight photos, grid (func_8033E3A8)", 0xC5B24, 0x24C63390, Table),
            Lui("flight photos, one photo (func_8033E3A8)", 0xC5B40, 0x3C058037, Table),
            Low("flight photos, one photo (func_8033E3A8)", 0xC5B44, 0x24A53390, Table),

            // "Please wait..." in the single photo views.
            new("photos, please wait (func_8033DDD8)", 0xC53CC, [0x0C0CE984], [busy]),
            new("photos, please wait (func_8033DFD0)", 0xC55CC, [0x0C0CE984], [busy]),
            // ... and again after the two clears of gameUpdateStateTestSetup when the photo's test is loaded first.
            new("photos, please wait while loading (func_8033DDD8)", 0xC53D8, [0x0C0CE84A], [setup]),
            new("photos, please wait while loading (func_8033DFD0)", 0xC55D8, [0x0C0CE84A], [setup]),
            new("photos, please wait while loading (gameUpdateStateTestSetup)", 0x73ABC, [0x0C08885F], [frame]),

            // Photo subjects (subjects.c): snapPhoto through the module (the game's function for missions without own
            // subjects), the radar marks of the main subjects in every photo mission and every mission with own subjects.
            new("photo subjects, Hang Glider shutter", 0xC1334, [0x0C0CE285], [snap]),
            new("photo subjects, Birdman shutter", 0xC14B4, [0x0C0CE285], [snap]),
            new("photo subjects, radar mark (whaleLoad)", 0xD3540, [ClsVehTest], [CodeArea.Jal(GameModuleImage.RadarWhaleAddress)]),
            new("photo subjects, radar mark (fountainLoad)", 0x726C8, [ClsVehTest], [CodeArea.Jal(GameModuleImage.RadarFountainAddress)]),
            new("photo subjects, radar mark (missiLoadLStates)", 0x9C7A0, [ClsVehTest], [CodeArea.Jal(GameModuleImage.RadarMissileAddress)]),
            new("photo subjects, radar mark (ferryLoad)", 0x6F0AC, [ClsVehTest], [CodeArea.Jal(GameModuleImage.RadarFerryAddress)]),
            new("photo subjects, radar mark (shuttleLoad)", 0xBD2C8, [ClsVehTest], [CodeArea.Jal(GameModuleImage.RadarShuttleAddress)]),
            new("photo subjects, radar mark (oilPlantLoad)", 0x9CDF0, [ClsVehTest], [CodeArea.Jal(GameModuleImage.RadarFlameAddress)]),
            // ... the marks of the other objects (whale pod, airplanes, model gliders, boats) at the start of the mission
            // (snapInit's hudSetCameraState) and the subjects' labels on the map (map3dLoad's taskGetPHTS, only in
            // map3dMain's view: the pause menu's and the test screen's "View map").
            new("photo subjects, radar marks (snapInit)", 0xBF908, [0x0C0C77DA], [CodeArea.Jal(GameModuleImage.SnapInitAddress)]),
            new("photo subjects, map labels (map3dLoad)", 0x976A0, [0x0C0D170F], [CodeArea.Jal(GameModuleImage.MapPhotosAddress)]),
            new("photo subjects, map view (pause menu)", 0xB439C, [0x0C0C3FA0], [CodeArea.Jal(GameModuleImage.MapPauseAddress)]),
            new("photo subjects, map view (test screen)", 0xD101C, [0x0C0C3FA0], [CodeArea.Jal(GameModuleImage.MapTestAddress)]),

            // Sample photos per mission: made when the view is called; the sample photo view (func_8033E3A8 mode 1)
            // shows them instead of the island's; their captions; the pause menu's photo item for every photo mission.
            new("sample photos, test screen (testMenu)", 0xD1078, [0x0C0CF8EA], [CodeArea.Jal(GameModuleImage.ExamplesPrepareAddress)]),
            new("sample photos, pause menu (gameUpdateStateTestUpdate)", 0x74EC8, [0x0C0CF8EA], [CodeArea.Jal(GameModuleImage.ExamplesPrepareAddress)]),
            new("sample photos, func_8033E3A8", 0xC5A84,
                [0x24010003, 0x3C058037, 0x95A30004, 0x24A53630, 0x3C048035, 0x10610009, 0x02203025, 0x24010005, 0x1061000A,
                    0x3C048035, 0x2401000A, 0x1061000E, 0x3C048035, 0x10000012, 0x24100001, 0x0C0CF776, 0x8C840538, 0x1000000E,
                    0x00408025, 0x3C058037, 0x24A538D0, 0x8C84053C, 0x0C0CF776, 0x02203025, 0x10000007, 0x00408025, 0x3C058037,
                    0x24A53B70, 0x8C840540, 0x0C0CF776, 0x02203025],
                [
                    0x95A40004,                                  // lhu a0, 4(t5): the map
                    CodeArea.Jal(GameModuleImage.ExamplesViewAddress),
                    0x02202825,                                  // move a1, s1: the chosen photo
                    0x1000001C,                                  // b 0x8033E5D4
                    0x00408025,                                  // move s0, v0
                    .. Enumerable.Repeat(0u, 26),
                ]),
            new("sample photos, caption width (func_8033ADD4)", 0xC3198, [TextGet], [CodeArea.Jal(GameModuleImage.ExampleTextAddress)]),
            new("sample photos, caption (func_8033ADD4)", 0xC31B0, [TextGet], [CodeArea.Jal(GameModuleImage.ExampleTextAddress)]),
            new("sample photos, pause menu (func_8032CF28)", 0xB44C8,
                [0x95030002, 0x2C6A0001, 0x11400019, 0xAFAA0040, 0x95020006, 0x2C4C0001, 0x11800006, 0xAFAC0040, 0x95040004,
                    0x388E0001, 0x2DCE0001, 0x15C00010, 0xAFAE0040, 0x38440001, 0x14800006, 0x38490002, 0x950F0004, 0x39F80002,
                    0x2F180001, 0x17000008, 0xAFB80040, 0x2D290001, 0x11200005, 0xAFA90040, 0x950B0004, 0x396C0003, 0x2D8C0001,
                    0xAFAC0040],
                [
                    0x01002025,                                  // move a0, t0: the flight
                    CodeArea.Jal(GameModuleImage.PausePhotoAddress),
                    0x00000000,
                    0xAFA20040,                                  // sw v0, 0x40(sp): the menu with the photo item
                    0x8FA8006C,                                  // lw t0, 0x6c(sp)
                    0x95030002,                                  // lhu v1, 2(t0): the vehicle, as the code behind expects
                    .. Enumerable.Repeat(0u, 22),
                ]),
        ];
    }
}
