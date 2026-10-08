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
    /// <summary>The default settings.</summary>
    public static readonly RomBuildOptions Default = new();
}
