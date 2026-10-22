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

    /// <summary>
    /// Ids of the code fixes applied to this hack (see <see cref="Code.CodeFixes"/>). New projects get
    /// all known fixes; projects made before a fix existed are offered it when opened. A fix cannot be
    /// removed again (only by returning to a restore point).
    /// </summary>
    public List<string> AppliedCodeFixes { get; set; } = [];

    /// <summary>
    /// Ids of the game patches applied to this hack (see <see cref="Code.GamePatches"/>): new features
    /// of the game. New projects get all of them; they can be applied and undone in the project settings.
    /// </summary>
    public List<string> AppliedPatches { get; set; } = [];

    /// <summary>Changed instruments and silenced channels of songs (only songs with changes).</summary>
    public List<Audio.SongSettings> Music { get; set; } = [];

    /// <summary>Changed result sheets and generated point texts of missions (only missions with changes).</summary>
    public List<Missions.MissionSettings> Missions { get; set; } = [];

    /// <summary>Changed classes: own medal limits and islands (Classes tab; only classes with changes).</summary>
    public List<Classes.ClassSettings> Classes { get; set; } = [];

    /// <summary>Additional features built into the game (Features tab).</summary>
    public GameFeatureSettings Features { get; set; } = new();
}

/// <summary>Additional features the editor builds into the game (stored in project.json).</summary>
public sealed class GameFeatureSettings
{
    /// <summary>Debug output: messages to the IS-Viewer port of emulators and into a log in RAM.</summary>
    public bool DebugOutput { get; set; }

    /// <summary>With <see cref="DebugOutput"/>: report the steps of the game's startup.</summary>
    public bool StartupInfo { get; set; }

    /// <summary>With <see cref="DebugOutput"/>: pass on the debug messages of the original developers.</summary>
    public bool GameMessages { get; set; }

    /// <summary>With <see cref="DebugOutput"/>: show the editor's messages on the screen too (15 seconds).</summary>
    public bool OnScreen { get; set; } = true;

    /// <summary>With <see cref="DebugOutput"/>: show current values during missions (the four below).</summary>
    public bool LiveValues { get; set; }

    /// <summary>Live value: distance flown.</summary>
    public bool LiveDistance { get; set; } = true;

    /// <summary>Live value: highest altitude of this flight.</summary>
    public bool LiveAltitude { get; set; } = true;

    /// <summary>Live value: horizontal speed.</summary>
    public bool LiveHorizontalSpeed { get; set; } = true;

    /// <summary>Live value: vertical speed.</summary>
    public bool LiveVerticalSpeed { get; set; } = true;

    /// <summary>The live values chosen (none unless <see cref="DebugOutput"/> and <see cref="LiveValues"/>).</summary>
    public Code.LiveValues LiveMask => !DebugOutput || !LiveValues
        ? Code.LiveValues.None
        : (LiveDistance ? Code.LiveValues.Distance : 0) | (LiveAltitude ? Code.LiveValues.HighestAltitude : 0)
          | (LiveHorizontalSpeed ? Code.LiveValues.HorizontalSpeed : 0) | (LiveVerticalSpeed ? Code.LiveValues.VerticalSpeed : 0);

    /// <summary>A copy.</summary>
    public GameFeatureSettings Clone() => (GameFeatureSettings)MemberwiseClone();

    /// <summary>What the game module does in a build (without the mission conditions).</summary>
    public Code.GameModuleOptions ModuleOptions =>
        new(DebugOutput, DebugOutput && StartupInfo, DebugOutput && GameMessages, DebugOutput && OnScreen) { Live = LiveMask };
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
