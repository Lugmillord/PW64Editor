namespace PW64Editor.Core.Text;

/// <summary>
/// Where a text appears in the game, for grouping texts in the editor.
/// </summary>
/// <param name="Area">Top level group, e.g. "Hang Glider" or "Menus and screens".</param>
/// <param name="AreaOrder">Sort position of the area.</param>
/// <param name="Section">Second level group, e.g. "Class A" or "Options".</param>
/// <param name="SectionOrder">Sort position of the section within its area.</param>
/// <param name="Description">What the text is, e.g. "Mission 2 hint". Empty if unknown.</param>
/// <param name="SortKey">Sort position within the section.</param>
/// <param name="SourceFiles">Source files of the decompilation that use the text directly (may be empty).</param>
public sealed record TextCategory(
    string Area,
    int AreaOrder,
    string Section,
    int SectionOrder,
    string Description,
    string SortKey,
    IReadOnlyList<string> SourceFiles)
{
    /// <summary>
    /// The English pattern <see cref="Description"/> is made from, with placeholders {0}, {1}, …
    /// for <see cref="DescriptionArgs"/> (e.g. "Mission {0} {1}" with "2", "hint"). Empty if the
    /// description is a fixed text. The editor translates the pattern and the arguments.
    /// </summary>
    public string DescriptionPattern { get; init; } = string.Empty;

    /// <summary>The values for the placeholders of <see cref="DescriptionPattern"/>.</summary>
    public IReadOnlyList<string> DescriptionArgs { get; init; } = [];
}
