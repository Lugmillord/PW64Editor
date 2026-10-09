using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Audio;

/// <summary>The result of <see cref="StandardMidi.Import"/>.</summary>
/// <param name="Sequence">The song in the game's format.</param>
/// <param name="Warnings">What was changed or left out, in short sentences.</param>
public sealed record MidiImportResult(byte[] Sequence, IReadOnlyList<string> Warnings);

/// <summary>
/// Converts between the game's songs (<see cref="CompactSequence"/>) and standard MIDI files.
/// </summary>
/// <remarks>
/// <para>Export writes a MIDI file of type 1: a first track with the tempo and the loop, then one
/// track per channel. The program numbers are the game's instruments (not General MIDI). The
/// loop of a song is marked with the markers "loopStart" and "loopEnd".</para>
/// <para>Import reads MIDI files of type 0 and 1. Loops are taken from markers named
/// "loopStart"/"loopEnd" (also "loop start", "[" and "]") or from the controllers 102 (start)
/// and 103 (end). Each channel becomes one track of the game's format; notes get their length
/// from their note-off; repeated byte runs are packed like the game's own songs.</para>
/// </remarks>
public static class StandardMidi
{
    /// <summary>Most notes the game's music player sounds at the same time.</summary>
    public const int MaxVoices = 16;

    private const byte LoopStartController = 102;
    private const byte LoopEndController = 103;

    // ================================================================= export

    /// <summary>Writes a song as a MIDI file (type 1).</summary>
    /// <param name="sequence">The song.</param>
    /// <param name="trackName">Optional name of each channel's track (e.g. its instrument), by channel.</param>
    public static byte[] Export(CompactSequence sequence, Func<int, string?>? trackName = null)
    {
        var tracks = new List<byte[]>();

        // Track 1: tempo and loop.
        var conductor = new List<(long Tick, int Order, byte[] Bytes)>();
        foreach (SequenceEvent e in sequence.Events.Where(e => e.Kind == SequenceEventKind.Tempo).DistinctBy(e => (e.Tick, e.Data1)))
        {
            conductor.Add((e.Tick, 0, [0xFF, 0x51, 3, (byte)(e.Data1 >> 16), (byte)(e.Data1 >> 8), (byte)e.Data1]));
        }

        if (FindLoop(sequence) is { } loop)
        {
            conductor.Add((loop.Start, 1, Meta(0x06, "loopStart")));
            conductor.Add((loop.End, 1, Meta(0x06, "loopEnd")));
        }

        tracks.Add(WriteTrack(conductor, Meta(0x03, "Pilotwings 64")));

        // One track per channel.
        foreach (int channel in sequence.Events.Where(e => e.Channel >= 0).Select(e => e.Channel).Distinct().Order())
        {
            var events = new List<(long Tick, int Order, byte[] Bytes)>();
            int order = 0;
            foreach (SequenceEvent e in sequence.Events.Where(e => e.Channel == channel))
            {
                order++;
                byte status;
                switch (e.Kind)
                {
                    case SequenceEventKind.Note:
                        events.Add((e.Tick, 2 * order + 1, [(byte)(0x90 | channel), (byte)e.Data1, (byte)Math.Max(1, e.Data2)]));
                        events.Add((e.Tick + e.Duration, 0, [(byte)(0x80 | channel), (byte)e.Data1, 0]));
                        break;
                    case SequenceEventKind.Program:
                        events.Add((e.Tick, 2 * order + 1, [(byte)(0xC0 | channel), (byte)e.Data1]));
                        break;
                    case SequenceEventKind.Control:
                        events.Add((e.Tick, 2 * order + 1, [(byte)(0xB0 | channel), (byte)e.Data1, (byte)e.Data2]));
                        break;
                    case SequenceEventKind.Other:
                        status = (byte)(e.Status | channel);
                        events.Add((e.Tick, 2 * order + 1, e.Status == 0xD0 ? [status, (byte)e.Data1] : [status, (byte)e.Data1, (byte)e.Data2]));
                        break;
                }
            }

            string? name = trackName?.Invoke(channel);
            tracks.Add(WriteTrack(events, name is null ? null : Meta(0x03, name)));
        }

        var file = new List<byte>();
        file.AddRange("MThd"u8.ToArray());
        file.AddRange(BigEndian32(6));
        file.AddRange([0, 1, (byte)(tracks.Count >> 8), (byte)tracks.Count, (byte)(sequence.Division >> 8), (byte)sequence.Division]);
        foreach (byte[] track in tracks)
        {
            file.AddRange("MTrk"u8.ToArray());
            file.AddRange(BigEndian32(track.Length));
            file.AddRange(track);
        }

        return file.ToArray();
    }

