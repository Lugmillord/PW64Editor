using System.Windows;
using System.Windows.Threading;

namespace PW64Editor.App;

/// <summary>
/// Application entry point. The start window is opened via StartupUri in App.xaml.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Last line of defense: an unexpected error shows a message instead of closing the editor
    /// without a word. Expected errors (wrong ROM, full file system, ...) are handled where
    /// they occur and never reach this point.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe editor keeps running, but please report this error.",
            "Hawk's Hangar",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
