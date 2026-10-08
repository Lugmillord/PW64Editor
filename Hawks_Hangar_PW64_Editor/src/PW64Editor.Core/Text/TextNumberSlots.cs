namespace PW64Editor.Core.Text;

/// <summary>
/// Texts into which the game writes a number at a fixed position (textFmtIntAt in
/// src/app/text_data.c), for example the track number in "Sound Track".
/// </summary>
/// <remarks>
/// <para>
/// The game overwrites the characters at the slot with the digits, directly in the loaded text.
/// The slot must therefore lie on the first line, in front of any line break or [x=...]
/// position, and the text must be long enough. The positions below are those of the US version
/// (cannonball.c, falco.c, options.c, rings.c, targets.c).
/// </para>
/// </remarks>
public static class TextNumberSlots
{
    /// <summary>A number slot: the first value written and the number of digits.</summary>
    public sealed record Slot(int Offset, int Length);

    private static readonly Dictionary<string, Slot> Slots = new(StringComparer.Ordinal)
    {
        ["LEFT_CNT"] = new(0, 2),   // "12 to go" (rings and targets left)
        ["STRACK"] = new(13, 2),    // "Sound Track 12" (options)
        ["LEFT_SHT"] = new(14, 1),  // "Shots to go : 5" (Meca Hawk)
        ["C_POINTS"] = new(0, 2),   // "12 pts." (cannonball)
    };

    /// <summary>The number slot of a text, if the game writes a number into it.</summary>
    public static Slot? Find(string name) => Slots.GetValueOrDefault(name);

    /// <summary>A short description for the user, e.g. "characters 14-15".</summary>
    public static string Describe(Slot slot) =>
        slot.Length == 1 ? $"character {slot.Offset + 1}" : $"characters {slot.Offset + 1}-{slot.Offset + slot.Length}";

    /// <summary>
    /// Checks that the slot is usable: it must consist of ordinary characters on the first line.
    /// </summary>
    /// <param name="name">The text's name.</param>
    /// <param name="codes">The encoded text (with end code).</param>
    public static IEnumerable<TextIssue> Validate(string name, IReadOnlyList<ushort> codes)
    {
        if (Find(name) is not { } slot)
        {
            yield break;
        }

        int needed = slot.Offset + slot.Length;
        for (int i = 0; i < needed; i++)
        {
            ushort code = i < codes.Count ? codes[i] : TextCodec.CodeEnd;
            if (code is TextCodec.CodeEnd or TextCodec.CodeLineBreak or TextCodec.CodePosition)
            {
                yield return new TextIssue(true,
                    $"The game writes a number into {Describe(slot)} of this text. These must be ordinary characters " +
                    $"(spaces are fine) on the first line: keep at least {needed} characters in front of the first line " +
                    "break or [x=...].");
                yield break;
            }
        }
    }

    /// <summary>
    /// Writes an example number into the slot, as the game does (bold digits, glyph 0x60 + digit).
    /// Returns the codes unchanged if the text has no usable slot.
    /// </summary>
    public static IReadOnlyList<ushort> WithExampleNumber(string name, IReadOnlyList<ushort> codes)
    {
        if (Find(name) is not { } slot || Validate(name, codes).Any())
        {
            return codes;
        }

        ushort[] result = [.. codes];
        string digits = slot.Length == 1 ? "5" : "12";
        for (int i = 0; i < slot.Length; i++)
        {
            result[slot.Offset + i] = (ushort)(0x60 + digits[i] - '0');
        }

        return result;
    }
}
