using System.Buffers.Binary;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Audio;

/// <summary>Kinds of events in a sequence.</summary>
public enum SequenceEventKind
{
    /// <summary>A note (note on with its length; compact MIDI has no note-off events).</summary>
    Note,

    /// <summary>A program change: the channel's instrument.</summary>
    Program,

    /// <summary>A control change, e.g. controller 7 (volume).</summary>
    Control,

    /// <summary>Pitch bend or channel pressure.</summary>
    Other,

    /// <summary>A tempo change.</summary>
    Tempo,

    /// <summary>The start of a loop.</summary>
    LoopStart,

    /// <summary>The end of a loop (jumps back to the loop start).</summary>
    LoopEnd,
}

/// <summary>One event of a sequence.</summary>
/// <param name="Track">The track it is in (0-15).</param>
/// <param name="Tick">Time from the start of the track, in ticks (loops are not followed).</param>
/// <param name="Kind">What it is.</param>
/// <param name="Channel">MIDI channel 0-15 (-1 for tempo and loop events).</param>
/// <param name="Data1">Key, program or controller number.</param>
/// <param name="Data2">Velocity or controller value.</param>
/// <param name="Duration">Length of a note in ticks.</param>
/// <param name="ValuePosition">Where in the data the program number (program change) or the
/// controller value (control change) is stored; -1 for other events.</param>
public sealed record SequenceEvent(int Track, long Tick, SequenceEventKind Kind, int Channel, int Data1, int Data2, int Duration, int ValuePosition);

/// <summary>A repetition of earlier bytes: "FE hi lo length" in the data, see <see cref="CompactSequence"/>.</summary>
/// <param name="Position">Position of the 0xFE that starts it.</param>
/// <param name="Source">Position of the repeated bytes.</param>
/// <param name="Length">Number of repeated bytes.</param>
public sealed record SequenceBackup(int Position, int Source, int Length);

/// <summary>One byte the player reads.</summary>
/// <param name="Position">Where it is stored.</param>
/// <param name="Event">Index of the event it belongs to in <see cref="CompactSequence.Events"/> (time values count to the next event).</param>
/// <param name="Backup">Index of the repetition it was read through in <see cref="CompactSequence.Backups"/>, or -1.</param>
public readonly record struct SequenceByteUse(int Position, int Event, int Backup);

/// <summary>The end of a loop: a meta event with 6 bytes (count, current count, 32-bit offset back).</summary>
/// <param name="Position">Position of its 6 bytes.</param>
/// <param name="Target">Where it jumps back to.</param>
public sealed record SequenceLoopEnd(int Position, int Target);

/// <summary>One track of a sequence.</summary>
/// <param name="Index">Its number (0-15).</param>
/// <param name="Start">Where its data starts.</param>
/// <param name="End">Where its data ends (behind its end-of-track event).</param>
/// <param name="FirstEventPosition">Where its first event starts (behind the first time value).</param>
/// <param name="FirstTick">Time of its first event.</param>
public sealed record SequenceTrack(int Index, int Start, int End, int FirstEventPosition, long FirstTick);

/// <summary>How a sequence uses one MIDI channel.</summary>
/// <param name="Channel">The channel (0-15).</param>
/// <param name="Track">The track that plays its first note.</param>
/// <param name="NoteCount">Number of notes.</param>
/// <param name="LowestKey">Lowest key played (MIDI number).</param>
/// <param name="HighestKey">Highest key played.</param>
/// <param name="Programs">The instruments that play the channel's notes, in the order they first
/// play. -1 stands for the instrument the channel has before any program change (see
/// <see cref="CompactSequence.DefaultProgram"/>), used if notes come before the first program change.</param>
public sealed record SequenceChannel(int Channel, int Track, int NoteCount, int LowestKey, int HighestKey, IReadOnlyList<int> Programs);

/// <summary>
/// A sequence in the "compact MIDI" format of the N64's audio library (ALCSeq, made by the SDK
/// tool midicomp from a type 0 MIDI file). Pilotwings 64 plays its music with it.
/// </summary>
/// <remarks>
/// <para>Layout: 16 track offsets (u32, 0 = unused) and the division (ticks per quarter note,
/// u32), then the tracks. A track is MIDI data: a time value (variable length), an event, a time
/// value, an event, … up to the end-of-track event (FF 2F). Differences to standard MIDI (see
/// alCSeqNextEvent and __getTrackByte in the decompilation):</para>
/// <list type="bullet">
///   <item>Notes have no note-off: the note-on is followed by the note's length (variable length).</item>
///   <item>Repeated byte runs are replaced by "FE hi lo n": repeat n bytes that start
///         (hi·256 + lo) bytes before this code. A literal 0xFE is written as FE FE.
///         The repeated bytes may lie in another track.</item>
///   <item>Loops: FF 2E with 2 bytes (start) and FF 2D with 6 bytes (end: count, current count,
///         32-bit distance back to the loop start).</item>
/// </list>
/// </remarks>
public sealed class CompactSequence
{
    /// <summary>Size of the header: 16 track offsets and the division.</summary>
    public const int HeaderSize = 0x44;