    /// <summary>The loop of a song (the same in all its tracks), or null.</summary>
    public static (long Start, long End)? FindLoop(CompactSequence sequence)
    {
        SequenceEvent? start = sequence.Events.FirstOrDefault(e => e.Kind == SequenceEventKind.LoopStart);
        SequenceEvent? end = sequence.Events.FirstOrDefault(e => e.Kind == SequenceEventKind.LoopEnd && e.Track == start?.Track);
        return start is null || end is null ? null : (start.Tick, end.Tick);
    }

    private static byte[] WriteTrack(List<(long Tick, int Order, byte[] Bytes)> events, byte[]? name)
    {
        var data = new List<byte>();
        if (name is not null)
        {
            data.Add(0);
            data.AddRange(name);
        }

        long last = 0;
        foreach ((long tick, _, byte[] bytes) in events.OrderBy(e => e.Tick).ThenBy(e => e.Order))
        {
            WriteVariable(data, tick - last);
            data.AddRange(bytes);
            last = tick;
        }

        data.AddRange([0, 0xFF, 0x2F, 0]);
        return data.ToArray();
    }

    private static byte[] Meta(byte type, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        var meta = new List<byte> { 0xFF, type };
        WriteVariable(meta, bytes.Length);
        meta.AddRange(bytes);
        return meta.ToArray();
    }

    // ================================================================= import

    /// <summary>One channel event of the MIDI file, at an absolute time.</summary>
    private sealed record MidiEvent(long Tick, int Order, int Status, int Data1, int Data2, long Duration = 0);

