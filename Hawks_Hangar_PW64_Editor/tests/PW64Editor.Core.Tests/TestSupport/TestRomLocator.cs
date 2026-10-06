using PW64Editor.Core.Rom;
using PW64Editor.Core.Verification;

namespace PW64Editor.Core.Tests.TestSupport;

/// <summary>
/// Finds the clean Pilotwings 64 (USA) ROM for integration tests.
/// </summary>
/// <remarks>
/// The ROM is copyrighted and must never be committed, so tests look for it in two places:
/// <list type="number">
///   <item>The path in the environment variable <c>PW64_ROM_PATH</c>.</item>
///   <item>All .z64/.v64/.n64 files in a folder named <c>roms</c> in the repository
///         (that folder is excluded by .gitignore).</item>
/// </list>
/// <para>
/// Every candidate is verified by its SHA-1, and only the clean US ROM is accepted. Other ROMs
/// (for example modified test builds) may be stored in the same folder; they are ignored.
/// If no clean ROM is found, tests marked with <see cref="RealRomFactAttribute"/> are skipped.
/// </para>
/// </remarks>
internal static class TestRomLocator
{
    private const string EnvironmentVariable = "PW64_ROM_PATH";
    private const string RomFolderName = "roms";
    private static readonly string[] RomExtensions = [".z64", ".v64", ".n64"];

    /// <summary>The path of the clean ROM, or <c>null</c> if none was found. Computed once.</summary>
    public static string? RomPath { get; } = GetCandidates().FirstOrDefault(IsCleanUsaRom);

    private static IEnumerable<string> GetCandidates()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
        {
            yield return fromEnvironment;
        }

        // Walk up from the test binary folder (e.g. tests/.../bin/Debug/net10.0)
        // and check every "roms" folder on the way up to the drive root.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string folder = Path.Combine(dir.FullName, RomFolderName);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            // Sorted, so the result does not depend on the order the file system lists files in.
            IEnumerable<string> roms = Directory.EnumerateFiles(folder)
                .Where(f => RomExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (string rom in roms)
            {
                yield return rom;
            }
        }
    }

    private static bool IsCleanUsaRom(string path)
    {
        try
        {
            // Skip files of the wrong size without reading them (e.g. expanded 16 MiB builds).
            if (new FileInfo(path).Length != KnownRoms.PilotwingsUsa.Size)
            {
                return false;
            }

            return N64Rom.Load(path).ComputeSha1() == KnownRoms.PilotwingsUsa.Sha1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidRomException)
        {
            return false;
        }
    }
}

/// <summary>
/// Like [Fact], but automatically skips the test if no clean ROM is available.
/// This keeps the test suite green on machines (and CI servers) without the game.
/// </summary>
public sealed class RealRomFactAttribute : FactAttribute
{
    public RealRomFactAttribute()
    {
        if (TestRomLocator.RomPath is null)
        {
            Skip = "No clean Pilotwings 64 (USA) ROM found. Put one into the 'roms' folder or set PW64_ROM_PATH.";
        }
    }
}
