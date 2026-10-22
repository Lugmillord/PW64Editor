using System.IO;
using System.Windows;
using System.Windows.Controls;
using PW64Editor.App.Services;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// The tab "Features": additional features the editor builds into the game (the debug output
/// with startup steps, developers' messages and live values). Changes are saved in the project right away; they take effect with the next build.
/// It also writes the mission settings report (<see cref="MissionReport"/>) for debugging.
/// </summary>
public partial class FeaturesTab : UserControl
{
    private EditorSession? _session;

    /// <summary>Set while the boxes are filled by code.</summary>
    private bool _showing;

    public FeaturesTab()
    {
        InitializeComponent();
    }

    /// <summary>Shows the settings of a project.</summary>
    public void Load(EditorSession session)
    {
        _session = session;
        Show(session.Project.Settings.Features);
        StatusText.Text = string.Empty;
    }

    private void Show(GameFeatureSettings features)
    {
        _showing = true;
        DebugBox.IsChecked = features.DebugOutput;
        StartupBox.IsChecked = features.StartupInfo;
        GameMessagesBox.IsChecked = features.GameMessages;
        OnScreenBox.IsChecked = features.OnScreen;
        LiveBox.IsChecked = features.LiveValues;
        LiveDistanceBox.IsChecked = features.LiveDistance;
        LiveAltitudeBox.IsChecked = features.LiveAltitude;
        LiveHorizontalBox.IsChecked = features.LiveHorizontalSpeed;
        LiveVerticalBox.IsChecked = features.LiveVerticalSpeed;
        AllFieldsBox.IsChecked = features.AllFieldsOpen;
        DebugOptions.IsEnabled = features.DebugOutput;
        LiveOptions.IsEnabled = features.LiveValues;
        _showing = false;
    }

    /// <summary>Writes the mission settings of the project into a text file.</summary>
    private void OnSaveReport(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        Window owner = Window.GetWindow(this)!;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Save mission settings as a text file"),
            Filter = L.T("Text files (*.txt)|*.txt|All files (*.*)|*.*"),
            FileName = L.F("{0} mission settings.txt", _session.Project.Settings.Name),
            InitialDirectory = _session.Project.Folder,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, MissionReport.Create(_session), new System.Text.UTF8Encoding(false));
            StatusText.Text = L.F("Mission settings saved in {0}.", dialog.FileName);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(owner, ex.Message);
        }
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || _session is null)
        {
            return;
        }

        var features = new GameFeatureSettings
        {
            DebugOutput = DebugBox.IsChecked == true,
            StartupInfo = StartupBox.IsChecked == true,
            GameMessages = GameMessagesBox.IsChecked == true,
            OnScreen = OnScreenBox.IsChecked == true,
            LiveValues = LiveBox.IsChecked == true,
            LiveDistance = LiveDistanceBox.IsChecked == true,
            LiveAltitude = LiveAltitudeBox.IsChecked == true,
            LiveHorizontalSpeed = LiveHorizontalBox.IsChecked == true,
            LiveVerticalSpeed = LiveVerticalBox.IsChecked == true,
            AllFieldsOpen = AllFieldsBox.IsChecked == true,
        };
        DebugOptions.IsEnabled = features.DebugOutput;
        LiveOptions.IsEnabled = features.LiveValues;

        try
        {
            _session.SaveFeatures(features);
            StatusText.Text = L.T("Saved. The change takes effect with the next build (Ctrl+S).");
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(Window.GetWindow(this)!, ex.Message);
            Show(_session.Project.Settings.Features);
        }
    }
}
