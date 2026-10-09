using System.Buffers.Binary;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Audio;

/// <summary>
/// Changes the instruments of a sequence and silences channels, keeping everything else.
/// </summary>
/// <remarks>
/// <para>Replacing an instrument changes the program number of the channel's program changes in
/// place. Silencing a channel sets the values of its volume changes (controller 7) to 0. Both
/// keep the size of the data.</para>
/// <para>A byte can be read more than once, because repeated byte runs ("FE hi lo n", see
/// <see cref="CompactSequence"/>) refer to earlier bytes, also of other channels. If a byte is
/// used by events that must get different values, the repetition is written out as plain bytes
/// first, so each use gets its own byte.</para>
/// <para>If a channel has no program change (or no volume change before its first note), a new
/// event is inserted in front of the channel's first event. Whenever bytes are inserted, the
/// track offsets and the distances of repetitions and loops across them are corrected.</para>
/// <para>At the end the result is read again and compared event by event with the expected
/// result, so a change can never damage a song unnoticed.</para>
/// </remarks>
public static class SequenceEditor
{
    private const int VolumeController = 7;
    private const int MaxRounds = 500;

    /// <summary>Applies the settings to a sequence.</summary>
    /// <param name="original">The original sequence.</param>
    /// <param name="settings">The changes.</param>
    /// <exception cref="InvalidDataException">The change cannot be made safely.</exception>
    public static byte[] Apply(byte[] original, SongSettings settings)
    {
        if (settings.IsEmpty)
        {
            return original;
        }

        foreach (InstrumentChange change in settings.Instruments)
        {
            if (change.Instrument is < 0 or > 127 || change.Channel is < 0 or > 15)
            {
                throw new InvalidDataException(CoreText.F("Instrument {0} does not exist.", change.Instrument));
            }
        }

        CompactSequence sequence = CompactSequence.Parse(original);
        List<SequenceEvent> expected = sequence.Events.Select(e => Transform(e, settings)).ToList();
        List<(int Channel, byte[] Bytes, SequenceEvent Event)> insertions = PlanInsertions(sequence, settings);

        byte[] data = PatchValues(original, settings);
        foreach (var group in insertions.GroupBy(i => i.Channel).OrderByDescending(g => FirstEventPosition(data, g.Key)))
        {
            data = Replace(data, FirstEventPosition(data, group.Key), 0, group.SelectMany(i => i.Bytes).ToArray());
        }

        expected.AddRange(insertions.Select(i => i.Event));
        Verify(data, expected);
        return data;
    }

    /// <summary>Changes program numbers and volumes in place, writing out shared repetitions where needed.</summary>
    private static byte[] PatchValues(byte[] data, SongSettings settings)
    {
        for (int round = 0; round < MaxRounds; round++)
        {
            CompactSequence sequence = CompactSequence.Parse(data);
            int? conflict = null;
            var patches = new Dictionary<int, byte>();

            foreach (IGrouping<int, SequenceByteUse> uses in sequence.Uses.GroupBy(u => u.Position))
            {
                // The value each use needs: the new one for a changed program or volume, else the current one.
                var wanted = uses.Select(u => (Use: u, Value: Wanted(sequence, u, settings) ?? data[u.Position])).ToList();
                if (wanted.All(w => w.Value == data[uses.Key]))
                {
                    continue;
                }

                if (wanted.Select(w => w.Value).Distinct().Count() == 1)
                {
                    patches[uses.Key] = wanted[0].Value;
                    continue;
                }

                // Different needs: give one of the repeated uses its own bytes.
                conflict = wanted.Where(w => w.Use.Backup >= 0).Select(w => (int?)w.Use.Backup).FirstOrDefault()
                    ?? throw new InvalidDataException(CoreText.T("The song cannot be changed there safely (shared data)."));
                break;
            }

            if (conflict is { } index)
            {
                data = ExpandBackup(data, sequence.Backups[index]);
                continue;
            }

            foreach ((int position, byte value) in patches)
            {
                data[position] = value;
            }

            return data;
        }

        throw new InvalidDataException(CoreText.T("The song cannot be changed there safely (shared data)."));
    }

    /// <summary>The new value of a byte if it is the program or volume value of an event that changes.</summary>
    private static byte? Wanted(CompactSequence sequence, SequenceByteUse use, SongSettings settings)
    {
        if (use.Event >= sequence.Events.Count)
        {
            return null;
        }

        SequenceEvent e = sequence.Events[use.Event];
        if (e.ValuePosition != use.Position)
        {
            return null;
        }

        SequenceEvent changed = Transform(e, settings);
        return changed == e ? null : (byte)(e.Kind == SequenceEventKind.Program ? changed.Data1 : changed.Data2);
    }

