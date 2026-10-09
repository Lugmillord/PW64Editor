using System.Text.Json;
using System.Text.RegularExpressions;
using PW64Editor.Core.Build;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Project;

/// <summary>
/// A ROM hack project: a folder that describes a hack as a set of changes to the clean ROM.
/// </summary>
/// <remarks>
/// <para>Folder layout:</para>
/// <code>
/// MyHack/
///   project.json        shared settings (name, version, base ROM SHA-1, build options, code fixes)
///   project.user.json   local settings (paths of the clean ROM and the output ROM)
///   files/              game files that replace the originals, e.g. 1063_UPWT_005.bin
///   backups/            restore points, only created on request (see ProjectBackups)
///   My_Hack.z64         the built hack ROM (default location, can be changed)
///   .gitignore          only if the project was prepared for Git
/// </code>
/// <para>
/// The clean ROM is never modified. Every build starts again from the clean ROM and applies
/// the project, so the result is always reproducible and mistakes never accumulate.
/// The hack ROM, on the other hand, is simply overwritten by every build.
/// </para>
/// <para>
/// Files in "files/" are identified by their name: table index, then the file type, e.g.
/// "1063_UPWT_005.bin" or simply "1063_UPWT.bin". Everything after the type is ignored,
/// so users may append notes to the name. Only ".bin" files count; other files (notes,
/// screenshots) may be kept in the folder and are ignored.
/// </para>
/// </remarks>
public sealed partial class HackProject
{
    public const string ProjectFileName = "project.json";
    public const string LocalFileName = "project.user.json";
    public const string FilesFolderName = "files";
    public const string OverrideExtension = ".bin";
    public const string GitIgnoreFileName = ".gitignore";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private HackProject(string folder, ProjectSettings settings, LocalProjectSettings local)
    {
        Folder = folder;
        Settings = settings;
        Local = local;
    }

    /// <summary>The project folder (full path).</summary>
    public string Folder { get; }

    /// <summary>Shared settings from project.json. Call <see cref="Save"/> after changing them.</summary>
    public ProjectSettings Settings { get; }

    /// <summary>Local settings from project.user.json. Call <see cref="Save"/> after changing them.</summary>
    public LocalProjectSettings Local { get; }

    /// <summary>Folder holding the replacement game files.</summary>
    public string FilesFolder => Path.Combine(Folder, FilesFolderName);

    /// <summary>
    /// Where the built hack ROM is written: the path from the local settings, or by default
    /// a file named after the hack inside the project folder.
    /// </summary>
    public string OutputRomPath =>
        Local.OutputRomPath ?? Path.Combine(Folder, GetOutputBaseName() + ".z64");

    /// <summary>The build options stored in the project.</summary>
    public RomBuildOptions BuildOptions => new(Settings.RelocateAudio, Settings.AllowExpansion, AppliedCodeFixes);

    /// <summary>The code fixes this project applies, in the editor's order.</summary>
    public IReadOnlyList<CodeFix> AppliedCodeFixes =>
        CodeFixes.All.Where(f => Settings.AppliedCodeFixes.Contains(f.Id)).ToList();

    /// <summary>Known code fixes the project does not apply yet.</summary>
    public IReadOnlyList<CodeFix> MissingCodeFixes =>
        CodeFixes.All.Where(f => !Settings.AppliedCodeFixes.Contains(f.Id)).ToList();

    /// <summary>Marks a code fix as applied (call <see cref="Save"/> afterwards). Applying it twice does nothing.</summary>
    public void AddCodeFix(CodeFix fix)
    {
        if (!Settings.AppliedCodeFixes.Contains(fix.Id))
        {
            Settings.AppliedCodeFixes.Add(fix.Id);
        }
    }

    /// <summary>
    /// Changes where the hack ROM is written (stored in the local settings; call <see cref="Save"/>).
    /// Pass <c>null</c> to return to the default location inside the project folder.
    /// </summary>
    /// <exception cref="ProjectException">The path is the clean ROM.</exception>
    public void SetOutputRomPath(string? path)
    {
        string? fullPath = path is null ? null : System.IO.Path.GetFullPath(path);
        if (fullPath is not null && IsCleanRomPath(fullPath))
        {
            throw new ProjectException(CoreText.T("The hack ROM cannot be written to the clean ROM. Choose a " +
                "different output path."));
        }

        Local.OutputRomPath = fullPath;
    }

