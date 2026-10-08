namespace PW64Editor.Core.Code;

/// <summary>
/// One change to the game's machine code: a short piece of original code and what replaces it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Original"/> and <see cref="Replacement"/> have the same length and usually contain a
/// few unchanged instructions around the changed ones. That makes the piece of code unique, so it
/// can be found again even if it is not at <see cref="RomOffset"/> (for example in a ROM where code
/// was moved by a future version of the editor). Before anything is written, the builder checks
/// that the original code is really there; it never overwrites code it does not recognize.
/// </para>
/// </remarks>
/// <param name="Description">What this piece of code is, for error messages.</param>
/// <param name="RomOffset">Where the code lies in the retail ROM.</param>
/// <param name="Original">The original bytes.</param>
/// <param name="Replacement">The new bytes (same length).</param>
public sealed record CodePatch(string Description, int RomOffset, byte[] Original, byte[] Replacement)
{
    /// <summary>Creates a patch from 32-bit instruction words.</summary>
    public static CodePatch FromWords(string description, int romOffset, uint[] original, uint[] replacement)
    {
        if (original.Length != replacement.Length)
        {
            throw new ArgumentException("Original and replacement must have the same number of instructions.");
        }

        return new CodePatch(description, romOffset, ToBytes(original), ToBytes(replacement));
    }

    private static byte[] ToBytes(uint[] words)
    {
        byte[] bytes = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), words[i]);
        }

        return bytes;
    }
}

/// <summary>How far a code fix is present in a ROM.</summary>
public enum CodeFixState
{
    /// <summary>All pieces of code are original: the fix can be applied.</summary>
    NotApplied,

    /// <summary>All pieces of code are already replaced.</summary>
    Applied,

    /// <summary>Some pieces are replaced, others are original (the fix can be completed).</summary>
    PartlyApplied,

    /// <summary>At least one piece of code is neither original nor replaced: the fix cannot be used.</summary>
    Unrecognized,
}

/// <summary>
/// A correction of a bug in the game's code, made of one or more <see cref="CodePatch"/>es that
/// are always applied together.
/// </summary>
/// <param name="Id">Stable identifier, stored in project.json. Never change it once released.</param>
/// <param name="Name">Short name for the user.</param>
/// <param name="Problem">What goes wrong in the original game (one or two sentences).</param>
/// <param name="Solution">How the fix corrects it (one or two sentences).</param>
/// <param name="Patches">The code changes.</param>
public sealed record CodeFix(string Id, string Name, string Problem, string Solution, IReadOnlyList<CodePatch> Patches)
{
    /// <summary>
    /// Checks whether the fix is present in a ROM.
    /// </summary>
    /// <param name="rom">The ROM data.</param>
    /// <param name="codeStart">First ROM offset of the game code (searched if code is not at its usual place).</param>
    /// <param name="codeEnd">End of the game code.</param>
    public CodeFixState GetState(ReadOnlySpan<byte> rom, int codeStart, int codeEnd)
    {
        int original = 0, replaced = 0;
        foreach (CodePatch patch in Patches)
        {
            switch (Locate(rom, patch, codeStart, codeEnd).State)
            {
                case PatchState.Original:
                    original++;
                    break;
                case PatchState.Replaced:
                    replaced++;
                    break;
                default:
                    return CodeFixState.Unrecognized;
            }
        }

        return replaced == 0 ? CodeFixState.NotApplied
            : original == 0 ? CodeFixState.Applied
            : CodeFixState.PartlyApplied;
    }

    /// <summary>
    /// Applies the fix. Pieces that are already replaced stay as they are.
    /// </summary>
    /// <exception cref="InvalidDataException">A piece of code was not found: the ROM is not the
    /// game version the fix was made for. Nothing is changed in that case.</exception>
    public void Apply(Span<byte> rom, int codeStart, int codeEnd)
    {
        // Check everything first, so a ROM is never left half-patched.
        var places = new List<(CodePatch Patch, int Offset)>();
        foreach (CodePatch patch in Patches)
        {
            (PatchState state, int offset) = Locate(rom, patch, codeStart, codeEnd);
            switch (state)
            {
                case PatchState.Original:
                    places.Add((patch, offset));
                    break;
                case PatchState.Replaced:
                    break;
                default:
                    throw new InvalidDataException(
                        $"The code fix \"{Name}\" cannot be applied: the expected code ({patch.Description}) was not found. " +
                        "The ROM is not the game version this fix was made for.");
            }
        }

        foreach ((CodePatch patch, int offset) in places)
        {
            patch.Replacement.CopyTo(rom[offset..]);
        }
    }

    private enum PatchState
    {
        Original,
        Replaced,
        NotFound,
    }

    /// <summary>
    /// Finds a piece of code: first at its usual offset, then anywhere in the code (only if it
    /// occurs exactly once, so nothing is ever patched at a wrong place).
    /// </summary>
    private static (PatchState State, int Offset) Locate(ReadOnlySpan<byte> rom, CodePatch patch, int codeStart, int codeEnd)
    {
        int length = patch.Original.Length;
        if (patch.RomOffset >= 0 && patch.RomOffset + length <= rom.Length)
        {
            ReadOnlySpan<byte> atOffset = rom.Slice(patch.RomOffset, length);
            if (atOffset.SequenceEqual(patch.Original))
            {
                return (PatchState.Original, patch.RomOffset);
            }

            if (atOffset.SequenceEqual(patch.Replacement))
            {
                return (PatchState.Replaced, patch.RomOffset);
            }
        }

        int start = Math.Clamp(codeStart, 0, rom.Length);
        int end = Math.Clamp(codeEnd, start, rom.Length);
        ReadOnlySpan<byte> code = rom[start..end];

        if (FindUnique(code, patch.Original) is { } original)
        {
            return (PatchState.Original, start + original);
        }

        if (FindUnique(code, patch.Replacement) is { } replaced)
        {
            return (PatchState.Replaced, start + replaced);
        }

        return (PatchState.NotFound, -1);
    }

    /// <summary>Position of the only occurrence (on a 4-byte boundary, like all MIPS code), or null.</summary>
    private static int? FindUnique(ReadOnlySpan<byte> code, ReadOnlySpan<byte> pattern)
    {
        int? found = null;
        int from = 0;
        while (from <= code.Length - pattern.Length)
        {
            int index = code[from..].IndexOf(pattern);
            if (index < 0)
            {
                break;
            }

            int position = from + index;
            if (position % 4 == 0)
            {
                if (found is not null)
                {
                    return null; // more than once: ambiguous
                }

                found = position;
            }

            from = position + 1;
        }

        return found;
    }
}
