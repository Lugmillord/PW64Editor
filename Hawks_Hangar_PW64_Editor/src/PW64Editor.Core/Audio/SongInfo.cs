namespace PW64Editor.Core.Audio;

/// <summary>A song of the game with its channels and the project's changes.</summary>
/// <param name="Number">Its number (0-30).</param>
/// <param name="Name">English name (see <see cref="MusicCatalog.SongNames"/>).</param>
/// <param name="Channels">The channels that play notes, with their original instruments.</param>
/// <param name="Settings">The project's changes (empty if none).</param>
public sealed record SongInfo(int Number, string Name, IReadOnlyList<SequenceChannel> Channels, SongSettings Settings)
{
    /// <summary>True if the project replaces the song with an imported MIDI file.</summary>
    public bool IsImported { get; init; }

    /// <summary>True if the project changes the song in any way.</summary>
    public bool IsChanged => IsImported || !Settings.IsEmpty;
}
