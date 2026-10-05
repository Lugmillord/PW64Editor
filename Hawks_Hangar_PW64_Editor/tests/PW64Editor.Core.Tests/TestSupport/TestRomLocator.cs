namespace PW64Editor.Core.Tests.TestSupport;

/// <summary>
/// Finds a real Pilotwings 64 ROM for integration tests.
/// </summary>
/// <remarks>
/// The ROM is copyrighted and must never be committed, so tests look for it in one of two places:
/// <list type="number">
///   <item>The path in the environment variable <c>PW64_ROM_PATH</c>.</item>
///   <item>Any .z64/.v64/.n64 file in a folder named <c>roms</c> in the repository
///         (that folder is excluded by .gitignore).</item>
/// </list>
/// If no ROM is found, tests marked with <see cref="RealRomFactAttribute"/> are skipped.
/// </remarks>
internal static class TestRomLocator
{
    private const string EnvironmentVariable = "PW64_ROM_PATH";
    private const string RomFolderName = "roms";
    private static readonly string[] RomExtensions = [".z64", ".v64", ".n64"];

    /// <summary>The path of the real ROM, or <c>null</c> if none was found. Computed once.</summary>
    public static string? RomPath { get; } = FindRom();

    private static string? FindRom()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        // Walk up from the test binary folder (e.g. tests/.../bin/Debug/net10.0)
        // until we find a "roms" folder or reach the drive root.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, RomFolderName);
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            string? rom = Directory.EnumerateFiles(candidate)
                .FirstOrDefault(f => RomExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            if (rom is not null)
            {
                return rom;
            }
        }

        return null;
    }
}

/// <summary>
/// Like [Fact], but automatically skips the test if no real ROM is available.
/// This keeps the test suite green on machines (and CI servers) without the game.
/// </summary>
public sealed class RealRomFactAttribute : FactAttribute
{
    public RealRomFactAttribute()
    {
        if (TestRomLocator.RomPath is null)
        {
            Skip = "No Pilotwings 64 ROM found. Put one into the 'roms' folder or set PW64_ROM_PATH.";
        }
    }
}
