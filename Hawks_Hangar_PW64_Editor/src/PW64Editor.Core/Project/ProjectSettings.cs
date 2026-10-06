namespace PW64Editor.Core.Project;

/// <summary>
/// The shared settings of a hack project, stored as <c>project.json</c>.
/// </summary>
/// <remarks>
/// This file belongs to the project and may be shared (e.g. in a Git repository).
/// It contains no paths and no game data, only a description of the hack and the
/// SHA-1 of the base ROM it was made for.
/// </remarks>
public sealed class ProjectSettings
{
    /// <summary>The project file format this editor writes.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Format version, so future editor versions can upgrade old projects.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Name of the hack, also used for the output file names.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Version of the hack, e.g. "1.0".</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>Author name(s).</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Free text describing the hack.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// SHA-1 of the clean ROM this project is based on. A build refuses any other ROM,
    /// because the resulting patch would only work for people with that exact file.
    /// </summary>
    public string BaseRomSha1 { get; set; } = string.Empty;

    /// <summary>Build option: always move the audio data behind the file system.</summary>
    public bool RelocateAudio { get; set; }

    /// <summary>Build option: allow enlarging the ROM to 16/32/64 MiB if needed.</summary>
    public bool AllowExpansion { get; set; }
}

/// <summary>
/// Settings that only apply to this computer, stored as <c>project.user.json</c>.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="ProjectSettings"/> because a path like
/// "C:\Users\...\Pilotwings.z64" is meaningless on someone else's computer.
/// The project's .gitignore excludes this file.
/// </remarks>
public sealed class LocalProjectSettings
{
    /// <summary>Where the clean base ROM is stored on this computer.</summary>
    public string? CleanRomPath { get; set; }

    /// <summary>
    /// Where the built hack ROM is written. <c>null</c> means the default: a file named after
    /// the hack inside the project folder (see <see cref="HackProject.OutputRomPath"/>).
    /// </summary>
    public string? OutputRomPath { get; set; }
}
