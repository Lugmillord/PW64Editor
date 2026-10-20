using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using PW64Editor.App.Dialogs;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Services;

/// <summary>
/// Creating, opening and copying projects, and making them from a ROM or a patch; shared by the
/// start window and the main window.
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
            Ui.ShowError(owner, L.F("The project could not be created.\n\n{0}", ex.Message));
            return null;
        }
    }

    /// <summary>Lets the user pick a project folder and opens it.</summary>
    public static EditorSession? OpenWithDialog(Window owner)
    {
        var dialog = new OpenFolderDialog { Title = L.T("Open a Hawk's Hangar project folder") };
        return dialog.ShowDialog(owner) == true ? Open(owner, dialog.FolderName) : null;
    }

    /// <summary>Opens the project in <paramref name="folder"/>.</summary>
    /// <param name="owner">The window the messages belong to.</param>
    /// <param name="folder">The project folder.</param>
    /// <param name="remember">Add it to the recent projects (not when it is only read to be copied).</param>
    public static EditorSession? Open(Window owner, string folder, bool remember = true)
    {
        N64Rom? rom = CleanRomSetup.EnsureCleanRom(owner);
        if (rom is null)
        {
            return null;
        }

        try
        {
            EditorSession session = EditorSession.Open(folder, rom, EditorContext.CleanRoms.RomPath);
            if (remember)
            {
                EditorContext.AddRecentProject(session.Project.Folder);
            }

            return session;
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, L.F("The project could not be opened.\n\n{0}", ex.Message));
            return null;
        }
    }
    /// <summary>
    /// Copies a project and opens the copy. Without <paramref name="current"/> the user picks the project folder first.
    /// </summary>
    /// <param name="owner">The window the dialogs belong to.</param>
    /// <param name="current">The open project (from the main window), or null.</param>
    /// <returns>The open copy, or null if cancelled or failed (the user has been told why).</returns>
    public static async Task<EditorSession?> CopyAsync(Window owner, EditorSession? current)
    {
        EditorSession? source = current;
        if (source is null)
        {
            var folderDialog = new OpenFolderDialog { Title = L.T("Which project should be copied?") };
            if (folderDialog.ShowDialog(owner) != true || (source = Open(owner, folderDialog.FolderName, remember: false)) is null)
            {
                return null;
            }
        }

        HackProject project = source.Project;
        var dialog = new NewProjectDialog { Owner = owner };
        dialog.Configure(L.T("Copy project"), L.T("Copy project"), L.F("{0} (copy)", project.Settings.Name), project.HasGitIgnore,
            Path.GetDirectoryName(project.Folder));
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        try
        {
            // The dialog's values are read here: its controls belong to the UI thread, the copy runs in the background.
            (string folder, string name, bool git) = (dialog.ProjectFolder, dialog.ProjectName, dialog.PrepareForGit);
            EditorSession copy = await Ui.RunBusyAsync(owner, () => source.CopyTo(folder, name, git));
            EditorContext.AddRecentProject(copy.Project.Folder);
            return copy;
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, L.F("The project could not be copied.\n\n{0}", ex.Message));
            return null;
        }
    }

    /// <summary>Lets the user pick a hack ROM and makes a project from it.</summary>
    /// <returns>The open project and the report of the import, or null if cancelled or failed (the user has been told why).</returns>
    public static async Task<(EditorSession Session, string Report)?> FromRomAsync(Window owner)
    {
        N64Rom? clean = CleanRomSetup.EnsureCleanRom(owner);
        if (clean is null)
        {
            return null;
        }

        var file = new OpenFileDialog { Title = L.T("Select the hack ROM to make a project from"), Filter = Ui.RomFilter };
        if (file.ShowDialog(owner) != true)
        {
            return null;
        }

        string path = file.FileName;
        return await ImportAsync(owner, clean, L.T("Project from ROM"), () =>
        {
            N64Rom hack = N64Rom.Load(path);
            return (hack, ProjectImporter.Analyze(hack, clean, RomLayout.PilotwingsUsa, Path.GetFileNameWithoutExtension(path)));
        });
    }

    /// <summary>Lets the user pick a BPS patch, applies it to the clean ROM and makes a project from the result.</summary>
    /// <returns>The open project and the report of the import, or null if cancelled or failed (the user has been told why).</returns>
    public static async Task<(EditorSession Session, string Report)?> FromPatchAsync(Window owner)
    {
        N64Rom? clean = CleanRomSetup.EnsureCleanRom(owner);
        if (clean is null)
        {
            return null;
        }

        var file = new OpenFileDialog { Title = L.T("Select the BPS patch to make a project from"), Filter = Ui.PatchFilter };
        if (file.ShowDialog(owner) != true)
        {
            return null;
        }

        string path = file.FileName;
        return await ImportAsync(owner, clean, L.T("Project from patch"), () =>
        {
            (N64Rom hack, string metadata) = ProjectImporter.ApplyPatch(clean, File.ReadAllBytes(path));
            return (hack, ProjectImporter.Analyze(hack, clean, RomLayout.PilotwingsUsa, Path.GetFileNameWithoutExtension(path), metadata));
        });
    }

    /// <summary>Reads the ROM (in the background), asks for name and location and creates the project.</summary>
    private static async Task<(EditorSession Session, string Report)?> ImportAsync(Window owner, N64Rom clean, string title,
        Func<(N64Rom Hack, RomImport Import)> read)
    {
        N64Rom hack;
        RomImport import;
        try
        {
            (hack, import) = await Ui.RunBusyAsync(owner, read);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, L.F("No project can be made from this file.\n\n{0}", ex.Message));
            return null;
        }

        var dialog = new NewProjectDialog { Owner = owner };
        dialog.Configure(title, L.T("Create project"), import.Settings.Name, false);
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        try
        {
            // The dialog's values are read here: its controls belong to the UI thread, the import runs in the background.
            (string folder, string name, bool git) = (dialog.ProjectFolder, dialog.ProjectName, dialog.PrepareForGit);
            string cleanRomPath = EditorContext.CleanRoms.RomPath;
            (EditorSession session, IReadOnlyList<ImportNote> notes) = await Ui.RunBusyAsync(owner, () => EditorSession.CreateFromImport(
                import, hack, folder, name, git, clean, cleanRomPath));
            EditorContext.AddRecentProject(session.Project.Folder);
            return (session, ReportText(notes));
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, L.F("The project could not be created.\n\n{0}", ex.Message));
            return null;
        }
    }

    /// <summary>The report of an import as text: what was taken over, then the warnings.</summary>
    private static string ReportText(IReadOnlyList<ImportNote> notes)
    {
        var text = new StringBuilder();
        text.AppendLine(L.T("The project was made from the ROM. What was found:"));
        text.AppendLine();
        foreach (ImportNote note in notes.Where(n => n.Kind == ImportNoteKind.Info))
        {
            text.AppendLine("• " + note.Text);
        }

        List<ImportNote> warnings = notes.Where(n => n.Kind == ImportNoteKind.Warning).ToList();
        if (warnings.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(L.T("Not taken over, or different in the rebuilt ROM:"));
            text.AppendLine();
            foreach (ImportNote note in warnings)
            {
                text.AppendLine("⚠ " + note.Text);
            }
        }

        return text.ToString();
    }

    /// <summary>Shows the report of an import.</summary>
    public static void ShowReport(Window owner, string report) =>
        TextReportWindow.ShowReport(owner, L.T("Project made from a ROM"), report);
}
