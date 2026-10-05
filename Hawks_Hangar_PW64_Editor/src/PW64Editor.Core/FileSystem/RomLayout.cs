namespace PW64Editor.Core.FileSystem;

/// <summary>
/// Fixed ROM addresses of one specific game release. The game's code contains these values
/// as constants, so they cannot change without patching code.
/// </summary>
/// <remarks>
/// Values come from the segment configuration of the Pilotwings 64 decompilation
/// (config/us/pilotwings64.us.yaml, https://github.com/gcsmith/Pilotwings64Decomp, MIT license).
/// </remarks>
/// <param name="FileTableOffset">ROM offset of the "UVRM" FORM holding the compressed file table.</param>
/// <param name="FileSystemOffset">ROM offset of the first game file. All other file offsets are
/// computed by adding up the sizes from the file table.</param>
/// <param name="FileSystemLimit">First ROM offset after the file system that is used by something
/// else (the audio data). The file system must end at or before this address.</param>
public sealed record RomLayout(int FileTableOffset, int FileSystemOffset, int FileSystemLimit)
{
    /// <summary>
    /// Layout of Pilotwings 64 (USA).
    /// Audio data follows the file system: sequences at 0x618B70, instrument bank at 0x62D460,
    /// samples at 0x6314D0. All three addresses are hardcoded in the audio code.
    /// </summary>
    public static readonly RomLayout PilotwingsUsa = new(
        FileTableOffset: 0xDE720,
        FileSystemOffset: 0xDF5B0,
        FileSystemLimit: 0x618B70);

    /// <summary>Bytes available for game files without moving the audio data.</summary>
    public int FileSystemCapacity => FileSystemLimit - FileSystemOffset;
}