    /// <summary>
    /// Converts a MIDI file into a song of the game.
    /// </summary>
    /// <param name="midi">The MIDI file.</param>
    /// <param name="isInstrument">True for the program numbers the game's bank has.</param>
    /// <param name="fallbackProgram">The instrument for unknown program numbers, by channel.</param>
    /// <param name="maxSize">Largest allowed size of the song in the game's format.</param>
    /// <exception cref="InvalidDataException">Not a usable MIDI file, or too large.</exception>
    public static MidiImportResult Import(byte[] midi, Func<int, bool> isInstrument, Func<int, int> fallbackProgram, int maxSize)
    {
        var warnings = new List<string>();
        (int division, List<MidiEvent> events, List<(long Tick, int Tempo)> tempos, long? loopStart, long? loopEnd) = Read(midi, warnings);

        // Notes: note-on with the length up to its note-off.
        var notes = new List<MidiEvent>();
        var others = new List<MidiEvent>();
        var open = new Dictionary<(int, int), Queue<MidiEvent>>();
        foreach (MidiEvent e in events)
        {
            int kind = e.Status & 0xF0;
            bool noteOn = kind == 0x90 && e.Data2 > 0;
            bool noteOff = kind == 0x80 || (kind == 0x90 && e.Data2 == 0);
            if (noteOn)
            {
                (int, int) key = (e.Status & 0x0F, e.Data1);
                if (!open.TryGetValue(key, out Queue<MidiEvent>? queue))
                {
                    open[key] = queue = new Queue<MidiEvent>();
                }

                queue.Enqueue(e);
            }
            else if (noteOff)
            {
                if (open.TryGetValue((e.Status & 0x0F, e.Data1), out Queue<MidiEvent>? queue) && queue.Count > 0)
                {
                    MidiEvent on = queue.Dequeue();
                    notes.Add(on with { Status = 0x90 | (on.Status & 0x0F), Duration = e.Tick - on.Tick });
                }
            }
            else if (kind == 0xB0 && e.Data1 is LoopStartController or LoopEndController)
            {
                // Loop controllers are read in Read.
            }
            else
            {
                others.Add(e);
            }
        }

        long songEnd = Math.Max(notes.Select(n => n.Tick + n.Duration).DefaultIfEmpty(0).Max(), events.Select(e => e.Tick).DefaultIfEmpty(0).Max());
        int unfinished = open.Values.Sum(q => q.Count);
        foreach (MidiEvent on in open.Values.SelectMany(q => q))
        {
            notes.Add(on with { Status = 0x90 | (on.Status & 0x0F), Duration = Math.Max(1, songEnd - on.Tick) });
        }

        if (unfinished > 0)
        {
            warnings.Add(CoreText.F("{0} note(s) had no end; they last until the end of the song.", unfinished));
        }

        if (notes.Count == 0)
        {
            throw new InvalidDataException(CoreText.T("The MIDI file contains no notes."));
        }

        // Loop.
        (long Start, long End)? loop = null;
        if (loopStart is not null || loopEnd is not null)
        {
            long start = loopStart ?? 0, end = loopEnd ?? songEnd;
            if (end > start)
            {
                loop = (start, end);
            }
            else
            {
                warnings.Add(CoreText.T("The loop end lies before the loop start; the song does not repeat."));
            }
        }

        if (loop is { } l)
        {
            int dropped = notes.Count(n => n.Tick >= l.End);
            notes.RemoveAll(n => n.Tick >= l.End);
            others.RemoveAll(e => e.Tick >= l.End);
            if (dropped > 0)
            {
                warnings.Add(CoreText.F("{0} note(s) behind the loop end are never played and were left out.", dropped));
            }
        }
        else
        {
            warnings.Add(CoreText.T("The MIDI file has no loop markers: the song plays once and then stops."));
        }

        // Instruments the bank does not have.
        foreach (IGrouping<(int Channel, int Program), MidiEvent> group in others
                     .Where(e => (e.Status & 0xF0) == 0xC0 && !isInstrument(e.Data1))
                     .GroupBy(e => (e.Status & 0x0F, e.Data1)).ToList())
        {
            int replacement = fallbackProgram(group.Key.Channel);
            warnings.Add(CoreText.F("Voice {0}: the game has no instrument {1}; instrument {2} plays instead. Choose another one in the list.",
                group.Key.Channel + 1, group.Key.Program, replacement));
            foreach (MidiEvent e in group)
            {
                others[others.IndexOf(e)] = e with { Data1 = replacement };
            }
        }

        int polyphony = MaxPolyphony(notes);
        if (polyphony > MaxVoices)
        {
            warnings.Add(CoreText.F("Up to {0} notes sound at the same time, but the game plays at most {1}: some notes will be cut short.",
                polyphony, MaxVoices));
        }

        byte[] sequence = Encode(division, notes, others, tempos, loop);
        if (sequence.Length > maxSize)
        {
            throw new InvalidDataException(CoreText.F("The song needs {0:N0} bytes in the game's format, but the game has room for at most {1:N0}. Shorten the song or use fewer notes.",
                sequence.Length, maxSize));
        }

        Verify(sequence, notes, others, loop);
        return new MidiImportResult(sequence, warnings);
    }