    /// <summary>True if <paramref name="path"/> is the clean ROM of this project.</summary>
    public bool IsCleanRomPath(string path) =>
        Local.CleanRomPath is { } clean
        && string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(clean), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new project in an empty or not yet existing folder.
    /// </summary>
    /// <param name="folder">Project folder.</param>
    /// <param name="name">Name of the hack.</param>
    /// <param name="baseRomSha1">SHA-1 of the clean base ROM.</param>
    /// <param name="cleanRomPath">Where the clean ROM is stored on this computer (optional).</param>
    /// <param name="prepareForGit">If true, a .gitignore is created that keeps local settings,
    /// backups and ROM files out of version control.</param>
    /// <exception cref="ProjectException">The folder already contains files.</exception>
    public static HackProject Create(string folder, string name, string baseRomSha1, string? cleanRomPath, bool prepareForGit = false)
    {
        string fullPath = Path.GetFullPath(folder);
        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
        {
            throw new ProjectException(CoreText.F("The folder '{0}' is not empty. Choose a new or empty folder.", fullPath));
        }

        Directory.CreateDirectory(fullPath);
        Directory.CreateDirectory(Path.Combine(fullPath, FilesFolderName));

        // New projects start with every known code fix.
        var project = new HackProject(
            fullPath,
            new ProjectSettings { Name = name, BaseRomSha1 = baseRomSha1, AppliedCodeFixes = [.. CodeFixes.AllIds] },
            new LocalProjectSettings { CleanRomPath = cleanRomPath is null ? null : Path.GetFullPath(cleanRomPath) });

        project.Save();
        if (prepareForGit)
        {
            File.WriteAllText(Path.Combine(fullPath, GitIgnoreFileName), GitIgnoreContent);
        }

        return project;
    }

    /// <summary>
    /// Loads an existing project.
    /// </summary>
    /// <exception cref="ProjectException">The folder contains no valid project.</exception>
    public static HackProject Load(string folder)
    {
        string fullPath = Path.GetFullPath(folder);
        string projectFile = Path.Combine(fullPath, ProjectFileName);
        if (!File.Exists(projectFile))
        {
            throw new ProjectException(CoreText.F("No {0} found in '{1}'.", ProjectFileName, fullPath));
        }

        ProjectSettings settings = ReadJson<ProjectSettings>(projectFile)
            ?? throw new ProjectException(CoreText.F("{0} is empty.", ProjectFileName));

        if (settings.FormatVersion > ProjectSettings.CurrentFormatVersion)
        {
            throw new ProjectException(
                CoreText.F("This project was created with a newer editor (format {0}). Please update Hawk's Hangar.",
                    settings.FormatVersion));
        }

        settings.AppliedCodeFixes ??= [];
        if (settings.AppliedCodeFixes.FirstOrDefault(id => CodeFixes.Find(id) is null) is { } unknown)
        {
            throw new ProjectException(
                CoreText.F("This project uses the code fix '{0}', which this version of the editor does not know. " +
                    "Please update Hawk's Hangar.", unknown));
        }

        string localFile = Path.Combine(fullPath, LocalFileName);
        LocalProjectSettings local = File.Exists(localFile)
            ? ReadJson<LocalProjectSettings>(localFile) ?? new LocalProjectSettings()
            : new LocalProjectSettings();

        Directory.CreateDirectory(Path.Combine(fullPath, FilesFolderName));
        return new HackProject(fullPath, settings, local);
    }

    /// <summary>Writes project.json and project.user.json.</summary>
    public void Save()
    {
        File.WriteAllText(Path.Combine(Folder, ProjectFileName), JsonSerializer.Serialize(Settings, JsonOptions));
        File.WriteAllText(Path.Combine(Folder, LocalFileName), JsonSerializer.Serialize(Local, JsonOptions));
    }

