using System.Buffers.Binary;
using PW64Editor.Core.Audio;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Audio;

public class MusicTests
{
    private static readonly AudioLayout Audio = AudioLayout.PilotwingsUsa;

    private static byte[] Rom() => N64Rom.Load(TestRomLocator.RomPath!).Data;

    private static SequenceBank Sequences(byte[] rom) => SequenceBank.Parse(rom.AsSpan(Audio.SequenceOffset, Audio.SequenceSize));

    [RealRomFact]
    public void SequenceBank_ReadsAllSongsAndKeepsTheLayout()
    {
        byte[] rom = Rom();
        SequenceBank bank = Sequences(rom);
        Assert.Equal(31, bank.Sequences.Count);

        // Unchanged lengths keep the original file; other lengths give a new layout like the original.
        Assert.Equal(rom.AsSpan(Audio.SequenceOffset, Audio.SequenceSize).ToArray(), bank.Build());
        var longer = bank.Sequences.ToList();
        longer[3] = [.. longer[3], 0];
        byte[] built = bank.With(longer).Build();
        Assert.Equal(0, built.Length % 16);
        Assert.Equal(longer, SequenceBank.Parse(built).Sequences);
        Assert.Equal(rom.AsSpan(Audio.SequenceOffset + 4, 3 * 8 + 4).ToArray(), built.AsSpan(4, 3 * 8 + 4).ToArray());
    }

    [RealRomFact]
    public void AllSongs_CanBeRead()
    {
        SequenceBank bank = Sequences(Rom());
        IReadOnlyList<SequenceChannel> first = CompactSequence.Parse(bank.Sequences[0]).GetChannels();
        Assert.Equal([0, 1, 4, 6, 7, 8, 9, 10, 12], first.Select(c => c.Channel));
        Assert.Equal([3], first[0].Programs);

        // The ending switches channel 11 between the two drum sets; songs 11 and 20 play drums without a program change.
        Assert.Equal([4, 1], CompactSequence.Parse(bank.Sequences[30]).GetChannels().First(c => c.Channel == 10).Programs);
        Assert.Equal([-1], CompactSequence.Parse(bank.Sequences[11]).GetChannels().First(c => c.Channel == 9).Programs);
        Assert.Equal(31, bank.Sequences.Count(s => CompactSequence.Parse(s).Events.Count > 0));
    }

    [RealRomFact]
    public void EveryInstrumentOfEverySong_CanBeChangedAndEveryChannelSilenced()
    {
        SequenceBank bank = Sequences(Rom());
        for (int song = 0; song < bank.Sequences.Count; song++)
        {
            byte[] original = bank.Sequences[song];
            IReadOnlyList<SequenceChannel> channels = CompactSequence.Parse(original).GetChannels();
            var settings = new SongSettings
            {
                Song = song,
                Instruments = channels.SelectMany(c => c.Programs.Select(p => new InstrumentChange(c.Channel, p, 60))).ToList(),
                MutedChannels = channels.Select(c => c.Channel).ToList(),
            };

            byte[] changed = SequenceEditor.Apply(original, settings); // checks every event itself
            CompactSequence result = CompactSequence.Parse(changed);
            // Channels without notes are not offered and stay as they are.
            var used = channels.Select(c => c.Channel).ToHashSet();
            foreach (int channel in used)
            {
                int current = -1;
                foreach (SequenceEvent e in result.Events.Where(e => e.Channel == channel))
                {
                    if (e.Kind == SequenceEventKind.Program)
                    {
                        current = e.Data1;
                    }
                    else if (e.Kind == SequenceEventKind.Note)
                    {
                        Assert.Equal(60, current); // every note is played by the new instrument
                    }
                }
            }

            Assert.All(result.Events.Where(e => e.Kind == SequenceEventKind.Control && e.Data1 == 7 && used.Contains(e.Channel)),
                e => Assert.Equal(0, e.Data2));
            // Shared repetitions may have been written out, so the song can only grow.
            Assert.True(changed.Length >= original.Length + (channels.Any(c => c.Programs.Contains(-1)) ? 7 : 0));
        }
    }

