using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PW64Editor.App.Dialogs;
using PW64Editor.App.Services;
using PW64Editor.Core.Missions;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.App.Views;

/// <summary>
/// The section "Photos" of the Missions tab (Hang Glider, Birdman with points): photo mission or not, and up to ten photo
/// subjects, each with its object type, a single object, its points, an optional direction and its distance curve.
/// </summary>
public partial class MissionsTab
{
    private void ShowPhotos(MissionDraft draft)
    {
        bool hangGlider = draft.Vehicle == Vehicle.HangGlider;
        PhotoPanel.Visibility = hangGlider || draft.Vehicle == Vehicle.Birdman ? Visibility.Visible : Visibility.Collapsed;
        PhotoSubjectsPanel.Children.Clear();
        if (PhotoPanel.Visibility != Visibility.Visible || _session is null)
        {
            return;
        }

        bool gameRule = _session.IsGamePhotoMission(draft.Mission);
        PhotoMissionBox.IsChecked = draft.Settings.PhotoMission ?? gameRule;
        PhotoMissionNote.Text = hangGlider
            ? L.T("A photo mission says \"Photo\" when a photo gives points, shows the points in the album, marks its main subjects on the radar and offers sample photos on the test screen and in the pause menu (with own subjects its own, otherwise the island's).")
            : L.T("With photo subjects of its own, a Birdman photo mission says \"Photo\" when a photo gives points, shows the points in the album, marks its subjects on the radar and the map and offers their sample photos on the test screen and in the pause menu.");

        IReadOnlyList<PhotoSubject> photos = draft.Photos;
        bool editable = draft.CanEditPhotos;
        PhotoEditPanel.IsEnabled = editable;
        var notes = new List<string>();
        if (!editable)
        {
            notes.Add(L.T("The Birdman's photos give points only in a mission with points (section \"Points\")."));
        }

        notes.Add(draft.HasOwnPhotos
            ? L.T("The mission has photo subjects of its own: the editor's game code scores its photos.")
            : L.T("The game's photo subjects of the mission file: the game's own code scores them as long as they are not changed."));
        if (photos.Count > 0 && !draft.Rows.Contains(PointSource.Photo) && draft.HasScoreSheet)
        {
            notes.Add(L.T("The result sheet has no row \"Photo\": the photo points count, but are not shown. Add the row in the section \"Points\"."));
        }

        if (photos.Count == 0)
        {
            notes.Add(L.T("No photo subjects: photos give no points."));
        }
        else if (draft.HasOwnPhotos)
        {
            notes.Add(L.T("Each subject with points gets a sample photo (test screen and pause menu). It shows the object at the start of its movement (a whale of the pod above the water), so it is the same before and during the mission; if the object is seen badly from there, change the camera's direction or distance. A picture of it in the editor comes with the map editor."));
        }
        else
        {
            notes.Add(L.F("The best photo of each subject counts: at most {0} points.", PhotoSubjects.MaxPoints(photos)));
        }

        PhotoNote.Text = string.Join(" ", notes);

        int island = draft.Comm.Island;
        IReadOnlyList<PhotoSubjectKind> kinds = PhotoSubjects.OnIsland(island);
        for (int i = 0; i < photos.Count; i++)
        {
            PhotoSubjectsPanel.Children.Add(PhotoSubjectView(draft, photos, i, kinds, island));
        }

        AddPhotoKindBox.Items.Clear();
        foreach (PhotoSubjectKind kind in kinds)
        {
            AddPhotoKindBox.Items.Add(new ComboBoxItem { Content = PhotoSubjects.Name(kind), Tag = kind });
        }

        AddPhotoKindBox.SelectedIndex = kinds.Count > 0 ? 0 : -1;
        AddPhotoButton.IsEnabled = kinds.Count > 0 && photos.Count < PhotoSubjects.Max;
        AddPhotoButton.ToolTip = kinds.Count == 0
            ? L.T("The island of this mission has no objects to photograph.")
            : L.F("A mission has at most {0} photo subjects.", PhotoSubjects.Max);
        GamePhotosButton.IsEnabled = draft.HasOwnPhotos;
    }

