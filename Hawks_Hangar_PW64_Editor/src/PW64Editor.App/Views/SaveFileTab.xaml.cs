using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PW64Editor.App.Services;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// Editor for save files (the cartridge's EEPROM as emulators store it): the points of all tests
/// and the photo album of both saved games. A save file is not part of the project; it is opened
/// and saved on its own.
/// </summary>
public partial class SaveFileTab : UserControl
{
    private const string FileFilter = "EEPROM save files (*.eep;*.eeprom)|*.eep;*.eeprom|All files (*.*)|*.*";

    private SaveGameLayout _layout = SaveGameLayout.Retail;
    private EepromFile? _file;
    private string? _path;

    public SaveFileTab()
    {
        InitializeComponent();
    }

    /// <summary>True if the open save file has changes that are not saved yet.</summary>
    public bool HasUnsavedChanges => _file?.IsModified == true;

    /// <summary>
    /// Takes the layout of the saved results from the game (it depends on the missions). The
    /// missions cannot be edited yet, so the clean ROM's missions are used.
    /// </summary>
    public void Load(EditorSession session)
    {
        try
        {
            _layout = SaveGameLayout.FromGameFiles(session.CleanFileSystem);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            // Damaged mission files: the retail layout is the best guess.
            _layout = SaveGameLayout.Retail;
        }

        // An open file without changes is read again with the new layout.
        if (_file is not null && !_file.IsModified)
        {
            _file = EepromFile.Read(_file.ToBytes(), _layout);
            ShowFile();
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        Window owner = Window.GetWindow(this)!;
        if (HasUnsavedChanges && !Ui.Confirm(owner, "The open save file has unsaved changes. Discard them?"))
        {
            return;
        }

        var dialog = new OpenFileDialog { Title = "Open save file", Filter = FileFilter };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            _file = EepromFile.Load(dialog.FileName, _layout);
            _path = dialog.FileName;
            ShowFile();
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, $"The save file could not be opened.\n\n{ex.Message}");
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_path is not null)
        {
            Write(_path, keepBackup: true);
        }
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        if (_file is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save save file as",
            Filter = FileFilter,
            FileName = _path is null ? "Pilotwings 64.eep" : Path.GetFileName(_path),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)!) == true)
        {
            Write(dialog.FileName, keepBackup: false);
        }
    }

    /// <summary>Writes the file and reads it again, so the editor shows what is stored now.</summary>
    /// <param name="keepBackup">Keeps the old file as .bak if there is no such copy yet.</param>
    private void Write(string path, bool keepBackup)
    {
        if (_file is null)
        {
            return;
        }

        Window owner = Window.GetWindow(this)!;
        try
        {
            string backup = path + ".bak";
            if (keepBackup && File.Exists(path) && !File.Exists(backup))
            {
                File.Copy(path, backup);
            }

            byte[] data = _file.ToBytes();
            File.WriteAllBytes(path, data);
            _file = EepromFile.Read(data, _layout);
            _path = path;
            ShowFile();
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, $"The save file could not be written.\n\n{ex.Message}");
        }
    }

    /// <summary>Builds the pages for the open file.</summary>
    private void ShowFile()
    {
        if (_file is null)
        {
            return;
        }

        NoFileText.Visibility = Visibility.Collapsed;
        SlotTabs.Visibility = Visibility.Visible;
        Slot1Host.Content = new SaveSlotView(_file.Slots[0], "File 1", OnSlotChanged);
        Slot2Host.Content = new SaveSlotView(_file.Slots[1], "File 2", OnSlotChanged);
        UpdateFileState();
    }

    private void OnSlotChanged() => UpdateFileState();

    /// <summary>File name, buttons, tab headers and the page "Other".</summary>
    private void UpdateFileState()
    {
        if (_file is null)
        {
            return;
        }

        string unsaved = _file.IsModified ? " (unsaved changes)" : string.Empty;
        FileText.Text = $"{_path}{unsaved}";
        SaveButton.IsEnabled = _file.IsModified && _path is not null;
        SaveAsButton.IsEnabled = true;
        Slot1Tab.Header = SlotHeader("File 1", _file.Slots[0]);
        Slot2Tab.Header = SlotHeader("File 2", _file.Slots[1]);
        ShowOther();
    }

    private static string SlotHeader(string name, SaveSlot slot)
    {
        string state = slot.State switch
        {
            SaveSlotState.InUse => string.Empty,
            SaveSlotState.Empty => " (empty)",
            _ => " (not prepared)",
        };
        return $"{name}{state}{(slot.IsModified ? " *" : string.Empty)}";
    }

    /// <summary>Everything that is not a test result or a photo: checksums, unused bytes, the raw bytes.</summary>
    private void ShowOther()
    {
        if (_file is null)
        {
            return;
        }

        byte[] data = _file.ToBytes();
        var info = new StringBuilder();
        info.AppendLine($"File size: {_file.Size} bytes. The game uses the first {EepromFile.UsedSize} bytes: File 1 at 0, File 2 at 256.");
        for (int i = 0; i < 2; i++)
        {
            SaveSlot slot = _file.Slots[i];
            byte stored = data[(i * SaveSlot.Size) + SaveSlot.Size - 1];
            string state = slot.State switch
            {
                SaveSlotState.InUse => "in use (\"PW\")",
                SaveSlotState.Empty => "empty (\"pw\")",
                _ => "not prepared (the game prepares it when it starts)",
            };
            string checksum = slot.State == SaveSlotState.InUse
                ? stored == SaveSlot.Checksum(data.AsSpan(i * SaveSlot.Size, SaveSlot.Size))
                    ? $"checksum {stored:X2}, correct"
                    : $"checksum {stored:X2}, WRONG: the game does not load this file"
                : $"checksum {stored:X2} (not checked for empty files)";
            int unusedStart = (slot.Layout.DataEndBit + 7) / 8;
            info.AppendLine(
                $"File {i + 1}: {state}, {checksum}. Bytes {unusedStart}-254 are unused; {slot.UnusedBytesInUse} of them are not 0.");
        }

        int extraUsed = _file.ExtraBytes.ToArray().Count(b => b != 0);
        info.Append(_file.ExtraBytes.Length == 0
            ? "No bytes behind the two files."
            : $"{_file.ExtraBytes.Length} bytes behind the two files (not used by the game, kept as they are); {extraUsed} of them are not 0.");
        OtherInfo.Text = info.ToString();
        HexView.Text = HexDump(data);
    }

    private static string HexDump(byte[] data)
    {
        var text = new StringBuilder();
        for (int row = 0; row < data.Length; row += 16)
        {
            text.Append($"{row:X4}  ");
            for (int i = row; i < Math.Min(row + 16, data.Length); i++)
            {
                text.Append($"{data[i]:X2} ");
            }

            text.Append(' ');
            for (int i = row; i < Math.Min(row + 16, data.Length); i++)
            {
                char c = (char)data[i];
                text.Append(c is >= ' ' and <= '~' ? c : '.');
            }

            text.AppendLine();
        }

        return text.ToString();
    }
}