    /// <summary>
    /// Lists the replacement files in the "files" folder.
    /// </summary>
    /// <exception cref="ProjectException">A file name cannot be interpreted, or two files
    /// replace the same table index.</exception>
    public IReadOnlyList<FileOverride> GetOverrides()
    {
        var overrides = new List<FileOverride>();
        var seen = new Dictionary<int, string>();

        IEnumerable<string> candidates = Directory.EnumerateFiles(FilesFolder)
            .Where(p => string.Equals(Path.GetExtension(p), OverrideExtension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);

        foreach (string path in candidates)
        {
            string fileName = Path.GetFileName(path);
            Match match = OverrideNamePattern().Match(fileName);
            if (!match.Success)
            {
                throw new ProjectException(
                    CoreText.F("Cannot tell which game file '{0}' replaces. Expected a name like '1063_UPWT_005.bin'.",
                        fileName));
            }

            int index = int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (seen.TryGetValue(index, out string? other))
            {
                throw new ProjectException(CoreText.F("'{0}' and '{1}' both replace file {2}.", fileName, other, index));
            }

            seen[index] = fileName;
            overrides.Add(new FileOverride(index, match.Groups["type"].Value, path));
        }

        return overrides.OrderBy(o => o.TableIndex).ToList();
    }

    /// <summary>
    /// Copies an original game file into the project, so it can be edited.
    /// </summary>
    /// <returns>Path of the new file.</returns>
    /// <exception cref="ProjectException">The project already replaces this file.</exception>
    public string AddOverride(GameFile original)
    {
        if (GetOverrides().Any(o => o.TableIndex == original.TableIndex))
        {
            throw new ProjectException(CoreText.F("The project already contains a replacement for file {0}.",
                original.TableIndex));
        }

        string path = Path.Combine(FilesFolder, original.ExportName);
        File.WriteAllBytes(path, original.Data);
        return path;
    }

    /// <summary>
    /// Writes new content for a game file into the project. An existing replacement file is
    /// overwritten; otherwise a new one is created with the standard name.
    /// </summary>
    /// <returns>Path of the written file.</returns>
    public string WriteOverride(GameFile original, byte[] data)
    {
        FileOverride? existing = GetOverrides().FirstOrDefault(o => o.TableIndex == original.TableIndex);
        string path = existing?.Path ?? System.IO.Path.Combine(FilesFolder, original.ExportName);
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>
    /// Removes the replacement for a file, so the build uses the original again.
    /// </summary>
    /// <returns>False if the project had no replacement for that file.</returns>
    public bool RemoveOverride(int tableIndex)
    {
        FileOverride? existing = GetOverrides().FirstOrDefault(o => o.TableIndex == tableIndex);
        if (existing is null)
        {
            return false;
        }

        File.Delete(existing.Path);
        return true;
    }

    /// <summary>Folder for songs imported from MIDI files (inside the files folder, so restore points keep them).</summary>
    public string MusicFolder => Path.Combine(FilesFolder, "music");

    private string ImportedSongPath(int song) =>
        Path.Combine(MusicFolder, $"song_{song.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}.seq");

    /// <summary>True if the project replaces a song with an imported one.</summary>
    public bool HasImportedSong(int song) => File.Exists(ImportedSongPath(song));

    /// <summary>The imported version of a song (in the game's format), or null.</summary>
    public byte[]? ReadImportedSong(int song) => HasImportedSong(song) ? File.ReadAllBytes(ImportedSongPath(song)) : null;

    /// <summary>Stores an imported song (in the game's format).</summary>
    public void WriteImportedSong(int song, byte[] sequence)
    {
        Directory.CreateDirectory(MusicFolder);
        File.WriteAllBytes(ImportedSongPath(song), sequence);
    }

    /// <summary>Removes the imported version of a song.</summary>
    /// <returns>False if there was none.</returns>
    public bool RemoveImportedSong(int song)
    {
        if (!HasImportedSong(song))
        {
            return false;
        }

        File.Delete(ImportedSongPath(song));
        return true;
    }

    /// <summary>
    /// A file name for build output, derived from the hack name: letters, digits, '-' and '_' only.
    /// </summary>
    public string GetOutputBaseName()
    {
        string cleaned = new(Settings.Name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        cleaned = cleaned.Trim('_');
        return cleaned.Length > 0 ? cleaned : "hack";
    }

    private static T? ReadJson<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProjectException(CoreText.F("'{0}' is not valid: {1}", Path.GetFileName(path), ex.Message), ex);
        }
    }

    /// <summary>File name pattern for replacement files: index, underscore, 4-character type, rest ignored.</summary>
    [GeneratedRegex(@"^(?<index>\d{1,5})_(?<type>[A-Za-z0-9 ]{4})(_.*)?\.bin$", RegexOptions.IgnoreCase)]
    private static partial Regex OverrideNamePattern();

    private const string GitIgnoreContent =
        """
        # Hawk's Hangar project
        # Local settings (contain paths of this computer)
        project.user.json

        # Restore points (contain ROMs)
        backups/

        # Never commit ROM files: they contain the complete game. Share the .bps patch instead.
        *.z64
        *.v64
        *.n64
        """;
}
