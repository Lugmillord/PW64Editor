using System.Windows;
using System.Windows.Controls;
using PW64Editor.App.Services;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>What a change of the mission list did: the mission to show afterwards, the status line and where the missions went.</summary>
public sealed record MissionListChange(TestId? Select, string Status, SavePlaceChanges? Places = null);

/// <summary>A request of the Missions tab to add or remove a mission (the main window saves or discards the edits first).</summary>
/// <param name="action">What is done, for the question about unsaved changes ("adding a mission").</param>
/// <param name="change">The change, run on the session after the question.</param>
public sealed class MissionListChangeEventArgs(string action, Func<EditorSession, MissionListChange> change) : EventArgs
{
    /// <summary>What is done (English, translated by the main window).</summary>
    public string Action { get; } = action;

    /// <summary>The change.</summary>
    public Func<EditorSession, MissionListChange> Change { get; } = change;
}

/// <summary>The number of missions, adding a copy of a mission and removing one (stage 7).</summary>
public partial class MissionsTab
{
    /// <summary>The mission whose class choice is shown (the choice starts at the mission's class when another mission is shown).</summary>
    private TestId? _copyClassOf;

    /// <summary>The mission to show after the next <see cref="Load"/>.</summary>
    private TestId? _selectAfterLoad;

    /// <summary>Raised when the user adds or removes a mission. The main window runs the change and reloads all tabs.</summary>
    public event EventHandler<MissionListChangeEventArgs>? MissionListChangeRequested;

    /// <summary>Shows this mission after the next reload (a new mission, the mission behind a removed one).</summary>
    public void SelectAfterLoad(TestId? id) => _selectAfterLoad = id;

    private void ShowMissionList(MissionInfo? mission)
    {
        MissionCountText.Text = L.F("Missions: {0} / {1}", _missions.Count, ProjectMissions.MaxMissions);
        bool offered = mission is not null && _session is not null && MissionPositions.CanAddTests(mission.Id.Vehicle);
        Visibility visibility = offered ? Visibility.Visible : Visibility.Collapsed;
        CopyClassBox.Visibility = visibility;
        AddMissionButton.Visibility = visibility;
        RemoveMissionButton.Visibility = visibility;
        CopyIntoLabel.Visibility = visibility;

        if (!offered)
        {
            MissionListNote.Visibility = Visibility.Collapsed;
            return;
        }

        if (_copyClassOf != mission!.Id || CopyClassBox.ItemsSource is null)
        {
            bool filling = _filling;
            _filling = true;
            List<ChoiceItem<int>> classes = [.. Enumerable.Range(0, 4).Select(c => new ChoiceItem<int>(c, MissionCatalog.ClassName(mission.Id.Vehicle, c)))];
            CopyClassBox.ItemsSource = classes;
            CopyClassBox.SelectedItem = classes[Math.Clamp(mission.Id.Class, 0, 3)];
            _filling = filling;
            _copyClassOf = mission.Id;
        }

        UpdateMissionListButtons(mission);
    }

    private void UpdateMissionListButtons(MissionInfo mission)
    {
        EditorSession session = _session!;
        int target = (CopyClassBox.SelectedItem as ChoiceItem<int>)?.Value ?? mission.Id.Class;
        string? cannotAdd, cannotRemove;
        int? island = null;
        try
        {
            cannotAdd = session.CannotAddMission(mission.Id.Vehicle, target);
            cannotRemove = session.CannotRemoveMission(mission);
            island = session.ClassIsland(mission.Id.Vehicle, target);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            cannotAdd = cannotRemove = L.F("The missions could not be read: {0}", ex.Message);
        }

        AddMissionButton.IsEnabled = cannotAdd is null;
        RemoveMissionButton.IsEnabled = cannotRemove is null;
        AddMissionButton.ToolTip = cannotAdd ?? L.T("Adds a copy of this mission as the last test of the chosen class: file, settings, title, task, hint and point texts.");
        RemoveMissionButton.ToolTip = cannotRemove ?? L.T("Removes this mission. The later tests of the class move up one place, with their texts and settings.");

        var notes = new List<string>();
        if (cannotAdd is not null && cannotAdd == cannotRemove)
        {
            notes.Add(cannotAdd); // the game patch is missing: one line for both buttons
        }
        else
        {
            if (cannotAdd is not null)
            {
                notes.Add(L.F("Add copy: {0}", cannotAdd));
            }

            if (cannotRemove is not null)
            {
                notes.Add(L.F("Remove: {0}", cannotRemove));
            }
        }

        if (cannotAdd is null && island is { } classIsland && classIsland != mission.Island)
        {
            notes.Add(L.F("The copy is played on {0}, the island of {1}; the objects of this mission were placed for {2}.",
                MissionCatalog.IslandName(classIsland), MissionCatalog.ClassName(mission.Id.Vehicle, target), MissionCatalog.IslandName(mission.Island)));
        }

        MissionListNote.Text = string.Join("\n", notes);
        MissionListNote.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCopyClassChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling && SelectedMission is { } mission && _session is not null)
        {
            UpdateMissionListButtons(mission);
        }
    }

    private void OnAddMission(object sender, RoutedEventArgs e)
    {
        if (SelectedMission is not { } mission || CopyClassBox.SelectedItem is not ChoiceItem<int> target)
        {
            return;
        }

        TestId source = mission.Id;
        int classIndex = target.Value;
        MissionListChangeRequested?.Invoke(this, new MissionListChangeEventArgs("adding a mission", session =>
        {
            MissionInfo current = session.LoadMissions().Missions.Single(m => m.Id == source);
            TestId added = session.AddMission(current, classIndex);
            return new MissionListChange(added, L.F("Mission added: {0}.", MissionCatalog.Label(added)));
        }));
    }

    private void OnRemoveMission(object sender, RoutedEventArgs e)
    {
        if (SelectedMission is not { } mission)
        {
            return;
        }

        if (!Ui.Confirm(Window.GetWindow(this)!, L.F(
                "Remove {0}?\n\nThe later tests of the class move up one place, with their texts and settings. A mission the project added is deleted; a mission of the game stays in the ROM but is no longer loaded. Only a restore point brings it back.",
                MissionCatalog.Label(mission.Id))))
        {
            return;
        }

        TestId removed = mission.Id;
        MissionListChangeRequested?.Invoke(this, new MissionListChangeEventArgs("removing a mission", session =>
        {
            MissionInfo current = session.LoadMissions().Missions.Single(m => m.Id == removed);
            SavePlaceChanges places = session.RemoveMission(current);
            int left = session.LoadMissions().Missions.Count(m => m.Id.Vehicle == removed.Vehicle && m.Id.Class == removed.Class);
            return new MissionListChange(removed with { Test = Math.Min(removed.Test, left - 1) },
                L.F("Mission removed: {0}.", MissionCatalog.Label(removed)), places);
        }));
    }
}
