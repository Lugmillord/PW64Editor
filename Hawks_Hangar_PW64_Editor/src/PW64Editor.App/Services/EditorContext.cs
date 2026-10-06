using System.IO;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Services;

/// <summary>
/// Editor-wide state shared by all windows: settings, the clean ROM copy and recent projects.
/// </summary>
internal static class EditorContext
{
    private static N64Rom? _cleanRom;

    /// <summary>The editor's data folder (%AppData%\HawksHangar).</summary>
    public static EditorStorage Storage { get; } = EditorStorage.CreateDefault();

    /// <summary>The private copy of the clean ROM.</summary>
    public static CleanRomStore CleanRoms { get; } = new(Storage);

    /// <summary>Editor settings, loaded once at startup.</summary>
    public static EditorSettings Settings { get; } = Storage.LoadSettings();

    /// <summary>
    /// Returns the verified clean ROM, loading it on first use.
    /// </summary>
    /// <param name="problem">Why it is not available, if it returns null.</param>
    public static N64Rom? GetCleanRom(out string? problem)
    {
        if (_cleanRom is not null)
        {
            problem = null;
            return _cleanRom;
        }

        _cleanRom = CleanRoms.TryLoad(out problem);
        return _cleanRom;
    }

    /// <summary>Forgets the loaded ROM, so the next access reads the (new) copy again.</summary>
    public static void ResetCleanRom() => _cleanRom = null;

    public static void AddRecentProject(string folder)
    {
        RecentProjects.Add(Settings, folder);
        SaveSettings();
    }

    public static void RemoveRecentProject(string folder)
    {
        RecentProjects.Remove(Settings, folder);
        SaveSettings();
    }

    /// <summary>Saves the settings. Failing to save a recent-projects list is not worth an error message.</summary>
    public static void SaveSettings()
    {
        try
        {
            Storage.SaveSettings(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