    /// <summary>One subject: type, object, points, direction, distance curve.</summary>
    private UIElement PhotoSubjectView(MissionDraft draft, IReadOnlyList<PhotoSubject> photos, int index, IReadOnlyList<PhotoSubjectKind> kinds, int island)
    {
        PhotoSubject subject = photos[index];
        var box = new Border
        {
            BorderBrush = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
            BorderThickness = new Thickness(0, 0, 0, 0.5),
            Padding = new Thickness(0, 6, 0, 10),
        };
        var panel = new StackPanel();
        box.Child = panel;

        // The list with one subject changed (or removed); a problem is shown and the view rebuilt.
        void Apply(Func<PhotoSubject, PhotoSubject?> change)
        {
            if (_showing)
            {
                return;
            }

            var changed = new List<PhotoSubject>();
            for (int i = 0; i < photos.Count; i++)
            {
                PhotoSubject? s = i == index ? change(photos[i].Clone()) : photos[i].Clone();
                if (s is not null)
                {
                    changed.Add(s);
                }
            }

            if (draft.SetPhotos(changed) is { } problem)
            {
                Ui.ShowError(Window.GetWindow(this)!, problem);
                ShowMission(draft.Mission);
                return;
            }

            Changed(draft);
        }

        void HookBox(TextBox textBox, Action commit)
        {
            textBox.LostKeyboardFocus += (_, e) =>
            {
                _focusAfterRefresh = (e.NewFocus as FrameworkElement)?.Tag as string;
                commit();
            };
            textBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    commit();
                }
            };
        }

        // Line 1: type, object, points, remove.
        var line = new WrapPanel();
        line.Children.Add(new TextBlock { Text = L.F("Subject {0}:", index + 1), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var kindBox = new ComboBox { MinWidth = 170, Margin = new Thickness(0, 0, 12, 0) };
        foreach (PhotoSubjectKind kind in kinds.Contains(subject.Kind) ? kinds : kinds.Append(subject.Kind))
        {
            kindBox.Items.Add(new ComboBoxItem { Content = PhotoSubjects.Name(kind), Tag = kind });
        }

        kindBox.SelectedItem = kindBox.Items.OfType<ComboBoxItem>().First(i => i.Tag is PhotoSubjectKind k && k == subject.Kind);
        kindBox.SelectionChanged += (_, _) =>
        {
            if (kindBox.SelectedItem is ComboBoxItem { Tag: PhotoSubjectKind kind } && kind != subject.Kind)
            {
                Apply(s =>
                {
                    s.Kind = kind;
                    s.Instance = null;
                    s.Curve = PhotoSubjects.Default(kind, s.Points).Curve; // the distances suit each type's size
                    s.Sample = null;
                    return s;
                });
            }
        };
        line.Children.Add(kindBox);

        int count = PhotoSubjects.Count(subject.Kind, island);
        if (count > 1)
        {
            line.Children.Add(new TextBlock { Text = L.T("Object:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var objectBox = new ComboBox { MinWidth = 90, Margin = new Thickness(0, 0, 12, 0) };
            objectBox.Items.Add(new ComboBoxItem { Content = L.F("any of the {0}", count), Tag = -1 });
            for (int k = 0; k < count; k++)
            {
                objectBox.Items.Add(new ComboBoxItem { Content = L.F("number {0}", k + 1), Tag = k });
            }

            objectBox.SelectedIndex = subject.Instance is { } instance && instance < count ? instance + 1 : 0;
            objectBox.ToolTip = L.T("Several objects of this type are on the island: any of them, or only one. The number is their order in the game's list; which one is which is best checked in the game.");
            objectBox.SelectionChanged += (_, _) =>
            {
                if (objectBox.SelectedItem is ComboBoxItem { Tag: int k })
                {
                    int? chosen = k < 0 ? null : k;
                    if (chosen != subject.Instance)
                    {
                        Apply(s =>
                        {
                            s.Instance = chosen;
                            return s;
                        });
                    }
                }
            };
            line.Children.Add(objectBox);
        }

        line.Children.Add(new TextBlock { Text = L.T("Points:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var pointsBox = new TextBox { Text = subject.Points.ToString(CultureInfo.CurrentCulture), Width = 50, Margin = new Thickness(0, 0, 12, 0), Tag = $"photo{index}p" };
        HookBox(pointsBox, () =>
        {
            if (!int.TryParse(pointsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int points) || points is < 0 or > 126)
            {
                Ui.ShowError(Window.GetWindow(this)!, L.T("Points are whole numbers from 0 to 126 (the game stores at most 126 per test)."));
                ShowMission(draft.Mission);
                return;
            }

            if (points != subject.Points)
            {
                Apply(s =>
                {
                    s.Points = points;
                    return s;
                });
            }
        });
        line.Children.Add(pointsBox);

        var remove = new Button { Content = L.T("Remove"), Style = (Style)FindResource("DialogButton"), ToolTip = L.T("Remove this photo subject") };
        remove.Click += (_, _) => Apply(_ => null);
        line.Children.Add(remove);
        panel.Children.Add(line);

        // Line 2: direction.
        var directionLine = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var directionCheck = new CheckBox
        {
            Content = L.T("Only when the camera looks in the direction"),
            IsChecked = subject.Direction is not null,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        directionCheck.Click += (_, _) => Apply(s =>
        {
            s.Direction = directionCheck.IsChecked == true ? 0 : null;
            return s;
        });
        directionLine.Children.Add(directionCheck);
        if (subject.Direction is { } direction)
        {
            var directionBox = new TextBox { Text = FormatMeters(direction), Width = 50, Tag = $"photo{index}d" };
            var rangeBox = new TextBox { Text = FormatMeters(subject.Tolerance / 2), Width = 50, Tag = $"photo{index}r" };
            void CommitDirection()
            {
                if (!TryParseNumber(directionBox.Text, out double d) || !TryParseNumber(rangeBox.Text, out double half) || half <= 0 || half > 180)
                {
                    Ui.ShowError(Window.GetWindow(this)!, L.T("The direction is a number of degrees, the range more than 0 and at most 180 degrees."));
                    ShowMission(draft.Mission);
                    return;
                }

                float newDirection = directionBox.Text.Trim() == FormatMeters(direction) ? direction : (float)(((d % 360) + 360) % 360);
                float newTolerance = rangeBox.Text.Trim() == FormatMeters(subject.Tolerance / 2) ? subject.Tolerance : (float)(half * 2);
                if (newDirection != direction || newTolerance != subject.Tolerance)
                {
                    Apply(s =>
                    {
                        s.Direction = newDirection;
                        s.Tolerance = newTolerance;
                        return s;
                    });
                }
            }

            HookBox(directionBox, CommitDirection);
            HookBox(rangeBox, CommitDirection);
            directionLine.Children.Add(directionBox);
            directionLine.Children.Add(new TextBlock { Text = L.T("° ±"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            directionLine.Children.Add(rangeBox);
            directionLine.Children.Add(new TextBlock { Text = "°", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        }

        panel.Children.Add(directionLine);

        // Line 3: the camera of the sample photo.
        var sampleLine = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        PhotoSample sample = subject.Sample ?? PhotoSubjects.DefaultSample(subject);
        sampleLine.Children.Add(new TextBlock { Text = L.T("Sample photo: camera"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var sampleDistance = new TextBox { Text = FormatMeters(sample.Distance * GameUnits.MetersPerUnit), Width = 50, Tag = $"photo{index}sd" };
        var sampleHeading = new TextBox { Text = FormatMeters(sample.Heading), Width = 50, Tag = $"photo{index}sh" };
        var sampleHeight = new TextBox { Text = FormatMeters(sample.Elevation), Width = 50, Tag = $"photo{index}se" };
        void CommitSample()
        {
            if (!TryParseNumber(sampleDistance.Text, out double meters) || !TryParseNumber(sampleHeading.Text, out double heading)
                || !TryParseNumber(sampleHeight.Text, out double height))
            {
                Ui.ShowError(Window.GetWindow(this)!, L.T("Please enter numbers."));
                ShowMission(draft.Mission);
                return;
            }

            // An unchanged box keeps the stored value exactly (shown values are rounded).
            var typed = new PhotoSample(
                sampleDistance.Text.Trim() == FormatMeters(sample.Distance * GameUnits.MetersPerUnit) ? sample.Distance : (float)(meters / GameUnits.MetersPerUnit),
                sampleHeading.Text.Trim() == FormatMeters(sample.Heading) ? sample.Heading : (float)(((heading % 360) + 360) % 360),
                sampleHeight.Text.Trim() == FormatMeters(sample.Elevation) ? sample.Elevation : (float)height);
            if (typed != sample)
            {
                Apply(s =>
                {
                    s.Sample = typed;
                    return s;
                });
            }
        }

        HookBox(sampleDistance, CommitSample);
        HookBox(sampleHeading, CommitSample);
        HookBox(sampleHeight, CommitSample);
        sampleLine.Children.Add(sampleDistance);
        sampleLine.Children.Add(new TextBlock { Text = L.T("m away, direction"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        sampleLine.Children.Add(sampleHeading);
        sampleLine.Children.Add(new TextBlock { Text = L.T("°, height angle"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        sampleLine.Children.Add(sampleHeight);
        sampleLine.Children.Add(new TextBlock { Text = "°", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) });
        var defaultSample = new Button
        {
            Content = L.T("Default"),
            Style = (Style)FindResource("DialogButton"),
            IsEnabled = subject.Sample is not null,
            ToolTip = L.T("The editor's choice: for the game's subjects their sample photos from the original game, for the other types a distance at which the object fills the picture; if that distance gives no full points, the middle of the distances with full points."),
        };
        defaultSample.Click += (_, _) => Apply(s =>
        {
            s.Sample = null;
            return s;
        });
        sampleLine.Children.Add(defaultSample);
        panel.Children.Add(sampleLine);

        // Hints for the types that are hard to photograph.
        string? hint = subject.Kind switch
        {
            PhotoSubjectKind.Plane => L.T("Warning: the planes fly fast and high, a photo of them is hardly possible."),
            PhotoSubjectKind.WhalePod => L.T("A whale of the pod counts only while it is above the water (it dives again and again)."),
            _ => null,
        };
        if (hint is not null)
        {
            panel.Children.Add(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("ChangedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        }

        // Line 4: the distance curve in percent of the points.
        panel.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("Note"),
            Text = L.T("Share of the points by the distance between camera and subject:"),
            Margin = new Thickness(0, 6, 0, 2),
        });
        panel.Children.Add(CurveEditor(draft, ScoreCurveKind.Distance, L.T("Distance (m)"), 20, $"photo{index}c", GameUnits.MetersPerUnit,
            custom: ([.. subject.Curve], "%", points =>
            {
                if (points.Any(p => p.Y is < 0 or > 100))
                {
                    return L.T("The share is from 0 to 100 %.");
                }

                PhotoSubject changed = subject.Clone();
                changed.Curve = points;
                var list = photos.Select((s, i) => i == index ? changed : s.Clone()).ToList();
                return draft.SetPhotos(list);
            })));
        return box;
    }

    private void OnPhotoMissionChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || SelectedDraft is not { } draft || _session is null)
        {
            return;
        }

        draft.SetPhotoMission(PhotoMissionBox.IsChecked == true, _session.IsGamePhotoMission(draft.Mission));
        Changed(draft);
    }

    private void OnAddPhotoSubject(object sender, RoutedEventArgs e)
    {
        if (SelectedDraft is not { } draft || AddPhotoKindBox.SelectedItem is not ComboBoxItem { Tag: PhotoSubjectKind kind })
        {
            return;
        }

        // A new subject starts with 10 points (or what is left up to 100).
        int rest = 100 - draft.EvaluateRows(OtherComm).Where(r => r.Source != PointSource.Photo).Sum(draft.TextPoints) - PhotoSubjects.MaxPoints(draft.Photos);
        List<PhotoSubject> list = [.. draft.Photos.Select(s => s.Clone()), PhotoSubjects.Default(kind, Math.Clamp(rest, 1, 10))];
        if (draft.SetPhotos(list) is { } problem)
        {
            Ui.ShowError(Window.GetWindow(this)!, problem);
            return;
        }

        Changed(draft);
    }

    private void OnGamePhotoSubjects(object sender, RoutedEventArgs e)
    {
        if (SelectedDraft is not { } draft)
        {
            return;
        }

        draft.ResetPhotos();
        Changed(draft);
    }

    private void OnPhotoHelp(object sender, RoutedEventArgs e) => PhotoScoringWindow.ShowHelp(Window.GetWindow(this)!);
}
