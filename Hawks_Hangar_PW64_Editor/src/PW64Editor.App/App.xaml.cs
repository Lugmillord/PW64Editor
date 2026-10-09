using System.Windows;
using System.Windows.Threading;
using PW64Editor.App.Services;

namespace PW64Editor.App;

/// <summary>What to open again after the editor restarted itself (to change the language).</summary>
/// <param name="ProjectFolder">The project that was open.</param>
/// <param name="SaveFile">The save file that was open in the tab "Save File", or null.</param>
/// <param name="TabIndex">The tab that was selected.</param>
public sealed record RestartState(string ProjectFolder, string? SaveFile, int TabIndex)
{
    private const string ProjectArgument = "--project";
    private const string SaveFileArgument = "--save-file";
    private const string TabArgument = "--tab";

    /// <summary>The command line arguments that bring the editor back to this state.</summary>
    public IReadOnlyList<string> ToArguments()
    {
        List<string> args = [ProjectArgument, ProjectFolder, TabArgument, TabIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        if (SaveFile is not null)
        {
            args.AddRange([SaveFileArgument, SaveFile]);
        }

        return args;
    }

    /// <summary>Reads the arguments of <see cref="ToArguments"/>; null if there is no project among them.</summary>
    public static RestartState? FromArguments(IReadOnlyList<string> args)
    {
        string? project = null;
        string? saveFile = null;
        int tab = 0;
        for (int i = 0; i + 1 < args.Count; i += 2)
        {
            switch (args[i])
            {
                case ProjectArgument:
                    project = args[i + 1];
                    break;
                case SaveFileArgument:
                    saveFile = args[i + 1];
                    break;
                case TabArgument:
                    _ = int.TryParse(args[i + 1], out tab);
                    break;
            }
        }

        return project is null ? null : new RestartState(project, saveFile, tab);
    }
}

/// <summary>
/// Application entry point. The start window is opened via StartupUri in App.xaml.
/// </summary>
public partial class App : Application
{
    /// <summary>Set when the editor was restarted by itself: what to open again (used once).</summary>
    public static RestartState? Restart { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // The language must be set before the first window is created.
        L.Load(EditorContext.Settings.Language);
        L.InstallAutoTranslation();
        Restart = RestartState.FromArguments(e.Args);
        base.OnStartup(e);
    }

    /// <summary>
    /// Last line of defense: an unexpected error shows a message instead of closing the editor
    /// without a word. Expected errors (wrong ROM, full file system, ...) are handled where
    /// they occur and never reach this point.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            L.F("An unexpected error occurred:\n\n{0}\n\nThe editor keeps running, but please report this error.", e.Exception.Message),
            Ui.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
