namespace PW64Editor.Core.Text;

/// <summary>
/// A game text prepared for display: raw entry, decoded markup and category.
/// </summary>
/// <param name="Entry">The text as stored in the text file.</param>
/// <param name="Markup">The decoded text (see <see cref="TextCodec"/> for the markup).</param>
/// <param name="Category">Where the text appears in the game.</param>
public sealed record GameText(GameTextEntry Entry, string Markup, TextCategory Category)
{
    public int Index => Entry.Index;

    public string Name => Entry.Name;

    /// <summary>True for texts added by the user (number at or above the original text count).</summary>
    public bool IsCustom { get; init; }

    /// <summary>True for a free slot left by a removed custom text (see <see cref="GameTextEntry.IsFree"/>).</summary>
    public bool IsFree => Entry.IsFree;
}

/// <summary>
/// All texts of the game, decoded and categorized.
/// </summary>
public sealed class GameTextLibrary
{
    private GameTextLibrary(TextFont font, GameTextFile file, IReadOnlyList<GameText> texts, bool fromProject)
    {
        Font = font;
        File = file;
        Texts = texts;
        FromProject = fromProject;
        Codec = new TextCodec(font);
    }

    public TextFont Font { get; }

    public TextCodec Codec { get; }

    /// <summary>The parsed text file.</summary>
    public GameTextFile File { get; }

    /// <summary>All texts in game order.</summary>
    public IReadOnlyList<GameText> Texts { get; }

    /// <summary>True if the texts come from the project's own copy of the text file.</summary>
    public bool FromProject { get; }

    /// <summary>
    /// Decodes and categorizes all texts of a text file.
    /// </summary>
    /// <param name="font">The text font.</param>
    /// <param name="textFileData">The text file (FORM "ADAT").</param>
    /// <param name="fromProject">Whether the file comes from the project (for display only).</param>
    /// <param name="originalCount">Number of texts of the original game. Texts from this number on are
    /// custom texts and are grouped as such, whatever their name looks like.</param>
    public static GameTextLibrary Create(TextFont font, byte[] textFileData, bool fromProject,
        int originalCount = GameTextFile.RetailTextCount)
    {
        GameTextFile file = GameTextFile.Parse(textFileData);
        var codec = new TextCodec(font);
        List<GameText> texts = file.Entries
            .Select(e => e.Index >= originalCount
                ? new GameText(e, codec.Decode(e.Data), CustomTexts.Category(e.Name)) { IsCustom = true }
                : new GameText(e, codec.Decode(e.Data), TextCatalog.Categorize(e.Name)))
            .ToList();
        return new GameTextLibrary(font, file, texts, fromProject) { OriginalCount = originalCount };
    }

    /// <summary>Number of texts of the original game; texts from this number on are custom texts.</summary>
    public int OriginalCount { get; private init; } = GameTextFile.RetailTextCount;
}
