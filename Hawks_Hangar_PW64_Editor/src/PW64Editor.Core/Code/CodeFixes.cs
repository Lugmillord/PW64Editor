using PW64Editor.Core.FileSystem;

namespace PW64Editor.Core.Code;

/// <summary>
/// All corrections of bugs in the game's code that the editor knows, in the order they are shown.
/// </summary>
/// <remarks>
/// <para>
/// Fixes only exchange instructions inside the game code. They need no extra room in the ROM, and
/// they do not depend on where data lies, so they keep working when the editor moves game files or
/// the audio data. New fixes are added at the end of <see cref="All"/>; the <see cref="CodeFix.Id"/>
/// of a released fix never changes, because projects store it.
/// </para>
/// <para>
/// Fixes only correct bugs that damage memory, data or saved games, or crash the game. Bugs that
/// only change how the game plays, looks or sounds are kept, so the game feels like the original.
/// </para>
/// <para>
/// <b>Code cave</b>: a fix that needs more instructions than it replaces puts them into
/// <see cref="CodeCaveStart"/> (the body of uvMemScanBlocks, see <see cref="PhotoAlbum"/>). Used so far:
/// 0x2B4A4–0x2B4D4 (PhotoAlbum). Later fixes continue behind the last used word.
/// </para>
/// </remarks>
public static class CodeFixes
{
    /// <summary>The game code lies between the ROM header area and the file table.</summary>
    public const int CodeStart = 0x1000;

    /// <summary>
    /// Two buffer overflows in the text code (US version), checked by running the original
    /// machine code in a MIPS emulator.
    /// </summary>
    /// <remarks>
    /// <para><b>Loader</b> (textLoadBlock, src/app/text_data.c, ROM 0xC9440): after copying a text,
    /// a loop converts the line break and end codes. It counts up to the text size in bytes but
    /// steps through 16-bit values, so it runs over twice the text: up to 422 bytes behind each
    /// text buffer are read and partly rewritten (all values 0x00FE and 0x00FF). Usually that memory
    /// is unused, but where a text ends right in front of the frame buffers or the game code, this
    /// damages them. Fix: the loop counter is increased by 2 instead of 1, so the loop ends exactly
    /// at the end of the text.</para>
    /// <code>
    /// 0xC95B0  addiu s0, s0, 1   →   addiu s0, s0, 2
    /// </code>
    /// <para><b>Line buffer</b> (uvFontPrintStr16, src/kernel/font.c, ROM 0x1A824): a piece of text
    /// is copied into a buffer of 44 values, followed by an end marker. The function accepts 44
    /// characters, and it decides whether to add the marker by the wrong counter (values read
    /// instead of characters stored). With 43 characters and a line break, or with 44 or more, the
    /// marker is written behind the buffer, into the pointer to the font, and the game crashes
    /// (black screen). After an [x=...] position a long piece gets no marker at all. Fix: at most 43
    /// characters are accepted, and the marker is placed by the character counter, so it always
    /// lands inside the buffer. A longer line is cut after 43 characters; the rest continues on the
    /// next line.</para>
    /// <code>
    /// 0x1A904  slti  at, a3, 45  →  slti  at, a3, 44    (strLen &gt; 44 ...)
    /// 0x1A918  addiu a3, zero, 44 → addiu a3, zero, 43  (... strLen = 43)
    /// 0x1AA14  bne   a0, a3, ...  → bne   v1, a3, ...   (i16 == strLen  →  i == strLen)
    /// </code>
    /// </remarks>
    public static readonly CodeFix SafeText = new(
        Id: "safe-text-v1",
        Name: "Safe text loading and drawing",
        Problem:
            "When loading texts, the game runs over twice the length of each text and may damage memory behind it. " +
            "When drawing, a line of 43 or more characters overflows its buffer and crashes the game.",
        Solution:
            "Loading stops exactly at the end of each text, and a drawn line can no longer leave its buffer: " +
            "overlong lines are cut instead of crashing the game.",
        Patches:
        [
            CodePatch.FromWords("text loader loop, textLoadBlock", 0xC95A8,
                [0xA4830000, 0x8FB80060, 0x26100001, 0x24A50002, 0x0218082B],
                [0xA4830000, 0x8FB80060, 0x26100002, 0x24A50002, 0x0218082B]),
            CodePatch.FromWords("line length limit, uvFontPrintStr16", 0x1A904,
                [0x28E1002D, 0xE4420008, 0x8D4E13BC, 0x14200002, 0xAC4E006C, 0x2407002C],
                [0x28E1002C, 0xE4420008, 0x8D4E13BC, 0x14200002, 0xAC4E006C, 0x2407002B]),
            CodePatch.FromWords("line end marker, uvFontPrintStr16", 0x1AA08,
                [0x0067082A, 0x5420FFD3, 0x000878C0, 0x14870008, 0x2416FFFF],
                [0x0067082A, 0x5420FFD3, 0x000878C0, 0x14670008, 0x2416FFFF]),
        ]);

