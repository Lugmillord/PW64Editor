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
        ];
    }
}
