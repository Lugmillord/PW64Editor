using PW64Editor.Core.Localization;
using PW64Editor.Core.Rom;

namespace PW64Editor.Core.Verification;

/// <summary>
/// Checks whether a loaded ROM is a clean, unmodified dump of a supported Pilotwings 64 release.
/// </summary>
/// <remarks>
/// Why this matters: BPS patches are created against one exact base ROM. If a user starts a
/// project from a modified or bad dump, the resulting patch will not apply for anyone else.
/// The hash is computed over the big-endian data, so .v64/.n64 files are verified correctly
/// as long as they were normalized on load (which <see cref="N64Rom"/> always does).
/// </remarks>
public static class RomVerifier
{
    public static RomVerificationResult Verify(N64Rom rom)
    {
        ArgumentNullException.ThrowIfNull(rom);

        string sha1 = rom.ComputeSha1();
        RomHeader header = rom.Header;

        // Case 1: exact hash match with a known clean dump.
        KnownRom? match = KnownRoms.FindBySha1(sha1);
        if (match is not null)
        {
            return match.IsSupported
                ? new RomVerificationResult(
                    RomVerificationStatus.CleanSupported, sha1, match,
                    CoreText.F("Clean dump of {0} (v{1}). Ready to use.", match.Name, header.VersionString))
                : new RomVerificationResult(
                    RomVerificationStatus.CleanUnsupported, sha1, match,
                    CoreText.F("Clean dump of {0}, but this release is not supported yet. Please use {1}.",
                        match.Name, KnownRoms.PilotwingsUsa.Name));
        }

        // Case 2: no hash match. Use the header to find out what went wrong.
        if (header.CartridgeId != KnownRoms.PilotwingsCartridgeId)
        {
            return new RomVerificationResult(
                RomVerificationStatus.NotPilotwings, sha1, null,
                CoreText.F("This is not Pilotwings 64 (game code {0}, name \"{1}\").",
                    header.GameCode, header.InternalName));
        }

        // The header claims Pilotwings 64. Is it the supported release?
        KnownRom expected = KnownRoms.PilotwingsUsa;
        if (header.GameCode == expected.GameCode && header.Version == expected.Version)
        {
            string sizeHint = rom.Size == expected.Size
                ? string.Empty
                : CoreText.F(" The file size is also wrong ({0:N0} bytes instead of {1:N0}), which suggests a bad " +
                    "dump or an already expanded ROM.", rom.Size, expected.Size);

            return new RomVerificationResult(
                RomVerificationStatus.ModifiedOrBadDump, sha1, null,
                CoreText.F("The header says {0}, but the SHA-1 does not match the clean dump. The ROM was probably " +
                    "already modified or is a bad dump.{1} Please use an unmodified dump of your cartridge.",
                    expected.Name, sizeHint));
        }

        return new RomVerificationResult(
            RomVerificationStatus.UnknownRelease, sha1, null,
            CoreText.F("This is Pilotwings 64 ({0}, game code {1}, v{2}), but this release is not supported. Please " +
                "use {3}.", header.RegionName, header.GameCode, header.VersionString, expected.Name));
    }
}
