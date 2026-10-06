using System.Windows;
using PW64Editor.App.Services;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Options for a stress test build (the GUI counterpart of "rebuild" with its test flags).
/// </summary>
public partial class StressTestDialog : Window
{
    public StressTestDialog()
    {
        InitializeComponent();
    }

    public bool Recompress => RecompressBox.IsChecked == true;

    public bool RelocateAudio => RelocateBox.IsChecked == true;

    public bool AllowExpansion => ExpandBox.IsChecked == true;

    /// <summary>Size of the unused file to add, or null for none.</summary>
    public int? DummyFileSize { get; private set; }

    private void OnBuild(object sender, RoutedEventArgs e)
    {
        string text = DummyBox.Text.Trim();
        if (text.Length == 0)
        {
            DummyFileSize = null;
        }
        else if (Ui.TryParseNumber(text, out int size) && size > 0)
        {
            DummyFileSize = size;
        }
        else
        {
            Ui.ShowError(this, $"\"{text}\" is not a valid size. Enter a positive number or leave the field empty.");
            return;
        }

        DialogResult = true;
    }
}