    /// <summary>Address of the moved text name table (see <see cref="ExpandedTexts"/>).</summary>
    public const uint TextNameTableAddress = 0x801FE000;

    /// <summary>Address of the moved text data table (see <see cref="ExpandedTexts"/>).</summary>
    public const uint TextDataTableAddress = 0x801FF000;

    /// <summary>Number of texts the game can hold with <see cref="ExpandedTexts"/> (439 without it).</summary>
    public const int ExpandedTextCapacity = 1024;

    /// <summary>
    /// Room for 1024 texts, and four more corrections of the text code (US version).
    /// </summary>
    /// <remarks>
    /// <para><b>Text tables</b> (textLoadBlock, textGetDataByName, textGetDataByIdx in
    /// src/app/text_data.c): the game keeps one pointer per text name and per text in two arrays
    /// of exactly 440 and 439 entries, without any bounds check. The retail game fills them
    /// completely, so one more text overwrites the counters behind them and crashes the game.
    /// The fix moves both arrays to 0x801FE000 and 0x801FF000, with room for 1024 entries each
    /// (8 KiB). This memory lies directly in front of the program block (0x80200000), so the
    /// room behind the game's variables (0x803805E0) stays free for new program code. Two more
    /// changes keep the game from using the tables' memory otherwise: the memory manager treats
    /// it as part of the program block, which now starts at 0x801FE000 (uvMemInitBlocks), and the
    /// clearing of free memory at every level start ends in front of it (uvMemClearRegions). The
    /// tables therefore behave exactly like the original ones.</para>
    /// <para>The program block is enlarged instead of adding a block of its own on purpose: the
    /// memory manager only checks whether the start or the end of a new allocation lies inside a
    /// reserved block, so an allocation larger than a small block could cover it completely.</para>
    /// <para>Memory layout rule for future fixes: new data goes further down in front of
    /// 0x801FE000, new program code behind 0x803805E0.</para>
    /// <para><b>Missing texts</b> (textGetDataByIdx): for a text number that does not exist, the
    /// game returns NULL, and most callers crash on it. Now it returns an empty text instead. That
    /// text (a line break and the end code) is written over the start of a debug message
    /// ("Null Kanji string...") that the retail game never prints, because its print function
    /// is empty.</para>
    /// <para><b>Flight messages</b> (hudText_8031D8E0, hudWarningText in src/app/hud.c): the copy into
    /// the 60-value message buffers is meant to stop at the end code, but compares the signed value
    /// with 0xFFFF and never stops, reading up to 120 bytes past every text. The comparison now uses -1.</para>
    /// <para><b>ASCII strings</b> (uvFontPrintStr, src/kernel/font.c): the same overflow as in
    /// uvFontPrintStr16 for strings of 44 or more characters; they are now cut after 43.</para>
    /// <para><b>Numbers in texts</b> (textFmtIntAt, src/app/text_data.c): the game writes numbers into
    /// texts at fixed places, e.g. the track number of "Sound Track". A number with more digits
    /// than its slot moves the write position in front of the slot, possibly in front of the text.
    /// Now the number is cut to the size of its slot instead (the last digits are shown).</para>
    /// </remarks>
    public static readonly CodeFix ExpandedTexts = new(
        Id: "expanded-texts-v1",
        Name: "Room for 1024 texts and safer text handling",
        Problem:
            "The game has room for exactly its 439 texts; one more crashes it. Missing texts, long flight messages " +
            "and numbers that are too big for their place in a text can also crash it or damage memory.",
        Solution:
            "The text tables move to a reserved memory area with room for 1024 texts. Missing texts show up empty, " +
            "message copies stop at the end of the text, and lines and numbers are cut instead of overflowing.",
        Patches:
        [
            // 1. Reserve 8 KiB in front of the program block: block start 0x80200000 -> 0x801FE000 (uvMemInitBlocks).
            //    The start needs two instructions (lui + addiu). The second one takes the place of "lui $t3, 0x8000",
            //    whose only use is the start of block 5 (exception vectors, 0x80000000-0x80000400). Block 5 now
            //    starts at 0 instead: the heap only uses addresses from 0x80000000 on, so it protects the same memory.
            CodePatch.FromWords("memory block list, uvMemInitBlocks", 0x2B3AC,
                [0x3C098020, 0x254A05E0, 0x3C0B8000, 0xAC4E0000, 0xAC440004, 0xAC440008, 0xAC4F000C, 0xAC580010,
                    0xAC590014, 0xAC450018, 0xAC48001C, 0xAC490020, 0xAC4A0024, 0xAC4B0028, 0xAC45002C],
                [0x3C098020, 0x254A05E0, 0x2529E000, 0xAC4E0000, 0xAC440004, 0xAC440008, 0xAC4F000C, 0xAC580010,
                    0xAC590014, 0xAC450018, 0xAC48001C, 0xAC490020, 0xAC4A0024, 0xAC400028, 0xAC45002C]),
            // ... and do not clear it at every level start (the second loop of uvMemClearRegions ends in front of it;
            //     it ended at 0x802000A0, the start of the kernel code).
            CodePatch.FromWords("free memory clearing, uvMemClearRegions", 0x2B44C,
                [0x3C038020, 0x246300A0, 0x3C028012, 0x10600005, 0x34425800],
                [0x3C038020, 0x2463E000, 0x3C028012, 0x10600005, 0x34425800]),
            // 2. Text tables: names 0x80377F10 -> 0x801FE000, data 0x803785F0 -> 0x801FF000.
            CodePatch.FromWords("text data table, textLoadBlock", 0xC94B8,
                [0x3C128038, 0x265285F0, 0x37DE5441],
                [0x3C128020, 0x2652F000, 0x37DE5441]),
            CodePatch.FromWords("text name table, textLoadBlock", 0xC9504,
                [0x3C018037, 0x00402025, 0x000E7880, 0x002F0821, 0xAC227F10],
                [0x3C018020, 0x00402025, 0x000E7880, 0x002F0821, 0xAC22E000]),
            CodePatch.FromWords("text name table, textGetDataByName", 0xC965C,
                [0x3C118037, 0x26317F10, 0x00009025],
                [0x3C118020, 0x2631E000, 0x00009025]),
            CodePatch.FromWords("text data table, textGetDataByName", 0xC9678,
                [0x3C0F8038, 0x01F27821, 0x8DEF85F0],
                [0x3C0F8020, 0x01F27821, 0x8DEFF000]),
            // 3. textGetDataByIdx: new table, and an empty text instead of NULL for unknown numbers.
            CodePatch.FromWords("text lookup by number, textGetDataByIdx", 0xC96D8,
                [0x3C028038, 0x008E082A, 0x10200003, 0x004F1021, 0x03E00008, 0x8C4285F0, 0x00001025, 0x03E00008, 0x00000000],
                [0x3C028020, 0x008E082A, 0x10200003, 0x004F1021, 0x03E00008, 0x8C42F000, 0x3C028035, 0x03E00008, 0x24425C60]),
            // The empty text at 0x80355C60 (ROM 0xDD190): line break, end code. Was "Null" of an unused debug message.
            CodePatch.FromWords("empty text, unused debug message", 0xDD190,
                [0x4E756C6C, 0x204B616E, 0x6A692073],
                [0x0FFEFFFF, 0x204B616E, 0x6A692073]),
            // 4. Flight messages: stop the copy at the end code (compare with -1 instead of 0xFFFF).
            CodePatch.FromWords("message copy, hudText_8031D8E0", 0xA4E90,
                [0x00402025, 0x3405FFFF, 0x84990000, 0x24630002, 0xA4790BCE],
                [0x00402025, 0x2405FFFF, 0x84990000, 0x24630002, 0xA4790BCE]),
            CodePatch.FromWords("message copy, hudWarningText", 0xA4F68,
                [0x00402025, 0x3405FFFF, 0x84990000, 0x24630002, 0xA4790B3E],
                [0x00402025, 0x2405FFFF, 0x84990000, 0x24630002, 0xA4790B3E]),
            // 5. ASCII strings: at most 43 characters (uvFontPrintStr).
            CodePatch.FromWords("ASCII line limit, uvFontPrintStr", 0x1AB6C,
                [0x2841002D, 0x14200003, 0x0040B825, 0xA2C0002C, 0x2417002C],
                [0x2841002C, 0x14200003, 0x0040B825, 0xA2C0002B, 0x2417002B]),
            // 6. Numbers in texts: digits = min(digits, length) (textFmtIntAt, first 10 instructions rewritten).
            CodePatch.FromWords("number digits, textFmtIntAt", 0xC9934,
                [0x28A10064, 0x14200003, 0x00077040, 0x10000006, 0x24030003, 0x28A1000A, 0x14200003, 0x24030001, 0x10000001, 0x24030002],
                [0x00077040, 0x28A1000A, 0x38210001, 0x24230001, 0x28A10064, 0x38210001, 0x00611821, 0x00C3082A, 0x54200001, 0x00C01825]),
        ]);

