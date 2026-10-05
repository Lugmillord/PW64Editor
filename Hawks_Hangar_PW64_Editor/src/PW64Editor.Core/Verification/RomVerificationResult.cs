namespace PW64Editor.Core.Verification;

/// <summary>
/// The outcome of <see cref="RomVerifier.Verify"/>.
/// </summary>
/// <param name="Status">The overall verdict.</param>
/// <param name="Sha1">The computed SHA-1 of the ROM (big-endian data).</param>
/// <param name="MatchedRom">The known release that matched by hash, if any.</param>
/// <param name="Message">A human readable explanation (English) suitable for the UI or console.</param>
public sealed record RomVerificationResult(
    RomVerificationStatus Status,
    string Sha1,
    KnownRom? MatchedRom,
    string Message)
{
    /// <summary>
    /// True if the ROM can be used as the base for a hack project.
    /// </summary>
    public bool IsUsableAsBase => Status == RomVerificationStatus.CleanSupported;
}
