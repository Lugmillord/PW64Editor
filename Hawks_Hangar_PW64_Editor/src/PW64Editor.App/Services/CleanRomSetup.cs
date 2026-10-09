using System.Windows;
using Microsoft.Win32;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Services;

/// <summary>
/// Guides the user through providing the clean ROM: on first use, when the editor's copy is
/// damaged, or when the user wants to replace it.
/// </summary>
internal static class CleanRomSetup
{
    /// <summary>
    /// Returns the clean ROM. If the editor has no valid copy, explains why it is needed and
    /// asks the user to select one.
    /// </summary>
    /// <returns>The clean ROM, or null if the user cancelled.</returns>
    public static N64Rom? EnsureCleanRom(Window owner)
    {
        N64Rom? rom = EditorContext.GetCleanRom(out string? problem);
        if (rom is not null)
        {
            return rom;
        }

        string message =
            $"{problem}\n\n" +
            L.T("Hawk's Hangar needs an unmodified Pilotwings 64 (USA) ROM. Every hack is built from it, " +
            "and patches are made against it. The editor keeps its own copy, so your file stays " +
            "untouched and may be moved later.") + "\n\n" +
            L.T("Select your ROM now? (.z64, .v64 and .n64 all work.)");

        if (MessageBox.Show(owner, message, Ui.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
        {
            return null;
        }

        return SelectAndImport(owner) ? EditorContext.GetCleanRom(out _) : null;
    }

    /// <summary>
    /// Lets the user pick a ROM file and stores a copy if it is the clean supported ROM.
    /// Asks again after a rejected file, until the user cancels.
    /// </summary>
    /// <returns>True if a ROM was imported.</returns>
    public static bool SelectAndImport(Window owner)
    {
        while (true)
        {
            var dialog = new OpenFileDialog
            {
                Title = L.T("Select your Pilotwings 64 (USA) ROM"),
                Filter = Ui.RomFilter,
            };

            if (dialog.ShowDialog(owner) != true)
            {
                return false;
            }

            CleanRomImportResult result = EditorContext.CleanRoms.Import(dialog.FileName);
            if (result.Success)
            {
                EditorContext.ResetCleanRom();
                Ui.ShowInfo(owner, L.F("ROM accepted. {0}", result.Message));
                return true;
            }

            if (!Ui.Confirm(owner, L.F("This ROM cannot be used.\n\n{0}\n\nSelect a different file?", result.Message)))
            {
                return false;
            }
        }
    }
}
