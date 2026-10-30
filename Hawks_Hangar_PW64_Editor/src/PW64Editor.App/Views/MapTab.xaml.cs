using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PW64Editor.App.Dialogs;
using PW64Editor.App.Services;
using PW64Editor.Core.Map;
using PW64Editor.Core.Map.Rendering;
using PW64Editor.Core.Missions;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Text;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// The tab "Map": an island with its decoration and the objects of one mission (or of all missions of a vehicle on
/// that island), drawn by the editor itself; the object list with the details of the chosen object, and the bar with
/// the mouse, camera and object tools.
/// </summary>
/// <remarks>
/// The map is drawn by the Core's <see cref="MapRenderer"/> (the user's choice: the editor's own drawing) into a bitmap.
/// In this step the map shows and chooses objects; moving and turning them follow.
/// </remarks>
public partial class MapTab : UserControl
{
    /// <summary>One entry of the object list.</summary>
    /// <param name="Name">The shown name.</param>
    /// <param name="Group">The group header (mission and object type).</param>
    /// <param name="Pick">The pick number in the scene.</param>
    public sealed record ObjectRow(string Name, string Group, int Pick);

    /// <summary>One entry of the mission choice.</summary>
    /// <param name="Label">The shown text.</param>
    /// <param name="Missions">The missions whose objects are shown (none: decoration only).</param>
    public sealed record MissionChoice(string Label, IReadOnlyList<MissionInfo> Missions);

    private readonly ObservableCollection<ObjectRow> _rows = [];
    private readonly ListCollectionView _rowView;
    private EditorSession? _session;
    private MapSceneBuilder? _builder;
    private MapScene? _scene;
    private IReadOnlyList<MissionInfo> _missions = [];
    private GameTextLibrary? _texts;
    private string? _filesStamp;
    private int _buildGeneration;
    private int _lastIsland = -1;
    // True while controls are filled in code (and while InitializeComponent sets the XAML values: a slider reports its
    // value before the other controls exist), so their change handlers do nothing.
    private bool _filling = true;