    [RealRomFact]
    public void InsertedProgramChange_ComesFirstAndKeepsTheRest()
    {
        byte[] original = Sequences(Rom()).Sequences[11];
        var settings = new SongSettings { Song = 11, Instruments = [new InstrumentChange(9, -1, 4)] };
        CompactSequence before = CompactSequence.Parse(original);
        CompactSequence after = CompactSequence.Parse(SequenceEditor.Apply(original, settings));

        SequenceEvent first = after.Events.First(e => e.Channel == 9);
        Assert.Equal((SequenceEventKind.Program, 4), (first.Kind, first.Data1));
        Assert.Equal(before.Events.Count + 1, after.Events.Count);
    }

    [RealRomFact]
    public void Bank_HasTheFortyEightInstrumentsAndDecodesExactly()
    {
        byte[] rom = Rom();
        byte[] ctl = rom.AsSpan(Audio.BankOffset, Audio.TableOffset - Audio.BankOffset).ToArray();
        InstrumentBank bank = InstrumentBank.Parse(ctl, rom.AsSpan(Audio.TableOffset, Audio.EndOffset - Audio.TableOffset).ToArray());

        Assert.Equal(48, bank.Instruments.Count);
        Assert.Equal(22050, bank.SampleRate);
        Assert.Equal(1, bank.PercussionProgram);
        Assert.Equal(0, bank.FirstProgram);
        Assert.Equal(bank.Instruments.Keys.Order(), MusicCatalog.Instruments.Select(i => i.Program));

        // The loop of the first sound stores the decoder state at the start of the frame that
        // contains the loop start: our decoded samples must be exactly these values.
        int b = (int)BinaryPrimitives.ReadUInt32BigEndian(ctl.AsSpan(4));
        int instrument = (int)BinaryPrimitives.ReadUInt32BigEndian(ctl.AsSpan(b + 12));
        int sound = (int)BinaryPrimitives.ReadUInt32BigEndian(ctl.AsSpan(instrument + 16));
        int wave = (int)BinaryPrimitives.ReadUInt32BigEndian(ctl.AsSpan(sound + 8));
        int loop = (int)BinaryPrimitives.ReadUInt32BigEndian(ctl.AsSpan(wave + 12));
        int loopStart = (int)BinaryPrimitives.ReadUInt32BigEndian(ctl.AsSpan(loop));
        short[] state = Enumerable.Range(0, 16).Select(i => BinaryPrimitives.ReadInt16BigEndian(ctl.AsSpan(loop + 12 + i * 2))).ToArray();
        short[] samples = bank.Instruments[0].Sounds[0].Samples;
        Assert.Equal(state, samples.AsSpan(loopStart / 16 * 16, 16).ToArray());

        byte[] wav = InstrumentPreview.RenderWav(bank, 14, 70);
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.True(wav.Length > 44 + 22050);
    }

    [RealRomFact]
    public void Session_SavesSongsAndBuildsThemIntoTheRom()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Music Test", false, rom, TestRomLocator.RomPath!);

        // Song 0: trumpet (3) on channel 1 becomes the flute (60), channel 2 is silent.
        session.SaveSong(new SongSettings { Song = 0, Instruments = [new InstrumentChange(0, 3, 60)], MutedChannels = [1] });
        // Songs 11 and 20: the drums get another set and are silent: new events, the music file grows.
        session.SaveSong(new SongSettings { Song = 11, Instruments = [new InstrumentChange(9, -1, 4)], MutedChannels = [9] });
        session.SaveSong(new SongSettings { Song = 20, Instruments = [new InstrumentChange(9, -1, 4)], MutedChannels = [9] });
        Assert.Throws<PW64Editor.Core.Project.ProjectException>(() => session.SaveSong(new SongSettings { Song = 1, Instruments = [new InstrumentChange(1, 14, 2)] }));

        EditorSession reopened = EditorSession.Open(temp.Combine("hack"), rom, TestRomLocator.RomPath!);
        Assert.Equal(3, reopened.Project.Settings.Music.Count);

        (_, string romPath, _) = reopened.Build(createRestorePoint: false);
        byte[] built = File.ReadAllBytes(romPath);
        int sequenceOffset = (int)MipsAddressPatcher.ReadValue(built, Audio.SequenceReferences[0]);
        int bankOffset = (int)MipsAddressPatcher.ReadValue(built, Audio.BankReferences[0]);
        int tableOffset = (int)MipsAddressPatcher.ReadValue(built, Audio.TableReferences[1]);
        Assert.True(bankOffset > Audio.BankOffset);
        Assert.Equal(rom.Data.AsSpan(Audio.BankOffset, Audio.EndOffset - Audio.BankOffset).ToArray(),
            built.AsSpan(bankOffset, Audio.EndOffset - Audio.BankOffset).ToArray());
        Assert.Equal(bankOffset + (Audio.TableOffset - Audio.BankOffset), tableOffset);

