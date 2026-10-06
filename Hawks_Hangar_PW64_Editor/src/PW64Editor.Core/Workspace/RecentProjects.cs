using PW64Editor.Core.Project;

namespace PW64Editor.Core.Workspace;

/// <summary>
/// Maintains the list of recently opened projects inside <see cref="EditorSettings"/>.
/// </summary>
public static class RecentProjects
{
    /// <summary>How many projects the list remembers.</summary>
    public const int MaxEntries = 10;

    /// <summary>
    /// Moves a project to the top of the list (adding it if new) and trims the list.
    /// </summary>
    public static void Add(EditorSettings settings, string projectFolder)
    {
        string fullPath = Path.GetFullPath(projectFolder);
        settings.RecentProjects.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        settings.RecentProjects.Insert(0, fullPath);

        if (settings.RecentProjects.Count > MaxEntries)
        {
            settings.RecentProjects.RemoveRange(MaxEntries, settings.RecentProjects.Count - MaxEntries);
        }
    }

    /// <summary>Removes a project from the list.</summary>
    public static void Remove(EditorSettings settings, string projectFolder)
    {
        string fullPath = Path.GetFullPath(projectFolder);
        settings.RecentProjects.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the projects that still exist. Projects that were moved or deleted are skipped
    /// (but stay in the settings, in case a drive is just not connected right now).
    /// </summary>
    public static IReadOnlyList<string> GetExisting(EditorSettings settings) =>
        settings.RecentProjects
            .Where(p => File.Exists(Path.Combine(p, HackProject.ProjectFileName)))
            .ToList();
}
