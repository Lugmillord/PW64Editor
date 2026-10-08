using PW64Editor.Core.FileSystem;

namespace PW64Editor.Core.Code;

/// <summary>
/// All corrections of bugs in the game's code that the editor knows, in the order they are shown.
/// </summary>
/// <remarks>
/// <para>
/// Fixes only exchange existing instructions. They need no extra room in the ROM or in memory, and
/// they do not depend on where data lies, so they keep working when the editor moves game files or
/// the audio data. New fixes are added at the end of <see cref="All"/>; the <see cref="CodeFix.Id"/>
/// of a released fix never changes, because projects store it.
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

    /// <summary>All known fixes.</summary>
    public static IReadOnlyList<CodeFix> All { get; } = [SafeText];

    /// <summary>The ids of all known fixes (what new projects get).</summary>
    public static IReadOnlyList<string> AllIds { get; } = All.Select(f => f.Id).ToList();

    /// <summary>Finds a fix by its id, or null if this editor does not know it.</summary>
    public static CodeFix? Find(string id) => All.FirstOrDefault(f => f.Id == id);

    /// <summary>End of the game code for a ROM layout (the file table follows it).</summary>
    public static int CodeEnd(RomLayout layout) => layout.FileTableOffset;
}