    /// <summary>Most tracks and channels.</summary>
    public const int ChannelCount = 16;

    /// <summary>The channel that starts with the bank's percussion instrument (MIDI channel 10).</summary>
    public const int PercussionChannel = 9;

    private CompactSequence(byte[] data, int division, IReadOnlyList<SequenceTrack> tracks, IReadOnlyList<SequenceEvent> events,
        IReadOnlyList<SequenceBackup> backups, IReadOnlyList<SequenceLoopEnd> loopEnds, IReadOnlyList<SequenceByteUse> uses)
    {
        Uses = uses;
        Data = data;
        Division = division;
        Tracks = tracks;
        Events = events;
        Backups = backups;
        LoopEnds = loopEnds;
    }

    /// <summary>The sequence's bytes.</summary>
    public byte[] Data { get; }

    /// <summary>Ticks per quarter note.</summary>
    public int Division { get; }

    public IReadOnlyList<SequenceTrack> Tracks { get; }

    /// <summary>All events, track by track, each track in time order.</summary>
    public IReadOnlyList<SequenceEvent> Events { get; }

    public IReadOnlyList<SequenceBackup> Backups { get; }

    public IReadOnlyList<SequenceLoopEnd> LoopEnds { get; }

    /// <summary>Every byte the player reads, in reading order (a byte of a repetition is read more than once).</summary>
    public IReadOnlyList<SequenceByteUse> Uses { get; }

    /// <summary>
    /// The instrument a channel has before any program change: the bank's percussion
    /// instrument on channel 10, otherwise the bank's first instrument (see __initFromBank).
    /// </summary>
    public static int DefaultProgram(int channel, int percussionProgram, int firstProgram) =>
        channel == PercussionChannel && percussionProgram >= 0 ? percussionProgram : firstProgram;

    /// <summary>Reads a sequence.</summary>
    /// <exception cref="InvalidDataException">The data is damaged.</exception>
    public static CompactSequence Parse(byte[] data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException(CoreText.T("The sequence is too short."));
        }

