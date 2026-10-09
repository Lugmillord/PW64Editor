namespace PW64Editor.Core.Audio;

/// <summary>The changes of one song (stored in project.json).</summary>
public sealed class SongSettings
{
    /// <summary>The song's number (0-30).</summary>
    public int Song { get; set; }

    /// <summary>Replaced instruments.</summary>
    public List<InstrumentChange> Instruments { get; set; } = [];

    /// <summary>Channels (0-15) that are silent.</summary>
    public List<int> MutedChannels { get; set; } = [];

    /// <summary>True if the settings change nothing.</summary>
    public bool IsEmpty => Instruments.Count == 0 && MutedChannels.Count == 0;
}

/// <summary>One replaced instrument of a song.</summary>
/// <param name="Channel">The MIDI channel (0-15).</param>
/// <param name="Original">The instrument the song selects there (program number), or -1 for the
/// instrument the channel has before any program change.</param>
/// <param name="Instrument">The instrument that plays instead.</param>
public sealed record InstrumentChange(int Channel, int Original, int Instrument);
