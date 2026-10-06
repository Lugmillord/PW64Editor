using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Patching;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;

namespace PW64Editor.App.Services;

/// <summary>
/// Small helpers for messages, background work and common UI tasks.
/// </summary>
internal static class Ui
{
    public const string AppName = "Hawk's Hangar";

    /// <summary>File dialog filter for N64 ROMs.</summary>
    public const string RomFilter = "N64 ROMs (*.z64;*.v64;*.n64)|*.z64;*.v64;*.n64|All files (*.*)|*.*";

    /// <summary>File dialog filter for BPS patches.</summary>
    public const string PatchFilter = "BPS patches (*.bps)|*.bps|All files (*.*)|*.*";

    public static void ShowError(Window owner, string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void ShowInfo(Window owner, string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirm(Window owner, string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>
    /// True for errors that are caused by the user's files or input and can be explained
    /// with their message (as opposed to bugs, which go to the global error handler).
    /// </summary>
    public static bool IsExpectedError(Exception ex) =>
        ex is ProjectException
            or FileSystemFullException
            or InvalidRomException
            or InvalidDataException
            or BpsException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException;

    /// <summary>
    /// Runs work on a background thread while the window shows a wait cursor and ignores input,
    /// so the window stays responsive (it repaints) during longer operations like building.
    /// </summary>
    public static async Task<T> RunBusyAsync<T>(Window window, Func<T> work)
    {
        Cursor previousCursor = window.Cursor;
        window.Cursor = Cursors.Wait;
        window.IsEnabled = false;
        try
        {
            return await Task.Run(work);
        }
        finally
        {
            window.IsEnabled = true;
            window.Cursor = previousCursor;
        }
    }

    /// <summary>Opens a folder in Windows Explorer.</summary>
    public static void OpenFolder(string folder)
    {
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    /// <summary>Parses a number given as hex ("0xDE754") or decimal ("911188").</summary>
    public static bool TryParseNumber(string text, out int value)
    {
        text = text.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
