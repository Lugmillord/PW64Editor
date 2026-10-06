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
/// <param name="AudioOffset">Where the audio data starts in the new ROM.</param>
/// <param name="AudioRelocated">True if the audio data was moved away from its original address.</param>
/// <param name="FreeSpace">How many more bytes of game files would fit into this ROM size
/// (moving the audio if necessary, but without expanding the ROM).</param>
/// <param name="Cic">The detected CIC type. If <see cref="CicType.Unknown"/>, the boot checksum
/// could not be updated and the ROM may not boot on real hardware.</param>
public sealed record RomBuildResult(
    N64Rom Rom,
    bool TableRewritten,
    int FileSystemEnd,
    int AudioOffset,
    bool AudioRelocated,
    int FreeSpace,
    CicType Cic)
{
    /// <summary>True if the boot checksum in the header was recomputed for the new data.</summary>
    public bool BootChecksumUpdated => Cic != CicType.Unknown;

    /// <summary>True if the ROM is larger than the base ROM.</summary>
    public bool Expanded { get; init; }
}