    /// <summary>Reads all tracks of a MIDI file into channel events at absolute times.</summary>
    private static (int Division, List<MidiEvent> Events, List<(long, int)> Tempos, long? LoopStart, long? LoopEnd) Read(byte[] midi, List<string> warnings)
    {
        if (midi.Length < 14 || !midi.AsSpan(0, 4).SequenceEqual("MThd"u8))
        {
            throw new InvalidDataException(CoreText.T("This is not a MIDI file."));
        }

        int headerLength = (int)BinaryPrimitives.ReadUInt32BigEndian(midi.AsSpan(4));
        int format = BinaryPrimitives.ReadUInt16BigEndian(midi.AsSpan(8));
        int trackCount = BinaryPrimitives.ReadUInt16BigEndian(midi.AsSpan(10));
        int division = BinaryPrimitives.ReadUInt16BigEndian(midi.AsSpan(12));
        if (format > 1)
        {
            throw new InvalidDataException(CoreText.T("MIDI files of type 2 are not supported. Save the file as type 0 or 1."));
        }

        if ((division & 0x8000) != 0 || division == 0)
        {
            throw new InvalidDataException(CoreText.T("MIDI files with SMPTE timing are not supported. Save the file with ticks per quarter note."));
        }

        var events = new List<MidiEvent>();
        var tempos = new List<(long, int)>();
        long? loopStart = null, loopEnd = null;
        int order = 0, position = 8 + headerLength, skipped = 0;

        for (int track = 0; track < trackCount && position + 8 <= midi.Length; track++)
        {
            bool isTrack = midi.AsSpan(position, 4).SequenceEqual("MTrk"u8);
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(midi.AsSpan(position + 4));
            int start = position + 8, end = Math.Min(midi.Length, start + length);
            position = start + length;
            if (!isTrack)
            {
                track--;
                continue;
            }

            int p = start, status = 0;
            long tick = 0;
            while (p < end)
            {
                tick += ReadVariable(midi, ref p, end);
                if (p >= end)
                {
                    break;
                }

                int b = midi[p];
                if (b == 0xFF)
                {
                    int type = midi[p + 1];
                    p += 2;
                    int metaLength = (int)ReadVariable(midi, ref p, end);
                    ReadOnlySpan<byte> data = midi.AsSpan(p, Math.Min(metaLength, end - p));
                    p += metaLength;
                    if (type == 0x2F)
                    {
                        break;
                    }

                    if (type == 0x51 && data.Length == 3)
                    {
                        tempos.Add((tick, (data[0] << 16) | (data[1] << 8) | data[2]));
                    }
                    else if (type is 0x06 or 0x01)
                    {
                        string text = Encoding.Latin1.GetString(data).Trim().Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
                        if (text is "loopstart" or "[")
                        {
                            loopStart ??= tick;
                        }
                        else if (text is "loopend" or "]")
                        {
                            loopEnd ??= tick;
                        }
                    }

                    status = 0;
                    continue;
                }

                if (b is 0xF0 or 0xF7)
                {
                    p++;
                    int sysexLength = (int)ReadVariable(midi, ref p, end);
                    p += sysexLength;
                    skipped++;
                    status = 0;
                    continue;
                }

                if ((b & 0x80) != 0)
                {
                    status = b;
                    p++;
                }
                else if (status == 0)
                {
                    throw new InvalidDataException(CoreText.T("The MIDI file is damaged."));
                }

                int kind = status & 0xF0;
                int data1 = midi[p++];
                int data2 = kind is 0xC0 or 0xD0 ? 0 : midi[p++];
                if (kind == 0xB0 && data1 == LoopStartController)
                {
                    loopStart ??= tick;
                }
                else if (kind == 0xB0 && data1 == LoopEndController)
                {
                    loopEnd ??= tick;
                }

                events.Add(new MidiEvent(tick, order++, status, data1 & 0x7F, data2 & 0x7F));
            }
        }

        if (skipped > 0)
        {
            warnings.Add(CoreText.F("{0} system exclusive message(s) were left out; the game cannot use them.", skipped));
        }

        events = events.OrderBy(e => e.Tick).ThenBy(e => e.Order).ToList();
        return (division, events, tempos.OrderBy(t => t.Item1).ToList(), loopStart, loopEnd);
    }