        SequenceBank songs = SequenceBank.Parse(built.AsSpan(sequenceOffset, bankOffset - sequenceOffset));
        CompactSequence song0 = CompactSequence.Parse(songs.Sequences[0]);
        Assert.Equal(60, song0.Events.First(e => e.Kind == SequenceEventKind.Program && e.Channel == 0).Data1);
        Assert.All(song0.Events.Where(e => e.Kind == SequenceEventKind.Control && e.Channel == 1 && e.Data1 == 7), e => Assert.Equal(0, e.Data2));
        Assert.Equal(Sequences(rom.Data).Sequences[5], songs.Sequences[5]);

        Assert.True(reopened.RestoreSong(0));
        Assert.False(reopened.RestoreSong(0));
        Assert.Equal(2, reopened.Project.Settings.Music.Count);
    }

    [RealRomFact]
    public void Build_KeepsTheMusicInPlaceIfItStillFits()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Music Test", false, rom, TestRomLocator.RomPath!);
        session.SaveSong(new SongSettings { Song = 4, Instruments = [new InstrumentChange(3, 27, 10)] });

        (_, string romPath, _) = session.Build(createRestorePoint: false);
        byte[] built = File.ReadAllBytes(romPath);
        Assert.Equal((uint)Audio.BankOffset, MipsAddressPatcher.ReadValue(built, Audio.BankReferences[0]));

        // Only the changed program numbers differ from the original ROM.
        byte[] original = rom.Data;
        int differences = Enumerable.Range(Audio.SequenceOffset, Audio.SequenceSize).Count(i => built[i] != original[i]);
        Assert.InRange(differences, 1, 4);
    }
}

public class MidiTests
{
    private static readonly AudioLayout Audio = AudioLayout.PilotwingsUsa;

    private static SequenceBank Sequences() =>
        SequenceBank.Parse(N64Rom.Load(TestRomLocator.RomPath!).Data.AsSpan(Audio.SequenceOffset, Audio.SequenceSize));

    private static List<string> Content(CompactSequence sequence) => sequence.Events
        .Where(e => e.Kind is not (SequenceEventKind.LoopStart or SequenceEventKind.LoopEnd or SequenceEventKind.Tempo))
        .Select(e => $"{e.Tick},{e.Kind},{e.Channel},{e.Data1},{e.Data2},{e.Duration}").Order().ToList();

    [RealRomFact]
    public void ExportThenImport_OfEverySong_KeepsAllEventsAndTheLoop()
    {
        SequenceBank bank = Sequences();
        int maxSize = bank.Sequences.Max(s => s.Length);
        foreach (byte[] original in bank.Sequences)
        {
            CompactSequence song = CompactSequence.Parse(original);
            MidiImportResult result = StandardMidi.Import(StandardMidi.Export(song), _ => true, _ => 0, maxSize);
            CompactSequence imported = CompactSequence.Parse(result.Sequence);

            Assert.Equal(Content(song), Content(imported));
            Assert.Equal(StandardMidi.FindLoop(song), StandardMidi.FindLoop(imported));
            Assert.Equal(song.Events.Where(e => e.Kind == SequenceEventKind.Tempo).Select(e => e.Data1).Distinct(),
                imported.Events.Where(e => e.Kind == SequenceEventKind.Tempo).Select(e => e.Data1).Distinct());
        }
    }

