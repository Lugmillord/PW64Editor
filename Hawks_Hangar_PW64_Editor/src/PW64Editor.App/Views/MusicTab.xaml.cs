using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PW64Editor.App.Services;
using PW64Editor.Core.Audio;
using PW64Editor.Core.Workspace;

namespace PW64Editor.App.Views;

/// <summary>
/// The tab "Music": the instruments of the 16 voices (MIDI channels) of a song can be replaced
/// and voices can be silenced. Every change is saved in the project (project.json) right away and
/// built into the music file of the hack ROM.
/// </summary>
public partial class MusicTab : UserControl
{
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private EditorSession? _session;
    private IReadOnlyList<SongInfo> _songs = [];
    private InstrumentBank? _bank;
    private SoundPlayer? _player;
    private int _loadGeneration;
    private bool _building;

    public MusicTab()
    {
        InitializeComponent();
        RestoreButton.IsEnabled = false;
    }

    private Window Owner => Window.GetWindow(this)!;

    private SongInfo? SelectedSong => (SongList.SelectedItem as SongItem)?.Song;

    /// <summary>One entry of the song list.</summary>
    /// <param name="Song">The song.</param>
    /// <param name="Text">What the list shows, e.g. "04 – Hang Glider (changed)".</param>
    public sealed record SongItem(SongInfo Song, string Text);

    /// <summary>An instrument as the list shows it, e.g. "Flute (60)".</summary>
    /// <param name="Program">Its number in the bank.</param>
    /// <param name="Text">Name and number.</param>
    public sealed record InstrumentItem(int Program, string Text)
    {
        public override string ToString() => Text;
    }

    /// <summary>Shows the songs of a project (reading the music takes a moment, so it runs in the background).</summary>
    public async void Load(EditorSession session)
    {
        _session = session;
        int generation = ++_loadGeneration;
        int selected = SongList.SelectedIndex;
        SongInfoText.Text = L.T("Reading the music…");
        ActionText.Text = string.Empty;
        VoiceGrid.Children.Clear();
        VoiceGrid.RowDefinitions.Clear();

        try
        {
            (_bank, _songs) = await Task.Run(() => (session.InstrumentBank, session.LoadSongs()));
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            if (generation == _loadGeneration)
            {
                SongInfoText.Text = L.F("The music could not be read: {0}", ex.Message);
            }

            return;
        }

        if (generation != _loadGeneration)
        {
            return;
        }

        FillSongList(selected < 0 ? 0 : selected);
    }

    private void FillSongList(int selectIndex)
    {
        _building = true;
        SongList.ItemsSource = _songs.Select(s => new SongItem(s, L.F("{0:D2} – {1}", s.Number, L.T(s.Name))
            + (s.Settings.IsEmpty ? string.Empty : " " + L.T("(changed)")))).ToList();
        SongList.SelectedIndex = Math.Clamp(selectIndex, 0, Math.Max(0, _songs.Count - 1));
        _building = false;
        ShowSong();
    }

