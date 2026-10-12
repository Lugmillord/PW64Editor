using PW64Editor.Core.Code;

namespace PW64Editor.Core.Build;

/// <summary>
/// Settings for <see cref="RomBuilder.Build"/>.
/// </summary>
/// <param name="AlwaysRelocateAudio">If true, the audio data is always placed directly after the
/// file system. If false (default), it is only moved when the files no longer fit in front of it,
/// so small hacks keep the original layout and produce the smallest possible patches.</param>
/// <param name="AllowExpansion">If true, the ROM is enlarged (16, 32 or 64 MiB) when the data does
/// not fit into the current size. If false (default), the build fails instead.</param>
/// <param name="CodeFixes">Corrections of the game's code to apply (see <see cref="Code.CodeFixes"/>).
/// None by default, so a plain rebuild stays byte-identical to the base ROM.</param>
public sealed record RomBuildOptions(bool AlwaysRelocateAudio = false, bool AllowExpansion = false, IReadOnlyList<CodeFix>? CodeFixes = null)
{
    /// <summary>
    /// A new music file (all sequences, see <see cref="Audio.SequenceBank"/>), or null to keep the
    /// original. If it is larger than the original, the instrument bank behind it moves.
    /// </summary>
    public byte[]? SequenceFile { get; init; }

    /// <summary>
    /// Changed bytes of tables in the game code (for example the result rows of the missions),
    /// written after the code fixes. Each patch must lie inside the game code.
    /// </summary>
    public IReadOnlyList<DataPatch> DataPatches { get; init; } = [];

    /// <summary>The default settings.</summary>
    public static readonly RomBuildOptions Default = new();
}

/// <summary>Bytes to write at a ROM offset inside the game code.</summary>
/// <param name="Offset">ROM offset.</param>
/// <param name="Data">The new bytes.</param>
/// <param name="Description">What is changed, for error messages.</param>
public sealed record DataPatch(int Offset, byte[] Data, string Description);