    /// <summary>Most notes that sound at the same time.</summary>
    private static int MaxPolyphony(List<MidiEvent> notes)
    {
        var changes = notes.SelectMany(n => new[] { (n.Tick, 1), (n.Tick + Math.Max(1, n.Duration), -1) })
            .OrderBy(c => c.Item1).ThenBy(c => c.Item2);
        int current = 0, max = 0;
        foreach ((_, int change) in changes)
        {
            current += change;
            max = Math.Max(max, current);
        }

        return max;
    }

    // ----------------------------------------------------------------- writing the game's format

    /// <summary>One item of a track: its time, sort order and bytes.</summary>
    private sealed record TrackItem(long Tick, int Order, byte[] Bytes, ItemKind Kind);

    private enum ItemKind
    {
        Event,
        LoopStart,
        LoopEnd,
    }

    /// <summary>Writes the song: one track per channel; the tempo goes into the first track.</summary>
    private static byte[] Encode(int division, List<MidiEvent> notes, List<MidiEvent> others, List<(long Tick, int Tempo)> tempos,
        (long Start, long End)? loop)
    {
        int[] channels = notes.Concat(others).Select(e => e.Status & 0x0F).Distinct().Order().ToArray();
        var literalTracks = new List<(int Channel, List<byte> Bytes, List<bool> Copyable, int LoopTarget, int LoopEnd)>();

        foreach (int channel in channels)
        {
            var items = new List<TrackItem>();
            foreach (MidiEvent e in others.Where(e => (e.Status & 0x0F) == channel))
            {
                items.Add(new(e.Tick, 1, (e.Status & 0xF0) is 0xC0 or 0xD0 ? [(byte)e.Status, (byte)e.Data1] : [(byte)e.Status, (byte)e.Data1, (byte)e.Data2], ItemKind.Event));
            }

            foreach (MidiEvent n in notes.Where(n => (n.Status & 0x0F) == channel))
            {
                var bytes = new List<byte> { (byte)n.Status, (byte)n.Data1, (byte)Math.Max(1, n.Data2) };
                WriteVariable(bytes, n.Duration);
                items.Add(new(n.Tick, 2, bytes.ToArray(), ItemKind.Event));
            }

            if (channel == channels[0])
            {
                foreach ((long tick, int tempo) in tempos)
                {
                    items.Add(new(tick, 0, [0xFF, 0x51, (byte)(tempo >> 16), (byte)(tempo >> 8), (byte)tempo], ItemKind.Event));
                }
            }

            if (loop is { } l)
            {
                items.Add(new(l.Start, -1, [0xFF, 0x2E, 0x00, 0xFF], ItemKind.LoopStart));
                items.Add(new(l.End, -2, [0xFF, 0x2D, 0xFF, 0xFF, 0, 0, 0, 0], ItemKind.LoopEnd));
            }

            // Literal track bytes: time, event, time, event, …, end of track.
            // Running status: a channel event with the same status as the one before leaves its
            // status byte out (the player forgets the status after meta events and loops).
            var data = new List<byte>();
            var copyable = new List<bool>();
            int loopTarget = -1, loopEndAt = -1, runningStatus = 0;
            long last = 0;
            foreach (TrackItem item in items.OrderBy(i => i.Tick).ThenBy(i => i.Order))
            {
                var time = new List<byte>();
                WriteVariable(time, item.Tick - last);
                last = item.Tick;
                Append(data, copyable, time, true);
                if (item.Kind == ItemKind.LoopEnd)
                {
                    loopEndAt = data.Count;
                }

                byte[] bytes = item.Bytes;
                if (bytes[0] == 0xFF)
                {
                    runningStatus = 0;
                }
                else if (bytes[0] == runningStatus)
                {
                    bytes = bytes[1..];
                }
                else
                {
                    runningStatus = bytes[0];
                }

                Append(data, copyable, bytes, item.Kind == ItemKind.Event);
                if (item.Kind == ItemKind.LoopStart)
                {
                    loopTarget = data.Count; // the loop jumps to the time value behind the loop start
                }
            }

            Append(data, copyable, [0, 0xFF, 0x2F], false);
            literalTracks.Add((channel, data, copyable, loopTarget, loopEndAt));
        }

        // Header, then the packed tracks.
        var output = new List<byte>(new byte[CompactSequence.HeaderSize]);
        BinaryPrimitives.WriteUInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(output)[0x40..], (uint)division);
        var packer = new Packer(output);
        foreach ((int channel, List<byte> bytes, List<bool> copyable, int loopTarget, int loopEnd) in literalTracks)
        {
            int start = output.Count;
            BinaryPrimitives.WriteUInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(output)[(channel * 4)..], (uint)start);
            (int targetOut, int loopEndOut) = packer.Write(bytes, copyable, loopTarget, loopEnd);
            if (loopEndOut >= 0)
            {
                // Distance from behind the loop end's 6 bytes back to the loop target.
                int distance = loopEndOut + 2 + 6 - targetOut;
                BinaryPrimitives.WriteUInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(output)[(loopEndOut + 4)..], (uint)distance);
            }
        }

        return output.ToArray();
    }

    private static void Append(List<byte> data, List<bool> copyable, IReadOnlyCollection<byte> bytes, bool canCopy)
    {
        data.AddRange(bytes);
        copyable.AddRange(Enumerable.Repeat(canCopy, bytes.Count));
    }

    /// <summary>
    /// Writes track bytes and replaces repeated runs by references to earlier plain bytes
    /// ("FE hi lo n"), like the SDK's midicomp. Only runs of at least 16 bytes are replaced: shorter
    /// ones would cut the earlier bytes into pieces that longer repetitions can no longer use
    /// (tested with the game's songs: 16 gives about the size of the originals). Loop events and the bytes at the loop
    /// target are never part of a repetition; 0xFE is written as FE FE.
    /// </summary>
    private sealed class Packer(List<byte> output)
    {
        private const int MinLength = 16;
        private const int KeyLength = 8;
        private const int MaxLength = 255;
        private const int MaxDistance = 0xFDFF;

        // Positions in the output where plain, copyable bytes of the minimum length start, by their first bytes.
        private readonly Dictionary<long, List<int>> _starts = [];

        // Output positions that hold plain copyable bytes (written literally, not 0xFE, not loop data).
        private readonly HashSet<int> _plain = [];

        public (int LoopTarget, int LoopEnd) Write(List<byte> bytes, List<bool> copyable, int loopTarget, int loopEnd)
        {
            int targetOut = -1, loopEndOut = -1;
            int i = 0;
            while (i < bytes.Count)
            {
                if (i == loopTarget)
                {
                    targetOut = output.Count;
                }

                if (i == loopEnd)
                {
                    loopEndOut = output.Count;
                }

                (int length, int source) = FindMatch(bytes, copyable, i, loopTarget, loopEnd);
                if (length >= MinLength && i + 1 != loopTarget && i + 1 != loopEnd
                    && FindMatch(bytes, copyable, i + 1, loopTarget, loopEnd).Length > length + 1)
                {
                    length = 0; // lazy matching: a byte later a clearly longer repetition starts
                }

                if (length >= MinLength)
                {
                    int distance = output.Count - source;
                    output.AddRange([0xFE, (byte)(distance >> 8), (byte)distance, (byte)length]);
                    i += length;
                    continue;
                }

                byte b = bytes[i];
                if (b == 0xFE)
                {
                    output.AddRange([0xFE, 0xFE]);
                }
                else
                {
                    if (copyable[i])
                    {
                        _plain.Add(output.Count);
                    }

                    output.Add(b);
                    Index(output.Count - MinLength);
                }

                i++;
            }

            if (loopTarget == bytes.Count)
            {
                targetOut = output.Count;
            }

            return (targetOut, loopEndOut);
        }

        private (int Length, int Source) FindMatch(List<byte> bytes, List<bool> copyable, int i, int loopTarget, int loopEnd)
        {
            if (i + MinLength > bytes.Count || !_starts.TryGetValue(Key(bytes, i), out List<int>? candidates))
            {
                return (0, 0);
            }

            int best = 0, bestSource = 0;
            for (int c = candidates.Count - 1; c >= 0; c--)
            {
                int source = candidates[c];
                if (output.Count - source > MaxDistance)
                {
                    continue;
                }

                int length = 0;
                while (length < MaxLength && i + length < bytes.Count && copyable[i + length] && bytes[i + length] != 0xFE
                       && source + length < output.Count && _plain.Contains(source + length)
                       && output[source + length] == bytes[i + length]
                       && (i + length != loopTarget || length == 0) && i + length != loopEnd)
                {
                    length++;
                }

                if (length > best)
                {
                    best = length;
                    bestSource = source;
                }
            }

            return (best, bestSource);
        }

        /// <summary>Remembers a position if the bytes there are all plain (a run of the minimum length can start there).</summary>
        private void Index(int position)
        {
            if (position < 0)
            {
                return;
            }

            for (int k = 0; k < MinLength; k++)
            {
                if (!_plain.Contains(position + k))
                {
                    return;
                }
            }

            long key = 0;
            for (int k = 0; k < KeyLength; k++)
            {
                key = (key << 8) | output[position + k];
            }

            if (!_starts.TryGetValue(key, out List<int>? list))
            {
                _starts[key] = list = [];
            }

            list.Add(position);
        }

        private static long Key(List<byte> bytes, int i)
        {
            long key = 0;
            for (int k = 0; k < KeyLength; k++)
            {
                key = (key << 8) | bytes[i + k];
            }

            return key;
        }
    }

    /// <summary>Reads the written song again and compares it with what should be in it.</summary>
    private static void Verify(byte[] data, List<MidiEvent> notes, List<MidiEvent> others, (long Start, long End)? loop)
    {
        CompactSequence result = CompactSequence.Parse(data);
        var gotNotes = result.Events.Where(e => e.Kind == SequenceEventKind.Note)
            .Select(e => (e.Tick, e.Channel, e.Data1, e.Data2, (long)e.Duration)).Order().ToList();
        var wantNotes = notes.Select(n => (n.Tick, n.Status & 0x0F, n.Data1, Math.Max(1, n.Data2), n.Duration)).Order().ToList();
        int gotOthers = result.Events.Count(e => e.Kind is SequenceEventKind.Program or SequenceEventKind.Control or SequenceEventKind.Other);
        bool loopOk = loop is null ? HasNoLoop(result) : FindLoop(result) == loop;
        if (!gotNotes.SequenceEqual(wantNotes) || gotOthers != others.Count || !loopOk)
        {
            throw new InvalidDataException(CoreText.T("The MIDI file could not be converted correctly."));
        }
    }

    private static bool HasNoLoop(CompactSequence sequence) =>
        !sequence.Events.Any(e => e.Kind is SequenceEventKind.LoopStart or SequenceEventKind.LoopEnd);

    // ----------------------------------------------------------------- helpers

    private static long ReadVariable(byte[] data, ref int position, int end)
    {
        long value = 0;
        for (int count = 0; count < 4 && position < end; count++)
        {
            int b = data[position++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        return value;
    }

    private static void WriteVariable(List<byte> data, long value)
    {
        var stack = new Stack<byte>();
        stack.Push((byte)(value & 0x7F));
        while ((value >>= 7) > 0)
        {
            stack.Push((byte)(0x80 | (value & 0x7F)));
        }

        data.AddRange(stack);
    }

    private static byte[] BigEndian32(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
