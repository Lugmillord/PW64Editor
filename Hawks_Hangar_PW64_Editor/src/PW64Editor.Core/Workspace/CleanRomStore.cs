using PW64Editor.Core.Rom;
using PW64Editor.Core.Verification;

namespace PW64Editor.Core.Workspace;

/// <summary>The outcome of <see cref="CleanRomStore.Import"/>.</summary>
/// <param name="Success">True if the ROM was accepted and copied.</param>
/// <param name="Message">Explanation for the user (English).</param>
public sealed record CleanRomImportResult(bool Success, string Message);

/// <summary>
/// Manages the editor's private copy of the clean ROM.
/// </summary>
/// <remarks>
/// <para>
/// Keeping a private copy makes the editor independent of the user's file: it may be moved,
/// renamed or even modified by another tool, and every build still starts from a verified
/// clean ROM. The copy is always stored in big-endian (.z64) order, whatever the user picked.
/// </para>
/// <para>
/// The copy is verified by its SHA-1 every time it is loaded, so a damaged copy is detected
/// instead of silently producing broken builds.
/// </para>
/// </remarks>
public sealed class CleanRomStore
{
    private readonly EditorStorage _storage;

    public CleanRomStore(EditorStorage storage)
    {
        _storage = storage;
    }

    /// <summary>Path of the private copy (it may not exist yet).</summary>
    public string RomPath => _storage.CleanRomPath;

    /// <summary>
    /// Checks a ROM chosen by the user and, if it is the clean supported ROM, stores a copy.
    /// An existing copy is only replaced after the new ROM has been verified.
    /// </summary>
    public CleanRomImportResult Import(string sourcePath)
    {
        N64Rom rom;
        try
        {
            rom = N64Rom.Load(sourcePath);
        }
        catch (Exception ex) when (ex is InvalidRomException or IOException or UnauthorizedAccessException)
        {
            return new CleanRomImportResult(false, $"The file cannot be used: {ex.Message}");
        }

        RomVerificationResult verification = RomVerifier.Verify(rom);
        if (!verification.IsUsableAsBase)
        {
            return new CleanRomImportResult(false, verification.Message);
        }

        // Write to a temporary file first and then move it into place, so a crash or a full
        // disk in the middle of writing can never leave a half-written copy behind.
        Directory.CreateDirectory(Path.GetDirectoryName(RomPath)!);
        string temporary = RomPath + ".tmp";
        rom.Save(temporary);
        File.Move(temporary, RomPath, overwrite: true);

        string format = rom.OriginalByteOrder == RomByteOrder.BigEndian
            ? string.Empty
            : $" It was converted from {ByteOrderConverter.GetFileExtension(rom.OriginalByteOrder)} to .z64.";
        return new CleanRomImportResult(true, $"{verification.Message}{format}");
    }

    /// <summary>
    /// Loads and verifies the private copy.
    /// </summary>
    /// <param name="problem">Why no ROM could be returned, for display to the user.</param>
    /// <returns>The clean ROM, or <c>null</c> if it is missing or damaged.</returns>
    public N64Rom? TryLoad(out string? problem)
    {
        if (!File.Exists(RomPath))
        {
            problem = "The editor does not have a copy of the clean ROM yet.";
            return null;
        }

        try
        {
            N64Rom rom = N64Rom.Load(RomPath);
            if (rom.ComputeSha1() != KnownRoms.PilotwingsUsa.Sha1)
            {
                problem = "The editor's copy of the clean ROM is damaged. Please select the ROM again.";
                return null;
            }

            problem = null;
            return rom;
        }
        catch (Exception ex) when (ex is InvalidRomException or IOException or UnauthorizedAccessException)
        {
            problem = $"The editor's copy of the clean ROM cannot be read ({ex.Message}). Please select the ROM again.";
            return null;
        }
    }
}
