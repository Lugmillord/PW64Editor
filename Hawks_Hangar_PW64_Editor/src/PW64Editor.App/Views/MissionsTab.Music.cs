using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PW64Editor.App.Services;
using PW64Editor.Core.Audio;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.App.Views;

/// <summary>
/// The section "Music" of the Missions tab: the mission's own songs for its flight and its jingles (crash, good and missed
/// landing; Cannonball hit and miss, Jumble Hopper goal, Birdman landings), see <see cref="MusicPlaces"/>.
/// </summary>
public partial class MissionsTab
{
    /// <summary>A song of the list: null for the place's song.</summary>
    /// <param name="Song">The song number, or null.</param>
    /// <param name="Text">What the list shows.</param>
    private sealed record MusicItem(int? Song, string Text)
    {
        public override string ToString() => Text;
    }

    private void ShowMusic(Core.Missions.MissionDraft draft)
    {
        MusicSongsPanel.Children.Clear();
        if (_session is null)
        {
            MusicPanel.Visibility = Visibility.Collapsed;
            return;
        }

        IReadOnlyList<MissionMusicKind> kinds = MusicPlaces.KindsOf(draft.Vehicle)
            .Where(k => draft.Vehicle != Vehicle.Birdman || draft.Settings.BirdmanPoints
                || k is MissionMusicKind.Flight or MissionMusicKind.Crash)
            .ToList();
        MusicPanel.Visibility = kinds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        IReadOnlyList<bool> loops, originalLoops;
        try
        {
            loops = _session.SongLoops();
            originalLoops = _session.OriginalSongLoops();
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            loops = originalLoops = [];
        }

        IReadOnlyDictionary<int, int> places = _session.Project.Settings.MusicPlaces;
        foreach (MissionMusicKind kind in kinds)
        {
            int place = MusicPlaces.Place(draft.Vehicle, kind)!.Value;
            int placeSong = MusicPlaces.SongAt(places, place);
            var line = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            line.Children.Add(new TextBlock { Text = L.T(MusicPlaces.KindName(draft.Vehicle, kind)) + ":", MinWidth = 190, VerticalAlignment = VerticalAlignment.Center });
            var box = new ComboBox { MinWidth = 320 };
            box.Items.Add(new MusicItem(null, L.F("Default ({0:D2} – {1})", placeSong, L.T(MusicPlaces.PlaceName(placeSong)))));
            for (int song = 0; song < MusicPlaces.Count; song++)
            {
                box.Items.Add(new MusicItem(song, L.F("{0:D2} – {1}", song, L.T(MusicPlaces.PlaceName(song)))));
            }

            int? own = draft.MusicOf(kind);
            box.SelectedIndex = own is { } s ? s + 1 : 0;
            box.SelectionChanged += (_, _) =>
            {
                if (_showing || box.SelectedItem is not MusicItem item || item.Song == draft.MusicOf(kind))
                {
                    return;
                }

                draft.SetMusic(kind, item.Song);
                Changed(draft);
            };
            line.Children.Add(box);
            MusicSongsPanel.Children.Add(line);

            if (MusicPlaces.Problem(place, own ?? placeSong, loops, originalLoops) is { } problem)
            {
                MusicSongsPanel.Children.Add(new TextBlock
                {
                    Text = problem,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("ChangedBrush"),
                    Margin = new Thickness(190, 0, 0, 2),
                });
            }
        }

        if (draft.Vehicle == Vehicle.Birdman && !draft.Settings.BirdmanPoints)
        {
            MusicSongsPanel.Children.Add(new TextBlock
            {
                Text = L.T("The Birdman's landings have jingles only in a mission with points (section \"Points\")."),
                Style = (Style)FindResource("Note"),
            });
        }
    }
}
