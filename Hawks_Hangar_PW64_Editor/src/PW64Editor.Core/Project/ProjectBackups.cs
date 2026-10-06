using System.Globalization;

namespace PW64Editor.Core.Project;

/// <summary>
/// Information about one restore point.
/// </summary>
/// <param name="Name">Folder name, e.g. "2026-10-06_14-30-05". Used to restore or delete it.</param>
/// <param name="Path">Full path of the backup folder.</param>
/// <param name="CreatedAt">When the restore point was created (local time).</param>
/// <param name="ContainsRom">Whether the hack ROM was saved with it.</param>
/// <param name="FileCount">Number of replacement files in the restore point.</param>
public sealed record BackupInfo(string Name, string Path, DateTime CreatedAt, bool ContainsRom, int FileCount);

/// <summary>
/// Restore points of a project, stored in the "backups" folder.
/// </summary>
/// <remarks>
/// <para>
/// A restore point is a complete snapshot of the project: project.json, the whole "files"
/// folder and the hack ROM. Restoring brings all three back together, so project and ROM
/// always match. (Saving only the ROM would not be enough: the next build would overwrite it
/// with the current project state again.)
/// </para>
/// <para>Layout:</para>
/// <code>
/// backups/
///   2026-10-06_14-30-05/
///     project.json
///     files/...
///     rom.z64
/// </code>
/// <para>
/// Local settings (project.user.json) are not part of a restore point: they hold paths of
/// this computer, which should not change when going back to an older state.
/// </para>
/// </remarks>
public static class ProjectBackups
{
    public const string FolderName = "backups";
    private const string RomFileName = "rom.z64";
    private const string NameFormat = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>The folder holding all restore points of a project.</summary>
    public static string GetFolder(HackProject project) => Path.Combine(project.Folder, FolderName);

    /// <summary>
    /// Creates a restore point of the current state: project.json, files and (if it exists) the hack ROM.
    /// </summary>
    public static BackupInfo Create(HackProject project)
    {
        string name = DateTime.Now.ToString(NameFormat, CultureInfo.InvariantCulture);
        string folder = Path.Combine(GetFolder(project), name);

        // Two backups within the same second: add a counter.
        for (int counter = 2; Directory.Exists(folder); counter++)
        {
            folder = Path.Combine(GetFolder(project), $"{name}_{counter}");
        }

        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(project.Folder, HackProject.ProjectFileName), Path.Combine(folder, HackProject.ProjectFileName));
        CopyDirectory(project.FilesFolder, Path.Combine(folder, HackProject.FilesFolderName));

        if (File.Exists(project.OutputRomPath))
        {
            File.Copy(project.OutputRomPath, Path.Combine(folder, RomFileName));
        }

        return ReadInfo(folder);
    }

    /// <summary>Lists all restore points, newest first.</summary>
    public static IReadOnlyList<BackupInfo> List(HackProject project)
    {
        string root = GetFolder(project);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, HackProject.ProjectFileName)))
            .Select(ReadInfo)
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Returns the project to the state of a restore point. The current project.json and files
    /// are replaced; the hack ROM is replaced too if the restore point contains one.
    /// </summary>
    /// <remarks>
    /// The current state is lost unless the caller creates a restore point first.
    /// The given <paramref name="project"/> object is outdated afterwards; use the returned one.
    /// </remarks>
    /// <returns>The project, reloaded from disk.</returns>
    /// <exception cref="ProjectException">The restore point does not exist.</exception>
    public static HackProject Restore(HackProject project, string backupName)
    {
        string folder = GetBackupFolder(project, backupName);

        File.Copy(Path.Combine(folder, HackProject.ProjectFileName),
                  Path.Combine(project.Folder, HackProject.ProjectFileName), overwrite: true);

        if (Directory.Exists(project.FilesFolder))
        {
            Directory.Delete(project.FilesFolder, recursive: true);
        }

        string backupFiles = Path.Combine(folder, HackProject.FilesFolderName);
        if (Directory.Exists(backupFiles))
        {
            CopyDirectory(backupFiles, project.FilesFolder);
        }
        else
        {
            Directory.CreateDirectory(project.FilesFolder);
        }

        string backupRom = Path.Combine(folder, RomFileName);
        if (File.Exists(backupRom))
        {
            File.Copy(backupRom, project.OutputRomPath, overwrite: true);
        }

        return HackProject.Load(project.Folder);
    }

    /// <summary>Deletes a restore point.</summary>
    /// <exception cref="ProjectException">The restore point does not exist.</exception>
    public static void Delete(HackProject project, string backupName)
    {
        Directory.Delete(GetBackupFolder(project, backupName), recursive: true);
    }

    private static string GetBackupFolder(HackProject project, string backupName)
    {
        // Only plain folder names are allowed, so "..\..\something" cannot escape the backups folder.
        if (backupName.Length == 0 || backupName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || backupName.Contains(".."))
        {
            throw new ProjectException($"Invalid restore point name '{backupName}'.");
        }

        string folder = Path.Combine(GetFolder(project), backupName);
        if (!File.Exists(Path.Combine(folder, HackProject.ProjectFileName)))
        {
            throw new ProjectException($"Restore point '{backupName}' does not exist.");
        }

        return folder;
    }

    private static BackupInfo ReadInfo(string folder)
    {
        string name = Path.GetFileName(folder);

        // The time comes from the folder name (first 19 characters), so it survives copying the
        // project to another computer. Fall back to the folder's creation time if it was renamed.
        DateTime created = name.Length >= NameFormat.Length
            && DateTime.TryParseExact(name[..NameFormat.Length], NameFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
                ? parsed
                : Directory.GetCreationTime(folder);

        string files = Path.Combine(folder, HackProject.FilesFolderName);
        int fileCount = Directory.Exists(files)
            ? Directory.EnumerateFiles(files, "*" + HackProject.OverrideExtension).Count()
            : 0;

        return new BackupInfo(name, folder, created, File.Exists(Path.Combine(folder, RomFileName)), fileCount);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string subfolder in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(subfolder, Path.Combine(destination, Path.GetFileName(subfolder)));
        }
    }
}