    /// <summary>
    /// ROM offset of the code cave: the body of uvMemScanBlocks (kernel, 0x8022A4F4), 0x100 bytes up to
    /// ROM 0x2B5A4. The function only checks the memory block list and reports overlaps through
    /// _uvDebugPrintf, which is empty in the retail game; so it does nothing visible and may return at once.
    /// </summary>
    public const int CodeCaveStart = 0x2B4A4;

    /// <summary>
    /// Three bugs of the photo album (snap.c, US version).
    /// </summary>
    /// <remarks>
    /// <para><b>Saving photos</b> (func_8033E860 / func_8033F050): every photo is packed into a 24-byte
    /// record and copied bit by bit (172 bits) into the saved game, bit n of a byte being 1 &lt;&lt; n. The
    /// record's last 4 used bits, however, are the upper 4 bits of byte 21 (big-endian bit fields): the
    /// last bit of the 5th object's value and the 3 bits of the 6th object's value. The copy takes the
    /// lower 4 bits of byte 21 instead, which hold nothing. So these values are lost when saving, and
    /// when loading they come from uninitialised stack memory; they then index small tables
    /// unchecked. Fix: the two calls that read and write the record go through two small helpers in the
    /// code cave, which shift bit numbers 168–171 to 172–175. Saved games stay compatible: all other bits
    /// keep their place, and the 4 bits moved held nothing useful before.</para>
    /// <para><b>Photos without objects</b> (func_8033DCD0): when a photo is copied, the object count is
    /// only copied if it is not 0, so an empty photo keeps the count of the photo that was in that slot
    /// before. A "branch likely" becomes a normal branch, so the store in its delay slot (the count)
    /// always runs.</para>
    /// <para><b>Resetting the album</b> (func_80337D50): the function resets the photos of D_80373060 but
    /// clears the object list of the other photo array, D_80373390. It now clears the object list of
    /// D_80373060.</para>
    /// </remarks>
    public static readonly CodeFix PhotoAlbum = new(
        Id: "photo-album-v1",
        Name: "Correct saving of photos",
        Problem:
            "Photos with five or more objects lose data when saved; after loading they show wrong objects and can crash " +
            "the game. An empty photo also takes over the object count of the photo that was in its slot before.",
        Solution:
            "All bits of a photo are saved, empty photos get the count 0, and resetting the album clears the right list. " +
            "Saved games of the original game can still be loaded.",
        Patches:
        [
            // 1. Code cave (uvMemScanBlocks): return at once, then two helpers. Each moves bit numbers >= 168 by 4
            //    and jumps on to the original bit function:  a1 += (a1 >= 168) * 4.
            CodePatch.FromWords("photo bit helpers, code cave in uvMemScanBlocks", CodeCaveStart,
                [0x27BDFFC8, 0x3C02802C, 0x8C428820, 0xAFB30028, 0xAFBF0034, 0xAFB50030,
                    0xAFB4002C, 0xAFB20024, 0xAFB10020, 0xAFB0001C, 0x1840002C, 0x00009825],
                [
                    0x03E00008, 0x00000000,                                     // jr ra; nop (uvMemScanBlocks)
                    0x28A100A8, 0x38210001, 0x00010880, 0x080CFA04, 0x00A12821, // 0x8022A4FC: get bit -> func_8033E810
                    0x28A100A8, 0x38210001, 0x00010880, 0x080CF9E1, 0x00A12821, // 0x8022A510: set bit -> func_8033E784
                ]),
            // 2. Saving (func_8033E860): read the photo record through the helper.
            CodePatch.FromWords("photo saving, func_8033E860", 0xC6510,
                [0x02202025, 0x0C0CFA04, 0x02002825, 0x8EE40000, 0x02A02825, 0x0C0CF9E1],
                [0x02202025, 0x0C08A93F, 0x02002825, 0x8EE40000, 0x02A02825, 0x0C0CF9E1]),
            // 3. Loading (func_8033F050): write the photo record through the helper.
            CodePatch.FromWords("photo loading, func_8033F050", 0xC6634,
                [0x02202025, 0x02002825, 0x0C0CF9E1, 0x00403025, 0x26100001, 0x2A0100AC],
                [0x02202025, 0x02002825, 0x0C08A944, 0x00403025, 0x26100001, 0x2A0100AC]),
            // 4. Empty photos (func_8033DCD0): bnel -> bne, so "sb v0, 0x42(a0)" also runs for a count of 0.
            CodePatch.FromWords("photo copy, func_8033DCD0", 0xC528C,
                [0x90A20042, 0x54400004, 0xA0820042, 0x10000018],
                [0x90A20042, 0x14400004, 0xA0820042, 0x10000018]),
            // 5. Album reset (func_80337D50): object list of D_80373060 (0x80373060-0x80373300) instead of D_80373390.
            CodePatch.FromWords("album reset, func_80337D50", 0xBF280,
                [0x3C0E8037, 0x25C63390, 0x3C038037, 0x3C088037, 0x25083630],
                [0x3C0E8037, 0x25C63060, 0x3C038037, 0x3C088037, 0x25083300]),
        ]);

    /// <summary>All known fixes.</summary>
    public static IReadOnlyList<CodeFix> All { get; } = [SafeText, ExpandedTexts, PhotoAlbum];

    /// <summary>The ids of all known fixes (what new projects get).</summary>
    public static IReadOnlyList<string> AllIds { get; } = All.Select(f => f.Id).ToList();

    /// <summary>Finds a fix by its id, or null if this editor does not know it.</summary>
    public static CodeFix? Find(string id) => All.FirstOrDefault(f => f.Id == id);

    /// <summary>End of the game code for a ROM layout (the file table follows it).</summary>
    public static int CodeEnd(RomLayout layout) => layout.FileTableOffset;
}