    public MapTab()
    {
        InitializeComponent();
        _controller = new MapCameraController(_camera);
        _rowView = new ListCollectionView(_rows);
        _rowView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ObjectRow.Group)));
        _rowView.Filter = MatchesSearch;
        ObjectList.ItemsSource = _rowView;
        SetUpView();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _session is not null && _session.FilesStamp() != _filesStamp)
            {
                Load(_session);
            }
        };
    }

    /// <summary>Shows a project (reads its files anew).</summary>
    public void Load(EditorSession session)
    {
        _session = session;
        _filesStamp = session.FilesStamp();
        _builder = new MapSceneBuilder(session.CreateMapLibrary());
        try
        {
            _missions = session.LoadMissions().Missions;
            _texts = session.LoadTexts();
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            _missions = [];
            _texts = null;
            MapMessage.Text = L.F("The missions could not be read: {0}", ex.Message);
        }

        _filling = true;
        int island = IslandBox.SelectedIndex;
        object? mission = MissionBox.SelectedItem;
        IslandBox.ItemsSource = MapIsland.WithAreas.Select(IslandLabel).ToList();
        IslandBox.SelectedIndex = island >= 0 ? island : 0;
        FillMissionChoices((mission as MissionChoice)?.Label);
        _filling = false;
        BuildScene(keepCamera: _lastIsland == IslandBox.SelectedIndex);
    }

    private MapIsland Island => MapIsland.WithAreas[Math.Clamp(IslandBox.SelectedIndex, 0, MapIsland.WithAreas.Count - 1)];

    /// <summary>"Crescent Island", "Crescent Island – cave and volcano" …</summary>
    private static string IslandLabel(MapIsland island)
    {
        string name = MissionCatalog.IslandName(island.Number);
        if (island.Area == 0)
        {
            return name;
        }

        return island.Number == 1 ? L.F("{0} – cave and volcano", name) : L.F("{0} – ice cave", name);
    }

    private void OnIslandChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling)
        {
            return;
        }

        _filling = true;
        FillMissionChoices(null);
        _filling = false;
        BuildScene(keepCamera: false);
    }

    private void OnMissionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling)
        {
            BuildScene(keepCamera: true);
        }
    }

    /// <summary>The choices: decoration only, all missions of each vehicle on the island, then every single mission.</summary>
    private void FillMissionChoices(string? keep)
    {
        if (_session is null)
        {
            return;
        }

        List<MissionInfo> here = _missions.Where(m => _session.MissionIsland(m) == Island.Number)
            .OrderBy(m => m.Id.Vehicle).ThenBy(m => m.Id.Class).ThenBy(m => m.Id.Test).ToList();
        var choices = new List<MissionChoice> { new(L.T("Decoration only (no mission objects)"), []) };
        foreach (IGrouping<Vehicle, MissionInfo> vehicle in here.GroupBy(m => m.Id.Vehicle).Where(g => g.Count() > 1))
        {
            choices.Add(new MissionChoice(L.F("All missions: {0}", Vehicles.Name(vehicle.Key)), [.. vehicle]));
        }

        choices.AddRange(here.Select(m => new MissionChoice(MissionLabel(m), [m])));
        MissionBox.ItemsSource = choices;
        MissionBox.DisplayMemberPath = nameof(MissionChoice.Label);
        MissionChoice? chosen = choices.FirstOrDefault(c => c.Label == keep) ?? choices.ElementAtOrDefault(choices.Count > 1 ? 1 : 0);
        MissionBox.SelectedItem = chosen;
    }

    /// <summary>"Hang Glider · Class A · Test 1 – Flame Hunt".</summary>
    private string MissionLabel(MissionInfo mission)
    {
        string? title = mission.TitleTextName is { } name ? _texts?.Texts.FirstOrDefault(t => !t.IsFree && t.Name == name)?.Markup : null;
        string text = title is null ? mission.File.Name : Regex.Replace(title.Replace("[b]", string.Empty).Replace("[/b]", string.Empty), @"\[[^\]]*\]", " ").Replace('\n', ' ').Trim();
        return $"{Vehicles.Name(mission.Id.Vehicle)} · {MissionCatalog.ClassName(mission.Id.Vehicle, mission.Id.Class)} · {MissionCatalog.TestName(mission.Id.Vehicle, mission.Id.Test)} – {text}";
    }

    private IReadOnlyList<MissionInfo> ShownMissions => (MissionBox.SelectedItem as MissionChoice)?.Missions ?? [];

    /// <summary>Builds the scene of the chosen island and missions in the background, then shows it.</summary>
    private async void BuildScene(bool keepCamera)
    {
        if (_builder is null)
        {
            return;
        }

        int generation = ++_buildGeneration;
        MapIsland island = Island;
        IReadOnlyList<MissionInfo> missions = ShownMissions;
        MapSceneBuilder builder = _builder;
        MapMessage.Text = L.T("Reading the island…");
        MapScene scene;
        try
        {
            scene = await Task.Run(() => builder.Build(island,
                missions.Select(m => new SceneMission(m, m.Id.Vehicle, MapObject.ReadAll(m.File)))));
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex) || ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            if (generation == _buildGeneration)
            {
                MapMessage.Text = L.F("The island could not be read: {0}", ex.Message);
            }

            return;
        }

        if (generation != _buildGeneration)
        {
            return; // another island or mission was chosen in the meantime
        }

        object? selected = _selectedPick >= 0 && _scene is not null && _selectedPick < _scene.Picks.Count ? _scene.Picks[_selectedPick] : null;
        _scene = scene;
        MapMessage.Text = string.Empty;
        _controller.GroundLevel = 0;
        if (!keepCamera || _lastIsland != IslandBox.SelectedIndex)
        {
            _controller.ShowAll(scene.Bounds);
        }

        _lastIsland = IslandBox.SelectedIndex;
        FillObjectList();
        Select(selected is null ? -1 : FindSamePick(selected), fromList: false);
        RequestRender();
    }

    /// <summary>The pick of the same thing in the new scene (objects are records, so an unchanged object compares equal).</summary>
    private int FindSamePick(object old)
    {
        if (_scene is null)
        {
            return -1;
        }

        for (int i = 0; i < _scene.Picks.Count; i++)
        {
            object pick = _scene.Picks[i];
            bool same = (pick, old) switch
            {
                (MissionObjectPick a, MissionObjectPick b) => ReferenceEquals(a.Mission, b.Mission) && a.Object.Kind == b.Object.Kind && a.Object.Index == b.Object.Index,
                (DecorationPick a, DecorationPick b) => a.Cell == b.Cell && a.Index == b.Index,
                (IslandObjectPick a, IslandObjectPick b) => a.Kind == b.Kind && a.Index == b.Index,
                (IslandExtraPick a, IslandExtraPick b) => a == b,
                _ => false,
            };
            if (same)
            {
                return i;
            }
        }

        return -1;
    }

    // ----------------------------------------------------------------- object list and details

    private void FillObjectList()
    {
        _filling = true;
        _rows.Clear();
        if (_scene is not null)
        {
            bool severalMissions = ShownMissions.Count > 1;
            bool overviewOnly = LayerBox.SelectedIndex == (int)MapLayerMode.MapModel;
            bool invisible = InvisibleBox.IsChecked == true;
            HashSet<int> markers = overviewOnly ? [.. _scene.Items.Where(i => i.Layer == SceneLayer.MapMarker).Select(i => i.Pick)] : [];
            var rows = new List<ObjectRow>();
            for (int i = 0; i < _scene.Picks.Count; i++)
            {
                object thing = _scene.Picks[i];
                // The list shows what the map shows: on the overview map only the mission objects with a marker there,
                // invisible things only while they are shown.
                if ((overviewOnly && !markers.Contains(i)) || (!invisible && IsInvisible(thing)))
                {
                    continue;
                }

                rows.Add(thing switch
                {
                    MissionObjectPick m => new ObjectRow(MissionObjectName(m.Object),
                        severalMissions && m.Mission is MissionInfo info ? $"{MissionCatalog.ClassName(info.Id.Vehicle, info.Id.Class)} · {MissionCatalog.TestName(info.Id.Vehicle, info.Id.Test)}: {L.T(m.Object.Type.PluralName)}" : L.T(m.Object.Type.PluralName), i),
                    IslandObjectPick o => new ObjectRow(IslandObjectName(o), L.T("Island"), i),
                    IslandExtraPick e => new ObjectRow(L.T(e.Extra.Name), L.T("Island (placed by the game's code)"), i),
                    DecorationPick d => new ObjectRow(DecorationName(d), L.F("Decoration, tile {0}", TileName(d.Cell)), i),
                    _ => new ObjectRow(L.T("Object"), L.T("Other"), i),
                });
            }

            // Mission objects first, then the island's own objects, then the decoration by tile.
            foreach (ObjectRow row in rows.OrderBy(r => _scene.Picks[r.Pick] switch { MissionObjectPick => 0, IslandObjectPick => 1, IslandExtraPick => 2, _ => 3 }))
            {
                _rows.Add(row);
            }
        }

        _filling = false;
        FillDetailTypes();
    }

    private static string TileName(TerrainCell cell) => $"{(char)('A' + cell.Column)}{cell.Row + 1}";

    private static string DecorationName(DecorationPick d)
    {
        string name = ModelNames.Of(d.Model.Model);
        return name.Length > 0 ? $"{name} (#{d.Index + 1})" : L.F("Model {0} (#{1})", d.Model.Model, d.Index + 1);
    }

    private static string IslandObjectName(IslandObjectPick o) => o.Kind switch
    {
        IslandObjectKind.PadPlace => L.F("Landing pad place {0}", o.Index + 1),
        IslandObjectKind.WindObject => L.F("Wind object {0}", o.Index + 1),
        IslandObjectKind.Toy => L.F("Animated decoration {0}", o.Index + 1),
        IslandObjectKind.Warp => o.Data is IslandWarp { FrontArea: < 0 } or IslandWarp { BackArea: < 0 } ? L.F("Warp point {0}", o.Index + 1) : L.F("Area switch {0}", o.Index + 1),
        IslandObjectKind.SoundZone => L.F("Sound zone {0}", o.Index + 1),
        IslandObjectKind.SoundEmitter => L.F("Sound emitter {0}", o.Index + 1),
        _ => L.T("Bonus star"),
    };

    /// <summary>True for things the game does not show (trigger zones, warp points, sound zones, toy places).</summary>
    private static bool IsInvisible(object thing) => thing switch
    {
        IslandObjectPick { Kind: IslandObjectKind.Toy or IslandObjectKind.Warp or IslandObjectKind.SoundZone or IslandObjectKind.SoundEmitter } => true,
        IslandExtraPick { Extra.Kind: IslandExtraKind.Sphere or IslandExtraKind.Box } => true,
        _ => false,
    };

    /// <summary>"Ring 3", with the special rings named: "Ring 7 (goal ring)", "Ring 2 (invisible check ring)".</summary>
    private static string MissionObjectName(MapObject o)
    {
        if (o.Kind != MissionObjectKind.Ring)
        {
            return o.DisplayName;
        }

        return o.Byte(0x72) switch
        {
            2 => L.F("{0} (invisible check ring)", o.DisplayName),
            3 => L.F("{0} (goal ring)", o.DisplayName),
            _ when o.Byte(0x55) == 0 => L.F("{0} (inactive at the start)", o.DisplayName),
            _ => o.DisplayName,
        };
    }

    /// <summary>True if a row's name or group contains the search text.</summary>
    private bool MatchesSearch(object item)
    {
        string search = ObjectSearchBox?.Text.Trim() ?? string.Empty;
        return search.Length == 0 || item is not ObjectRow row
            || row.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || row.Group.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnObjectSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_rowView is null)
        {
            return;
        }

        _filling = true;
        _rowView.Refresh();
        ObjectList.SelectedItem = _rows.FirstOrDefault(r => r.Pick == _selectedPick);
        _filling = false;
    }

    private void OnObjectListChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling && ObjectList.SelectedItem is ObjectRow row)
        {
            Select(row.Pick, fromList: true);
        }
    }

    /// <summary>The groups of the detail dropdown: the groups of the object list.</summary>
    private void FillDetailTypes()
    {
        _filling = true;
        DetailTypeBox.ItemsSource = _rows.Select(r => r.Group).Distinct().ToList();
        _filling = false;
    }

    private void OnDetailTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || DetailTypeBox.SelectedItem is not string group)
        {
            return;
        }

        _filling = true;
        DetailObjectBox.ItemsSource = _rows.Where(r => r.Group == group).ToList();
        DetailObjectBox.DisplayMemberPath = nameof(ObjectRow.Name);
        _filling = false;
        if (_rows.FirstOrDefault(r => r.Group == group) is { } first)
        {
            Select(first.Pick, fromList: false);
        }
    }

    private void OnDetailObjectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling && DetailObjectBox.SelectedItem is ObjectRow row)
        {
            Select(row.Pick, fromList: false);
        }
    }

    /// <summary>Chooses a thing of the scene (−1: nothing): list, detail dropdowns, details, handles.</summary>
    private void Select(int pick, bool fromList)
    {
        _selectedPick = pick;
        _filling = true;
        ObjectRow? row = _rows.FirstOrDefault(r => r.Pick == pick);
        if (!fromList)
        {
            ObjectList.SelectedItem = row;
            if (row is not null)
            {
                ObjectList.ScrollIntoView(row);
            }
        }

        if (row is not null)
        {
            DetailTypeBox.SelectedItem = row.Group;
            DetailObjectBox.ItemsSource = _rows.Where(r => r.Group == row.Group).ToList();
            DetailObjectBox.DisplayMemberPath = nameof(ObjectRow.Name);
            DetailObjectBox.SelectedItem = row;
        }
        else
        {
            DetailObjectBox.ItemsSource = null;
        }

        _filling = false;
        ShowDetails();
        // Orbit mode: the view jumps to the chosen object, keeping its direction and its distance to the object before.
        float distance = Vector3.Distance(_camera.Position, _controller.FocusPoint());
        _controller.Target = SelectedPosition;
        if (_controller.Orbit && SelectedPosition is { } target)
        {
            _controller.LookAt(target, Math.Clamp(distance, 3, 40000));
        }

        UpdateTransformRows();
        RequestRender();
    }

    private object? SelectedThing => _scene is not null && _selectedPick >= 0 && _selectedPick < _scene.Picks.Count ? _scene.Picks[_selectedPick] : null;

    /// <summary>Where the chosen thing is (game units), for its handles and the orbit.</summary>
    private Vector3? SelectedPosition => SelectedThing switch
    {
        MissionObjectPick m when m.Object.Kind == MissionObjectKind.LandingPad => _scene!.BoundsOf(_selectedPick) is { IsEmpty: false } b ? b.Center with { Z = b.Min.Z } : m.Object.Position,
        MissionObjectPick m => m.Object.Position,
        DecorationPick d => d.Model.Placement.Translation + d.Cell.Center,
        IslandObjectPick o => o.Position,
        IslandExtraPick e => e.Position,
        _ => null,
    };

    /// <summary>The rotation of the chosen thing (for handles along its own axes), if it has one.</summary>
    private Matrix4x4? SelectedRotation => SelectedThing switch
    {
        MissionObjectPick m when m.Object.Type.CanRotate => MapMath.Rotation(m.Object.Angles.X, m.Object.Angles.Y, m.Object.Angles.Z),
        DecorationPick d => WithoutScale(d.Model.Placement),
        IslandObjectPick { Data: IslandPadPlace pad } => MapMath.Rotation(pad.Heading, 0, 0),
        IslandObjectPick { Data: IslandWarp warp } => MapMath.Rotation(warp.Heading, 0, 0),
        IslandExtraPick e => MapMath.Rotation(e.Extra.StartHeading, 0, e.Extra.Roll),
        _ => null,
    };

    private static Matrix4x4 WithoutScale(Matrix4x4 m)
    {
        Vector3 x = Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13));
        Vector3 y = Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23));
        Vector3 z = Vector3.Normalize(new Vector3(m.M31, m.M32, m.M33));
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    /// <summary>The angles of the chosen thing (heading, pitch, roll), if it has any.</summary>
    private Vector3? SelectedAngles => SelectedThing switch
    {
        MissionObjectPick m when m.Object.Type.CanRotate => m.Object.Angles,
        _ => null,
    };

    private void ShowDetails()
    {
        DetailGrid.Children.Clear();
        DetailGrid.RowDefinitions.Clear();
        DetailGrid.ColumnDefinitions.Clear();
        DetailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        DetailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        DetailNote.Text = string.Empty;

        var lines = new List<(string Name, string Value)>();
        switch (SelectedThing)
        {
            case MissionObjectPick m:
                if (m.Mission is MissionInfo info && ShownMissions.Count > 1)
                {
                    lines.Add((L.T("Mission"), MissionLabel(info)));
                }

                foreach (MapField field in m.Object.Type.Fields)
                {
                    lines.Add((L.T(field.Name), m.Object.Format(field)));
                }

                DetailNote.Text = Note(m.Object);
                break;
            case DecorationPick d:
                Vector3 p = d.Model.Placement.Translation + d.Cell.Center;
                lines.Add((L.T("Model"), $"{d.Model.Model} (0x{d.Model.Model:X3}) {ModelNames.Of(d.Model.Model)}"));
                lines.Add((L.T("Tile"), TileName(d.Cell)));
                lines.Add((L.T("Contour"), d.Cell.Contour.ToString()));
                lines.Add((L.T("Position"), Meters(p)));
                DetailNote.Text = L.T("A decoration object stands on the island's terrain; it is the same in every mission on this island.");
                break;
            case IslandObjectPick o:
                lines.Add((L.T("Position"), Meters(o.Position)));
                AddIslandDetails(o, lines);
                DetailNote.Text = IslandNote(o);
                break;
            case IslandExtraPick e:
                IslandExtra extra = e.Extra;
                if (extra.Model is { } model)
                {
                    lines.Add((L.T("Model"), $"{model} (0x{model:X3}) {ModelNames.Of(model)}"));
                }

                if (extra.Kind == IslandExtraKind.Path)
                {
                    lines.Add((L.T("Start"), Meters(e.Position)));
                    lines.Add((L.T("Path file"), L.F("User file {0} (SPTH)", extra.PathFile)));
                    if (extra.Count > 1)
                    {
                        lines.Add((L.T("Models on the path"), extra.Count.ToString()));
                    }
                }
                else if (extra.Kind == IslandExtraKind.Fire)
                {
                    lines.Add((L.T("Base"), Meters(extra.Position)));
                    lines.Add((L.T("Width"), MetersOf(extra.Size.X)));
                    lines.Add((L.T("Height"), MetersOf(extra.Size.Z)));
                }
                else if (extra.Kind == IslandExtraKind.Mover)
                {
                    lines.Add((L.T("Centre of the circle"), Meters(extra.Position)));
                    lines.Add((L.T("Radius"), MetersOf(extra.Radius)));
                    lines.Add((L.T("Speed"), L.F("{0:0.##}° per second", extra.Speed)));
                    lines.Add((L.T("Start"), Meters(extra.StartPosition)));
                }
                else
                {
                    lines.Add((L.T("Position"), Meters(extra.Position)));
                }

                if (extra.Kind == IslandExtraKind.Sphere)
                {
                    lines.Add((L.T("Radius"), MetersOf(extra.Radius)));
                }

                if (extra.Kind == IslandExtraKind.Box)
                {
                    lines.Add((L.T("Size"), Meters(extra.Size)));
                    lines.Add((L.T("Heading"), $"{extra.Heading:0.##}°"));
                }

                lines.Add((L.T("Source"), extra.Source));
                DetailNote.Text = L.T(extra.Note) + " " + L.T("The game places this object in its code; it cannot be changed here.");
                break;
            default:
                DetailNote.Text = _scene is null ? string.Empty : L.T("Choose an object in the list or on the map.");
                break;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            DetailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = lines[i].Name, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 8, 1), Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush") };
            var value = new TextBlock { Text = lines[i].Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
            Grid.SetRow(name, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            DetailGrid.Children.Add(name);
            DetailGrid.Children.Add(value);
        }
    }

    /// <summary>The details of the island's own objects.</summary>
    private static void AddIslandDetails(IslandObjectPick o, List<(string Name, string Value)> lines)
    {
        switch (o.Data)
        {
            case IslandPadPlace pad:
                lines.Add((L.T("Heading"), $"{pad.Heading:0.##}°"));
                break;
            case IslandWarp warp:
                lines.Add((L.T("Radius"), MetersOf(warp.Radius)));
                lines.Add((L.T("Heading"), $"{warp.Heading:0.##}°"));
                lines.Add((L.T("Front side"), AreaName(warp.FrontArea)));
                lines.Add((L.T("Back side"), AreaName(warp.BackArea)));
                lines.Add((L.T("Offset"), Meters(warp.Offset)));
                lines.Add((L.T("Only when crossing"), warp.Mode == 1 ? L.T("Yes") : L.T("No")));
                break;
            case IslandSoundEmitter emitter:
                lines.Add((L.T("Shape"), emitter.IsLine ? L.T("Line") : L.T("Point")));
                if (emitter.IsLine)
                {
                    lines.Add((L.T("From"), Meters(emitter.A)));
                    lines.Add((L.T("To"), Meters(emitter.B)));
                }

                lines.Add((L.T("Sound"), emitter.Sound.ToString()));
                lines.Add((L.T("Full volume within"), MetersOf(emitter.Near)));
                lines.Add((L.T("Silent from"), MetersOf(emitter.Far)));
                lines.Add((L.T("Area"), emitter.Zone == 10 ? L.T("Everywhere") : AreaName(emitter.Zone > 1 ? 0 : emitter.Zone)));
                break;
            case IslandSoundZone zone:
                lines.Add((L.T("Radius"), MetersOf(zone.Radius)));
                lines.Add((L.T("Type"), zone.Type == 1 ? L.T("Flying under something") : L.T("Volume of the surroundings")));
                break;
        }
    }

    private static string AreaName(int area) => area switch
    {
        < 0 => L.T("No switch (only the warp)"),
        0 => L.T("Island"),
        _ => L.T("Sub-area (cave)"),
    };

    private static string IslandNote(IslandObjectPick o) => o.Kind switch
    {
        IslandObjectKind.PadPlace => L.T("A place of the island where a mission's landing pad can be: the mission's pad takes the place within 100 units of its position."),
        IslandObjectKind.Toy => L.T("The place of an animated decoration object (merry-go-round, ferris wheel, pump jack …): within 100 units the game moves its parts."),
        IslandObjectKind.Warp => L.T("Inside the sphere the side of the arrow decides the area: front or back. When the area changes, the offset is added to the player's position (the cyan line leads there)."),
        IslandObjectKind.SoundZone => L.T("A sound zone: flying under something here plays a whoosh, or the sound of the surroundings fades."),
        IslandObjectKind.SoundEmitter => L.T("A sound of the surroundings (surf, bells, music …): full volume within the inner circle, silent beyond the outer one. A line sounds along its whole length."),
        _ => L.T("This object belongs to the island; it is the same in every mission on this island."),
    };

    private static string MetersOf(float units) => (units * GameUnits.MetersPerUnit).ToString("0.##") + " m";

    /// <summary>A hint about the rules the game has for an object.</summary>
    private static string Note(MapObject o) => o.Kind switch
    {
        MissionObjectKind.LandingPad => L.T("The game moves a mission's landing pad to the island's landing pad place within 100 units; without one the pad does not work (shown as a red circle)."),
        MissionObjectKind.TakeoffPad => L.T("A start on the ground has to lie on the ground: the game looks for the ground only half a unit around its height."),
        MissionObjectKind.Ring when o.Byte(0x72) == 2 => L.T("An invisible check ring: no model, no radar mark and no points; passing it activates its child rings."),
        MissionObjectKind.Ring when o.Byte(0x72) == 3 => L.T("The goal ring: passing it ends the mission. While it is inactive the game shows a grey locked ring."),
        MissionObjectKind.Ring => L.T("Child rings are alternatives: passing one removes its siblings. Yellow lines lead to the child rings, blue ones to the timed child rings."),
        MissionObjectKind.MecaHawk => L.T("Each record is a domain (sphere) of the one Meca Hawk; the record where it starts also sets its behaviour."),
        MissionObjectKind.LocalWind => L.T("The blue line shows the direction of the wind."),
        _ => string.Empty,
    };

    // ----------------------------------------------------------------- map info

    private void OnMapInfo(object sender, RoutedEventArgs e)
    {
        if (_scene is null || _builder is null)
        {
            return;
        }

        MapStatistics s = MapStatistics.Of(_scene, _builder.Library, Island);
        var text = new System.Text.StringBuilder();
        text.AppendLine(IslandLabel(Island));
        text.AppendLine();
        text.AppendLine(L.F("Terrain: {0} tiles, {1} different contours, {2} triangles, {3} vertices", s.Tiles, s.Contours, s.TerrainTriangles, s.TerrainVertices));
        text.AppendLine(L.F("Decoration: {0} objects of {1} different models, {2} triangles", s.DecorationObjects, s.DecorationModels, s.DecorationTriangles));
        text.AppendLine(L.F("Textures of terrain and decoration: {0} ({1:N0} texels)", s.Textures, s.Texels));
        text.AppendLine(L.F("Island objects: {0} from the island's file, {1} placed by the game's code", s.IslandObjects, s.CodeObjects));
        text.AppendLine(L.F("Mission objects shown: {0}", s.MissionObjects));
        text.AppendLine();
        text.AppendLine(L.F("The level loads {0} models, {1} contours and {2} textures: {3:N0} KB as stored in the ROM.", s.LevelModels, s.LevelContours, s.LevelTextures, s.LevelBytes / 1024));
        if (IslandBudget.Of(Island.Number) is { } budget)
        {
            text.AppendLine(L.F("Measured in the game (unchanged island): files {0:N0} KB unpacked, {1:N0} KB packed; heap in flight {2:N0} KB of {3:N0} KB with the Expansion Pak; {4} display objects of the island's own.",
                budget.FilesUnpacked / 1024, budget.FilesPacked / 1024, budget.HeapInFlight / 1024, IslandBudget.Heap / 1024, budget.DisplayObjects));
        }

        TextReportWindow.ShowReport(Window.GetWindow(this), L.T("Map info"), text.ToString());
    }

    private static string Meters(Vector3 p) =>
        string.Join(" / ", new[] { p.X, p.Y, p.Z }.Select(v => (v * GameUnits.MetersPerUnit).ToString("0.##"))) + " m";
}
