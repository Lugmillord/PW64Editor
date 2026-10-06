using System.Windows;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// A minimal dialog asking for one line of text.
/// </summary>
public partial class InputDialog : Window
{
    private InputDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the dialog and returns the entered text, or null if the user cancelled.
    /// </summary>
    public static string? Ask(Window owner, string title, string prompt, string initialValue)
    {
        var dialog = new InputDialog { Owner = owner, Title = title };
        dialog.PromptText.Text = prompt;
        dialog.InputBox.Text = initialValue;
        dialog.InputBox.CaretIndex = initialValue.Length;
        dialog.InputBox.Focus();
        return dialog.ShowDialog() == true ? dialog.InputBox.Text : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