    /// <summary>A small type 0 MIDI file: two notes on channel 1, program 25, a loop from beat 1 to 3.</summary>
    private static byte[] SmallMidi(bool loop, int program = 25)
    {
        var track = new List<byte>
        {
            0, 0xFF, 0x51, 3, 0x07, 0xA1, 0x20, // 120 bpm
            0, 0xC0, (byte)program,
            0, 0x90, 60, 100,
        };
        if (loop)
        {
            track.AddRange([0, 0xFF, 0x06, 9, .. "loopStart"u8.ToArray()]);
        }

        track.AddRange([0x83, 0x60, 0x80, 60, 0, // 480 ticks later: off
                        0, 0x90, 64, 90,
                        0x83, 0x60, 0x90, 64, 0]); // note-on with velocity 0 = off
        if (loop)
        {
            track.AddRange([0, 0xFF, 0x06, 7, .. "loopEnd"u8.ToArray()]);
        }

        track.AddRange([0, 0xFF, 0x2F, 0]);
        return [.. "MThd"u8.ToArray(), 0, 0, 0, 6, 0, 0, 0, 1, 1, 0xE0,
                .. "MTrk"u8.ToArray(), 0, 0, (byte)(track.Count >> 8), (byte)track.Count, .. track];
    }

    [Fact]
    public void Import_ReadsNotesLoopsAndReplacesUnknownInstruments()
    {
        MidiImportResult result = StandardMidi.Import(SmallMidi(loop: true), p => p != 25, _ => 3, 10000);
        CompactSequence song = CompactSequence.Parse(result.Sequence);

        Assert.Equal(480, song.Division);
        Assert.Equal([(0L, 60, 100, 480), (480L, 64, 90, 480)],
            song.Events.Where(e => e.Kind == SequenceEventKind.Note).Select(e => (e.Tick, e.Data1, e.Data2, e.Duration)));
        Assert.Equal(3, song.Events.Single(e => e.Kind == SequenceEventKind.Program).Data1);
        Assert.Equal((0L, 960L), StandardMidi.FindLoop(song));
        Assert.Contains(result.Warnings, w => w.Contains("instrument 25"));
    }

    [Fact]
    public void Import_WithoutLoop_WarnsThatTheSongStops()
    {
        MidiImportResult result = StandardMidi.Import(SmallMidi(loop: false, program: 3), _ => true, _ => 0, 10000);
        Assert.Null(StandardMidi.FindLoop(CompactSequence.Parse(result.Sequence)));
        Assert.Contains(result.Warnings, w => w.Contains("no loop"));
    }

    [Fact]
    public void Import_RejectsOtherFilesAndTooLargeSongs()
    {
        Assert.Throws<InvalidDataException>(() => StandardMidi.Import([1, 2, 3], _ => true, _ => 0, 10000));
        Assert.Throws<InvalidDataException>(() => StandardMidi.Import(SmallMidi(loop: true), _ => true, _ => 0, 20));
    }

    [RealRomFact]
    public void Session_ImportsExportsAndRestoresSongs()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Midi Test", false, rom, TestRomLocator.RomPath!);
        session.SaveSong(new SongSettings { Song = 7, MutedChannels = [3] });

        // Song 7 becomes the small MIDI; its old changes are gone.
        IReadOnlyList<string> warnings = session.ImportSongFromMidi(7, SmallMidi(loop: true, program: 60));
        Assert.Empty(warnings);
        SongInfo song = session.LoadSongs()[7];
        Assert.True(song.IsImported);
        Assert.True(song.Settings.IsEmpty);
        Assert.Equal([0], song.Channels.Select(c => c.Channel));

        // Changes apply to the imported song, and the export contains them.
        session.SaveSong(new SongSettings { Song = 7, Instruments = [new InstrumentChange(0, 60, 14)] });
        byte[] exported = session.ExportSongAsMidi(7);
        CompactSequence again = CompactSequence.Parse(StandardMidi.Import(exported, _ => true, _ => 0, 10000).Sequence);
        Assert.Equal(14, again.Events.Single(e => e.Kind == SequenceEventKind.Program).Data1);

        (_, string romPath, _) = session.Build(createRestorePoint: false);
        byte[] built = File.ReadAllBytes(romPath);
        int start = (int)MipsAddressPatcher.ReadValue(built, Audio.SequenceReferences[0]);
        int bank = (int)MipsAddressPatcher.ReadValue(built, Audio.BankReferences[0]);
        CompactSequence inRom = CompactSequence.Parse(SequenceBank.Parse(built.AsSpan(start, bank - start)).Sequences[7]);
        Assert.Equal(2, inRom.Events.Count(e => e.Kind == SequenceEventKind.Note));

        Assert.True(session.RestoreSong(7));
        Assert.False(session.LoadSongs()[7].IsChanged);
        Assert.Throws<PW64Editor.Core.Project.ProjectException>(() => session.ImportSongFromMidi(7, [0, 1, 2]));
    }
}