    /// <summary>Events to insert: program changes for channels without one, volume 0 for silenced channels without a volume change in front of their first note.</summary>
    private static List<(int, byte[], SequenceEvent)> PlanInsertions(CompactSequence sequence, SongSettings settings)
    {
        var insertions = new List<(int, byte[], SequenceEvent)>();
        IReadOnlyList<SequenceChannel> channels = sequence.GetChannels();
        foreach (InstrumentChange change in settings.Instruments.Where(c => c.Original < 0))
        {
            SequenceTrack track = TrackOf(sequence, channels, change.Channel);
            insertions.Add((change.Channel, [(byte)(0xC0 | change.Channel), (byte)change.Instrument, 0],
                new SequenceEvent(track.Index, track.FirstTick, SequenceEventKind.Program, change.Channel, change.Instrument, 0, 0, -1)));
        }

        foreach (int channel in settings.MutedChannels.Distinct())
        {
            bool silentFromStart = false;
            foreach (SequenceEvent e in sequence.Events.Where(e => e.Channel == channel))
            {
                if (e.Kind == SequenceEventKind.Note)
                {
                    break;
                }

                silentFromStart |= e.Kind == SequenceEventKind.Control && e.Data1 == VolumeController;
            }

            if (!silentFromStart)
            {
                SequenceTrack track = TrackOf(sequence, channels, channel);
                insertions.Add((channel, [(byte)(0xB0 | channel), VolumeController, 0, 0],
                    new SequenceEvent(track.Index, track.FirstTick, SequenceEventKind.Control, channel, VolumeController, 0, 0, -1)));
            }
        }

        return insertions;
    }

    private static int FirstEventPosition(byte[] data, int channel)
    {
        CompactSequence sequence = CompactSequence.Parse(data);
        return TrackOf(sequence, sequence.GetChannels(), channel).FirstEventPosition;
    }

    /// <summary>Writes a repetition out as plain bytes (0xFE doubled, as the game expects outside repetitions).</summary>
    private static byte[] ExpandBackup(byte[] data, SequenceBackup backup)
    {
        var bytes = new List<byte>();
        foreach (byte b in data.AsSpan(backup.Source, backup.Length))
        {
            bytes.Add(b);
            if (b == 0xFE)
            {
                bytes.Add(0xFE);
            }
        }

        return Replace(data, backup.Position, 4, bytes.ToArray());
    }

    /// <summary>
    /// Replaces <paramref name="removeCount"/> bytes at a position by other bytes (0 = insert) and
    /// corrects everything that points across it: track offsets, repetitions and loop ends.
    /// </summary>
    /// <exception cref="InvalidDataException">A repetition copies bytes of the replaced range.</exception>
    public static byte[] Replace(byte[] data, int position, int removeCount, byte[] bytes)
    {
        CompactSequence sequence = CompactSequence.Parse(data);
        int delta = bytes.Length - removeCount;
        byte[] result = [.. data.AsSpan(0, position), .. bytes, .. data.AsSpan(position + removeCount)];
        int end = position + removeCount;

        foreach (SequenceBackup backup in sequence.Backups.DistinctBy(b => b.Position))
        {
            if (backup.Position == position && removeCount > 0)
            {
                continue; // the repetition that is replaced
            }

            if (backup.Source < end && position < backup.Source + backup.Length)
            {
                throw new InvalidDataException(CoreText.T("The song cannot be changed there safely (shared data)."));
            }

            if (backup.Position >= end && backup.Source < position)
            {
                int at = backup.Position + delta;
                int distance = ((result[at + 1] << 8) | result[at + 2]) + delta;
                if (distance is < 0 or > 0xFDFF)
                {
                    throw new InvalidDataException(CoreText.T("The song cannot be changed there safely (shared data)."));
                }

                result[at + 1] = (byte)(distance >> 8);
                result[at + 2] = (byte)distance;
            }
        }

        foreach (SequenceLoopEnd loop in sequence.LoopEnds)
        {
            if (loop.Position >= end && (loop.Target < position || (removeCount > 0 && loop.Target == position)))
            {
                int at = loop.Position + delta + 2;
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at), (uint)(BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(at)) + delta));
            }
        }

        for (int track = 0; track < CompactSequence.ChannelCount; track++)
        {
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(track * 4));
            if (offset != 0 && offset > position)
            {
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(track * 4), (uint)(offset + delta));
            }
        }

        return result;
    }

    private static SequenceEvent Transform(SequenceEvent e, SongSettings settings)
    {
        if (e.Kind == SequenceEventKind.Program
            && settings.Instruments.FirstOrDefault(c => c.Channel == e.Channel && c.Original == e.Data1) is { } change)
        {
            return e with { Data1 = change.Instrument };
        }

        if (e.Kind == SequenceEventKind.Control && e.Data1 == VolumeController && settings.MutedChannels.Contains(e.Channel))
        {
            return e with { Data2 = 0 };
        }

        return e;
    }

    private static SequenceTrack TrackOf(CompactSequence sequence, IReadOnlyList<SequenceChannel> channels, int channel)
    {
        SequenceChannel info = channels.FirstOrDefault(c => c.Channel == channel)
            ?? throw new InvalidDataException(CoreText.F("Channel {0} plays no notes in this song.", channel + 1));
        return sequence.Tracks.First(t => t.Index == info.Track);
    }

    private static void Verify(byte[] data, List<SequenceEvent> expected)
    {
        CompactSequence result = CompactSequence.Parse(data);
        static (int, long, SequenceEventKind, int, int, int, int) Key(SequenceEvent e) =>
            (e.Track, e.Tick, e.Kind, e.Channel, e.Data1, e.Data2, e.Duration);

        if (!expected.Select(Key).Order().SequenceEqual(result.Events.Select(Key).Order()))
        {
            throw new InvalidDataException(CoreText.T("The song cannot be changed there safely (shared data)."));
        }
    }
}