        int division = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x40));
        var tracks = new List<SequenceTrack>();
        var events = new List<SequenceEvent>();
        var backups = new List<SequenceBackup>();
        var loopEnds = new List<SequenceLoopEnd>();
        var uses = new List<SequenceByteUse>();

        for (int track = 0; track < ChannelCount; track++)
        {
            int start = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(track * 4));
            if (start == 0)
            {
                continue;
            }

            var reader = new Reader(data, start, backups, uses, events);
            long tick = reader.ReadVariable();
            int firstEvent = reader.Position;
            long firstTick = tick;
            int status = 0;
            while (true)
            {
                (int value, _) = reader.Next();
                if (value == 0xFF)
                {
                    int type = reader.Next().Value;
                    if (type == 0x2F)
                    {
                        break;
                    }

                    switch (type)
                    {
                        case 0x51:
                            int tempo = (reader.Next().Value << 16) | (reader.Next().Value << 8) | reader.Next().Value;
                            events.Add(new(track, tick, SequenceEventKind.Tempo, -1, tempo, 0, 0, -1));
                            break;
                        case 0x2E:
                            reader.Next();
                            reader.Next();
                            events.Add(new(track, tick, SequenceEventKind.LoopStart, -1, 0, 0, 0, -1));
                            break;
                        case 0x2D:
                            int position = reader.ReadRaw(6);
                            int target = position + 6 - (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 2));
                            if (target < HeaderSize || target > position)
                            {
                                throw Damaged();
                            }

                            loopEnds.Add(new(position, target));
                            events.Add(new(track, tick, SequenceEventKind.LoopEnd, -1, data[position], 0, 0, -1));
                            break;
                        default:
                            throw Damaged();
                    }

                    status = 0; // no running status after meta events
                }
                else
                {
                    int data1;
                    int data1Position;
                    if ((value & 0x80) != 0)
                    {
                        status = value;
                        (data1, data1Position) = reader.Next();
                    }
                    else
                    {
                        if (status == 0)
                        {
                            throw Damaged();
                        }

                        (data1, data1Position) = (value, reader.LastPosition);
                    }

                    int kind = status & 0xF0, channel = status & 0x0F;
                    switch (kind)
                    {
                        case 0xC0:
                            events.Add(new(track, tick, SequenceEventKind.Program, channel, data1, 0, 0, data1Position));
                            break;
                        case 0xD0:
                            events.Add(new(track, tick, SequenceEventKind.Other, channel, data1, 0, 0, -1));
                            break;
                        default:
                            (int data2, int data2Position) = reader.Next();
                            if (kind == 0x90)
                            {
                                int duration = (int)reader.ReadVariable();
                                events.Add(new(track, tick, SequenceEventKind.Note, channel, data1, data2, duration, -1));
                            }
                            else if (kind == 0xB0)
                            {
                                events.Add(new(track, tick, SequenceEventKind.Control, channel, data1, data2, 0, data2Position));
                            }
                            else
                            {
                                events.Add(new(track, tick, SequenceEventKind.Other, channel, data1, data2, 0, -1));
                            }

                            break;
                    }
                }

                tick += reader.ReadVariable();
            }

            tracks.Add(new SequenceTrack(track, start, reader.Position, firstEvent, firstTick));
        }

        return new CompactSequence(data, division, tracks, events, backups, loopEnds, uses);
    }

    /// <summary>How the sequence uses each channel that plays notes.</summary>
    public IReadOnlyList<SequenceChannel> GetChannels()
    {
        var result = new List<SequenceChannel>();
        foreach (IGrouping<int, SequenceEvent> group in Events
                     .Where(e => e.Channel >= 0)
                     .OrderBy(e => e.Tick).ThenBy(e => e.Track)
                     .GroupBy(e => e.Channel)
                     .OrderBy(g => g.Key))
        {
            var programs = new List<int>();
            int current = -1;
            int notes = 0, lowest = 127, highest = 0, track = -1;
            foreach (SequenceEvent e in group)
            {
                if (e.Kind == SequenceEventKind.Program)
                {
                    current = e.Data1;
                }
                else if (e.Kind == SequenceEventKind.Note)
                {
                    // Only instruments that play notes count (some songs select one and replace it right away).
                    if (!programs.Contains(current))
                    {
                        programs.Add(current);
                    }

                    if (track < 0)
                    {
                        track = e.Track;
                    }

                    notes++;
                    lowest = Math.Min(lowest, e.Data1);
                    highest = Math.Max(highest, e.Data1);
                }
            }

            if (notes > 0)
            {
                result.Add(new SequenceChannel(group.Key, track, notes, lowest, highest, programs));
            }
        }

        return result;
    }

    private static InvalidDataException Damaged() => new(CoreText.T("The sequence data is damaged."));

    /// <summary>Reads track bytes like the game's __getTrackByte, including repeated byte runs.</summary>
    private sealed class Reader(byte[] data, int position, List<SequenceBackup> backups, List<SequenceByteUse> uses, List<SequenceEvent> events)
    {
        private int _backupPosition;
        private int _backupLeft;

        /// <summary>Position of the next byte that is not part of a repetition.</summary>
        public int Position { get; private set; } = position;

        /// <summary>Where the byte returned last is stored.</summary>
        public int LastPosition { get; private set; }

        public (int Value, int Position) Next()
        {
            if (_backupLeft > 0)
            {
                return FromBackup();
            }

            int at = Position;
            int value = Get(Position++);
            if (value == 0xFE)
            {
                int next = Get(Position++);
                if (next != 0xFE)
                {
                    int low = Get(Position++);
                    int length = Get(Position++);
                    int source = Position - (((next << 8) | low) + 4);
                    if (source < HeaderSize || length == 0 || source + length > at)
                    {
                        throw Damaged();
                    }

                    backups.Add(new SequenceBackup(at, source, length));
                    _backupPosition = source;
                    _backupLeft = length;
                    return FromBackup();
                }

                at = Position - 1;
            }

            LastPosition = at;
            uses.Add(new SequenceByteUse(at, events.Count, -1));
            return (value, at);
        }

        /// <summary>Reads a variable-length number (7 bits per byte, high bit = more follows).</summary>
        public long ReadVariable()
        {
            long value = Next().Value;
            if ((value & 0x80) != 0)
            {
                value &= 0x7F;
                int count = 0;
                int next;
                do
                {
                    next = Next().Value;
                    value = (value << 7) + (next & 0x7F);
                    if (++count > 4)
                    {
                        throw Damaged();
                    }
                }
                while ((next & 0x80) != 0);
            }

            return value;
        }

        /// <summary>Skips bytes that the game reads directly (the end of a loop); returns their position.</summary>
        public int ReadRaw(int count)
        {
            if (_backupLeft > 0 || Position + count > data.Length)
            {
                throw Damaged();
            }

            int at = Position;
            Position += count;
            return at;
        }

        private (int, int) FromBackup()
        {
            int at = _backupPosition++;
            _backupLeft--;
            LastPosition = at;
            uses.Add(new SequenceByteUse(at, events.Count, backups.Count - 1));
            return (Get(at), at);
        }

        private int Get(int at) => at < data.Length ? data[at] : throw Damaged();
    }
}
