using PW64Editor.Core.Boot;
using PW64Editor.Core.Rom;

namespace PW64Editor.Core.Build;

/// <summary>
/// The outcome of <see cref="RomBuilder.Build"/>.
/// </summary>
/// <param name="Rom">The newly built ROM (boot checksum already updated).</param>
/// <param name="TableRewritten">True if the file table had to be recompressed. False means the
/// original table bytes were kept, because no file changed its type or size.</param>
/// <param name="FileSystemEnd">ROM offset right after the last game file.</param>
/// <param name="FreeSpace">Bytes left before the file system would run into the audio data.</param>
/// <param name="Cic">The detected CIC type. If <see cref="CicType.Unknown"/>, the boot checksum
/// could not be updated and the ROM may not boot on real hardware.</param>
public sealed record RomBuildResult(N64Rom Rom, bool TableRewritten, int FileSystemEnd, int FreeSpace, CicType Cic)
{
    /// <summary>True if the boot checksum in the header was recomputed for the new data.</summary>
    public bool BootChecksumUpdated => Cic != CicType.Unknown;
}
