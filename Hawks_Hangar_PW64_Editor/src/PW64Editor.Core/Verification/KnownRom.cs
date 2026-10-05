namespace PW64Editor.Core.Verification;

/// <summary>
/// A known retail release of Pilotwings 64, identified by the hash of its clean dump.
/// </summary>
/// <param name="Name">Name in No-Intro style, e.g. "Pilotwings 64 (USA)".</param>
/// <param name="GameCode">Four-character game code from the ROM header, e.g. "NPWE".</param>
/// <param name="Version">ROM version byte from the header.</param>
/// <param name="Size">Expected file size in bytes.</param>
/// <param name="Sha1">SHA-1 of the clean big-endian (.z64) dump, lowercase hex.</param>
/// <param name="IsSupported">Whether the editor can work with this release.</param>
public sealed record KnownRom(
    string Name,
    string GameCode,
    byte Version,
    int Size,
    string Sha1,
    bool IsSupported);

/// <summary>
/// The list of releases the editor knows about.
/// </summary>
/// <remarks>
/// Only the US release is supported for now, because all research and the decompilation
/// project are based on it. Other releases are recognized by their game code
/// (see <see cref="RomVerifier"/>) so we can give the user a helpful message.
/// </remarks>
public static class KnownRoms
{
    /// <summary>Pilotwings 64 (USA), the base ROM for all hacks made with this editor.</summary>
    public static readonly KnownRom PilotwingsUsa = new(
        Name: "Pilotwings 64 (USA)",
        GameCode: "NPWE",
        Version: 0,
        Size: 8 * 1024 * 1024,
        Sha1: "ec771aedf54ee1b214c25404fb4ec51cfd43191a",
        IsSupported: true);

    /// <summary>All releases identified by hash.</summary>
    public static IReadOnlyList<KnownRom> All { get; } = [PilotwingsUsa];

    /// <summary>
    /// The cartridge ID shared by every Pilotwings 64 release ("PW" in game code "NPWx").
    /// </summary>
    public const string PilotwingsCartridgeId = "PW";

    /// <summary>Finds a known release by its SHA-1 hash, or returns <c>null</c>.</summary>
    public static KnownRom? FindBySha1(string sha1)
    {
        return All.FirstOrDefault(rom => string.Equals(rom.Sha1, sha1, StringComparison.OrdinalIgnoreCase));
    }
}
