using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Text;

namespace PW64Editor.Core.Workspace;

/// <summary>
/// An open project in the editor, together with the clean ROM it is based on.
/// </summary>
/// <remarks>
/// This class bundles every project operation the user interface offers, so the UI code stays
/// small and the logic can be tested without a UI. Methods may take a moment (building
/// recompresses data, patches are verified); call them from a background thread in a UI.
/// </remarks>
public sealed class EditorSession
{
    private GameFileSystem? _cleanFileSystem;

    private EditorSession(HackProject project, N64Rom cleanRom, RomLayout layout)
    {
        Project = project;
        CleanRom = cleanRom;
        Layout = layout;
    }

    /// <summary>The open project. Replaced after restoring a restore point.</summary>
    public HackProject Project { get; private set; }

    /// <summary>The clean ROM the project is based on.</summary>
    public N64Rom CleanRom { get; }

    /// <summary>The fixed addresses of the game release.</summary>
    public RomLayout Layout { get; }

    /// <summary>The game files of the clean ROM (read once, then cached).</summary>
    public GameFileSystem CleanFileSystem => _cleanFileSystem ??= GameFileSystem.Read(CleanRom, Layout);

    /// <summary>
    /// Creates a new project and opens it.
    /// </summary>
    /// <param name="folder">Project folder (new or empty).</param>
    /// <param name="name">Name of the hack.</param>
    /// <param name="prepareForGit">Whether to add a .gitignore.</param>
    /// <param name="cleanRom">The verified clean ROM.</param>
    /// <param name="cleanRomPath">Where that ROM is stored (remembered in the project's local
    /// settings, so the command line tool can build the project too).</param>
    public static EditorSession Create(string folder, string name, bool prepareForGit, N64Rom cleanRom, string cleanRomPath)
    {
        HackProject project = HackProject.Create(folder, name, cleanRom.ComputeSha1(), cleanRomPath, prepareForGit);
        return new EditorSession(project, cleanRom, RomLayout.PilotwingsUsa);
    }

    /// <summary>
    /// Opens an existing project.
    /// </summary>
    /// <exception cref="ProjectException">No project in the folder, or it was made for a different ROM.</exception>
    public static EditorSession Open(string folder, N64Rom cleanRom, string cleanRomPath)
    {
        HackProject project = HackProject.Load(folder);

        if (!string.Equals(project.Settings.BaseRomSha1, cleanRom.ComputeSha1(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectException(
                "This project was made for a different base ROM than the one the editor uses. " +
                $"Project base ROM SHA-1: {project.Settings.BaseRomSha1}.");
        }

        // Point the project at the editor's clean ROM copy, so the command line tool
        // finds it too when building this project.
        if (!string.Equals(project.Local.CleanRomPath, cleanRomPath, StringComparison.OrdinalIgnoreCase))
        {
            project.Local.CleanRomPath = cleanRomPath;
            project.Save();
        }

        return new EditorSession(project, cleanRom, RomLayout.PilotwingsUsa);
    }

    /// <summary>
    /// Builds the project and overwrites the hack ROM.
    /// </summary>
    /// <param name="createRestorePoint">Create a restore point after building.</param>
    /// <returns>The build result, the path of the hack ROM and the restore point (if created).</returns>
    public (ProjectBuildResult Result, string RomPath, BackupInfo? RestorePoint) Build(bool createRestorePoint)
    {
        ProjectBuildResult result = ProjectBuilder.Build(Project, CleanRom, Layout);
        string romPath = ProjectBuilder.WriteRom(Project, result);
        BackupInfo? restorePoint = createRestorePoint ? ProjectBackups.Create(Project) : null;
        return (result, romPath, restorePoint);
    }

    /// <summary>
    /// Builds the project fresh and saves a verified BPS patch.
    /// </summary>
    /// <returns>Size of the patch in bytes.</returns>
    public int ExportPatch(string patchPath)
    {
        ProjectBuildResult result = ProjectBuilder.Build(Project, CleanRom, Layout);
        byte[] patch = ProjectBuilder.CreatePatch(Project, CleanRom, result.RomBuild.Rom);
        File.WriteAllBytes(patchPath, patch);
        return patch.Length;
    }

    /// <summary>Compares the project's replacement files with the originals.</summary>
    public IReadOnlyList<OverrideStatus> GetStatus() => ProjectStatus.Compute(Project, CleanFileSystem);

    /// <summary>
    /// Copies an original game file into the project for editing.
    /// </summary>
    /// <returns>Path of the new file in the project.</returns>
    /// <exception cref="ProjectException">Unknown index, or the file is already in the project.</exception>
    public string AddFileToProject(int tableIndex)
    {
        GameFile file = CleanFileSystem.Files.FirstOrDefault(f => f.TableIndex == tableIndex)
            ?? throw new ProjectException($"The game has no file with table index {tableIndex}.");
        return Project.AddOverride(file);
    }

    /// <summary>Removes a file from the project, so the original is used again.</summary>
    public bool RemoveFileFromProject(int tableIndex) => Project.RemoveOverride(tableIndex);

    /// <summary>
    /// Returns the current version of a game file: the project's replacement if there is one,
    /// otherwise the original from the clean ROM.
    /// </summary>
    /// <param name="fromProject">True if the project's replacement was used.</param>
    /// <exception cref="ProjectException">Unknown index.</exception>
    public byte[] GetCurrentFileData(int tableIndex, out bool fromProject)
    {
        FileOverride? replacement = Project.GetOverrides().FirstOrDefault(o => o.TableIndex == tableIndex);
        if (replacement is not null)
        {
            fromProject = true;
            return File.ReadAllBytes(replacement.Path);
        }

        fromProject = false;
        GameFile original = CleanFileSystem.Files.FirstOrDefault(f => f.TableIndex == tableIndex)
            ?? throw new ProjectException($"The game has no file with table index {tableIndex}.");
        return original.Data;
    }

    /// <summary>
    /// Loads all game texts in their current version (project copy of the text file if the
    /// project has one, otherwise the original).
    /// </summary>
    /// <exception cref="InvalidDataException">The text file or the font is damaged.</exception>
    public GameTextLibrary LoadTexts()
    {
        TextFont font = TextFont.Load(CleanFileSystem);
        GameFile textFile = GameTextFile.FindFile(CleanFileSystem);
        byte[] data = GetCurrentFileData(textFile.TableIndex, out bool fromProject);
        return GameTextLibrary.Create(font, data, fromProject);
    }

    /// <summary>Lists the restore points, newest first.</summary>
    public IReadOnlyList<BackupInfo> GetRestorePoints() => ProjectBackups.List(Project);

    /// <summary>Creates a restore point of the current state.</summary>
    public BackupInfo CreateRestorePoint() => ProjectBackups.Create(Project);

    /// <summary>Returns the project to a restore point. The current state is replaced.</summary>
    public void RestoreTo(string restorePointName) => Project = ProjectBackups.Restore(Project, restorePointName);

    /// <summary>Deletes a restore point.</summary>
    public void DeleteRestorePoint(string restorePointName) => ProjectBackups.Delete(Project, restorePointName);
}
