using System.Windows;
using Microsoft.Win32;
using PW64Editor.App.Dialogs;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Services;

/// <summary>
/// Creating and opening projects, shared by the start window and the main window.
/// </summary>
internal static class ProjectOpener
{
    /// <summary>Asks for name and location and creates a new project.</summary>
    /// <returns>The open project, or null if cancelled or failed (the user has been told why).</returns>
    public static EditorSession? CreateNew(Window owner)
    {
        N64Rom? rom = CleanRomSetup.EnsureCleanRom(owner);
        if (rom is null)
        {
            return null;
        }

        var dialog = new NewProjectDialog { Owner = owner };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        try
        {
            EditorSession session = EditorSession.Create(
                dialog.ProjectFolder, dialog.ProjectName, dialog.PrepareForGit, rom, EditorContext.CleanRoms.RomPath);
            EditorContext.AddRecentProject(session.Project.Folder);
            return session;
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, $"The project could not be created.\n\n{ex.Message}");
            return null;
        }
    }

    /// <summary>Lets the user pick a project folder and opens it.</summary>
    public static EditorSession? OpenWithDialog(Window owner)
    {
        var dialog = new OpenFolderDialog { Title = "Open a Hawk's Hangar project folder" };
        return dialog.ShowDialog(owner) == true ? Open(owner, dialog.FolderName) : null;
    }

    /// <summary>Opens the project in <paramref name="folder"/>.</summary>
    public static EditorSession? Open(Window owner, string folder)
    {
        N64Rom? rom = CleanRomSetup.EnsureCleanRom(owner);
        if (rom is null)
        {
            return null;
        }

        try
        {
            EditorSession session = EditorSession.Open(folder, rom, EditorContext.CleanRoms.RomPath);
            EditorContext.AddRecentProject(session.Project.Folder);
            return session;
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, $"The project could not be opened.\n\n{ex.Message}");
            return null;
        }
    }
}
