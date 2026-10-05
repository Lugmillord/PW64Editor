namespace PW64Editor.Core.Verification;

/// <summary>
/// The result category of checking a loaded ROM against the known clean dumps.
/// </summary>
public enum RomVerificationStatus
{
    /// <summary>The ROM is a byte-exact clean dump of a supported release. Ready to use.</summary>
    CleanSupported,

    /// <summary>The ROM is a clean dump, but of a release the editor does not support yet.</summary>
    CleanUnsupported,

    /// <summary>
    /// The header says Pilotwings 64, but the hash does not match any clean dump.
    /// Usually this means the ROM was already modified (patched), or it is a bad/over-dump.
    /// </summary>
    ModifiedOrBadDump,

    /// <summary>The header says Pilotwings 64, but for a region/version we do not know.</summary>
    UnknownRelease,

    /// <summary>The ROM is not Pilotwings 64 at all.</summary>
    NotPilotwings,
}