    private void OnSongChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_building)
        {
            ShowSong();
        }
    }

    // ----------------------------------------------------------------- the voices of a song

    /// <summary>Builds one row per instrument of each voice; voices without notes are shown disabled.</summary>
    private void ShowSong()
    {
        VoiceGrid.Children.Clear();
        VoiceGrid.RowDefinitions.Clear();
        SongInfo? song = SelectedSong;
        RestoreButton.IsEnabled = song is not null && !song.Settings.IsEmpty;
        if (song is null || _session is null || _bank is null)
        {
            SongInfoText.Text = string.Empty;
            return;
        }

        SongInfoText.Text = L.F("{0} of 16 voices used", song.Channels.Count);
        AddHeader();
        for (int channel = 0; channel < 16; channel++)
        {
            SequenceChannel? info = song.Channels.FirstOrDefault(c => c.Channel == channel);
            if (info is null)
            {
                AddUnusedRow(channel);
                continue;
            }

            for (int slot = 0; slot < info.Programs.Count; slot++)
            {
                AddVoiceRow(song, info, slot);
            }
        }
    }

    private void AddHeader()
    {
        int row = NewRow();
        string[] titles = [L.T("Voice"), L.T("Category"), L.T("Instrument"), string.Empty, string.Empty, L.T("Notes")];
        for (int column = 0; column < titles.Length; column++)
        {
            Place(new TextBlock { Text = titles[column], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 4) }, row, column);
        }
    }

    private void AddUnusedRow(int channel)
    {
        int row = NewRow();
        Brush muted = (Brush)FindResource("MutedTextBrush");
        Place(new TextBlock { Text = L.F("Voice {0}", channel + 1), Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3) }, row, 0);
        Place(new ComboBox { IsEnabled = false, Margin = new Thickness(0, 3, 8, 3) }, row, 1);
        Place(new ComboBox { IsEnabled = false, Margin = new Thickness(0, 3, 8, 3) }, row, 2);
        Place(new TextBlock { Text = L.T("not used"), Foreground = muted, VerticalAlignment = VerticalAlignment.Center }, row, 5);
    }

    private void AddVoiceRow(SongInfo song, SequenceChannel info, int slot)
    {
        int row = NewRow();
        int original = info.Programs[slot];
        int originalInstrument = original >= 0 ? original : _session!.DefaultProgram(info.Channel);
        InstrumentChange? change = song.Settings.Instruments.FirstOrDefault(c => c.Channel == info.Channel && c.Original == original);
        int current = change?.Instrument ?? originalInstrument;
        bool muted = song.Settings.MutedChannels.Contains(info.Channel);

        string label = info.Programs.Count > 1
            ? L.F("Voice {0} (instrument {1})", info.Channel + 1, slot + 1)
            : L.F("Voice {0}", info.Channel + 1);
        var voiceText = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3) };
        if (change is not null || (muted && slot == 0))
        {
            voiceText.Foreground = (Brush)FindResource("ChangedBrush");
            voiceText.FontWeight = FontWeights.SemiBold;
        }

        voiceText.ToolTip = L.F("Originally: {0}", Describe(originalInstrument));
        Place(voiceText, row, 0);

        var categoryBox = new ComboBox { Margin = new Thickness(0, 3, 8, 3) };
        var instrumentBox = new ComboBox { Margin = new Thickness(0, 3, 8, 3) };
        List<InstrumentCategory> categories = MusicCatalog.Instruments.Select(i => i.Category).Distinct().ToList();
        categoryBox.ItemsSource = categories.Select(c => L.T(MusicCatalog.CategoryName(c))).ToList();

        InstrumentCategory? currentCategory = MusicCatalog.Find(current)?.Category;
        categoryBox.SelectedIndex = currentCategory is { } cat ? categories.IndexOf(cat) : -1;
        FillInstruments(instrumentBox, currentCategory, current);

        categoryBox.SelectionChanged += (_, _) =>
        {
            // A new category only offers its instruments; the voice changes when one is chosen.
            InstrumentCategory chosen = categories[categoryBox.SelectedIndex];
            FillInstruments(instrumentBox, chosen, chosen == MusicCatalog.Find(current)?.Category ? current : null);
        };
        instrumentBox.SelectionChanged += (_, _) =>
        {
            if (instrumentBox.SelectedItem is InstrumentItem item && item.Program != current)
            {
                SaveInstrument(song, info.Channel, original, item.Program);
            }
        };
        Place(categoryBox, row, 1);
        Place(instrumentBox, row, 2);

        var play = new Button
        {
            Content = "▶",
            Width = 30,
            Margin = new Thickness(0, 3, 8, 3),
            ToolTip = L.T("Plays a short sample of the selected instrument"),
        };
        play.Click += (_, _) => Play(instrumentBox.SelectedItem is InstrumentItem item ? item.Program : current, info);
        Place(play, row, 3);

        if (slot == 0)
        {
            var mute = new CheckBox
            {
                Content = L.T("Mute"),
                IsChecked = muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 14, 3),
                ToolTip = L.T("Silences the voice in this song (its volume is set to 0)."),
            };
            mute.Click += (_, _) => SaveMute(song, info.Channel, mute.IsChecked == true);
            Place(mute, row, 4);

            Place(new TextBlock
            {
                Text = L.F("{0} notes, {1} to {2}", info.NoteCount, NoteName(info.LowestKey), NoteName(info.HighestKey)),
                Foreground = (Brush)FindResource("MutedTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            }, row, 5);
        }
    }

    private void FillInstruments(ComboBox box, InstrumentCategory? category, int? select)
    {
        List<InstrumentItem> items = MusicCatalog.Instruments
            .Where(i => category is null || i.Category == category)
            .Select(i => new InstrumentItem(i.Program, Describe(i.Program)))
            .ToList();
        if (select is { } program && items.All(i => i.Program != program))
        {
            items.Insert(0, new InstrumentItem(program, Describe(program)));
        }

        box.ItemsSource = items;
        box.SelectedItem = items.FirstOrDefault(i => i.Program == select);
    }

    /// <summary>Name and number of an instrument, e.g. "Flute (60)".</summary>
    private static string Describe(int program) =>
        MusicCatalog.Find(program) is { } info ? $"{L.T(info.Name)} ({program})" : L.F("Instrument {0}", program);

    /// <summary>A key as note name, e.g. 60 → "C4".</summary>
    private static string NoteName(int key) => $"{NoteNames[key % 12]}{key / 12 - 1}";

    private int NewRow()
    {
        VoiceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return VoiceGrid.RowDefinitions.Count - 1;
    }

    private void Place(UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        VoiceGrid.Children.Add(element);
    }

    // ----------------------------------------------------------------- saving and playing

    private void SaveInstrument(SongInfo song, int channel, int original, int instrument)
    {
        SongSettings settings = Copy(song.Settings);
        settings.Instruments.RemoveAll(c => c.Channel == channel && c.Original == original);
        settings.Instruments.Add(new InstrumentChange(channel, original, instrument));
        Save(settings, L.F("Voice {0} of song {1:D2} now plays {2}.", channel + 1, song.Number, Describe(instrument)));
    }

    private void SaveMute(SongInfo song, int channel, bool mute)
    {
        SongSettings settings = Copy(song.Settings);
        settings.MutedChannels.Remove(channel);
        if (mute)
        {
            settings.MutedChannels.Add(channel);
            settings.MutedChannels.Sort();
        }

        Save(settings, mute
            ? L.F("Voice {0} of song {1:D2} is silent.", channel + 1, song.Number)
            : L.F("Voice {0} of song {1:D2} plays again.", channel + 1, song.Number));
    }

    private void Save(SongSettings settings, string message)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            _session.SaveSong(settings);
            ActionText.Text = message + " " + L.T("Build the hack ROM (Ctrl+S) to hear it in the game.");
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(Owner, L.F("The change could not be made.\n\n{0}", ex.Message));
        }

        // Rebuild the rows after the event of the changed control is finished.
        Dispatcher.BeginInvoke(Reload);
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (_session is null || SelectedSong is not { } song
            || !Ui.Confirm(Owner, L.F("Restore the original instruments and voices of song {0:D2}?", song.Number)))
        {
            return;
        }

        try
        {
            _session.RestoreSong(song.Number);
            ActionText.Text = L.F("Song {0:D2} is the original again. Build the hack ROM (Ctrl+S) to hear it in the game.", song.Number);
        }
        catch (Exception ex) when (Ui.IsExpectedError(ex))
        {
            Ui.ShowError(Owner, L.F("The change could not be made.\n\n{0}", ex.Message));
        }

        Reload();
    }

    /// <summary>Reads the songs again (after a change) and shows the same song.</summary>
    private void Reload()
    {
        if (_session is null)
        {
            return;
        }

        int selected = SongList.SelectedIndex;
        _songs = _session.LoadSongs();
        FillSongList(selected);
    }

    private void Play(int program, SequenceChannel channel)
    {
        if (_bank is null || !_bank.Instruments.ContainsKey(program))
        {
            return;
        }

        try
        {
            _player?.Stop();
            byte[] wav = InstrumentPreview.RenderWav(_bank, program, (channel.LowestKey + channel.HighestKey) / 2);
            _player = new SoundPlayer(new MemoryStream(wav));
            _player.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            ActionText.Text = L.F("The sample could not be played: {0}", ex.Message);
        }
    }

    private static SongSettings Copy(SongSettings settings) => new()
    {
        Song = settings.Song,
        Instruments = settings.Instruments.ToList(),
        MutedChannels = settings.MutedChannels.ToList(),
    };
}
