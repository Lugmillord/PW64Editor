namespace PW64Editor.Core.Textures;

/// <summary>One texture of the game in the project.</summary>
/// <param name="Number">The game's texture number (the position among the UVTX files).</param>
/// <param name="TableIndex">Index of the texture file in the game's file table.</param>
/// <param name="Original">The texture of the original game.</param>
/// <param name="Current">The texture as it is in the project (the original if the project does not replace it).</param>
/// <param name="IsChanged">True if the project replaces the texture with a different one.</param>
/// <param name="Problem">Why the texture cannot be edited, or null.</param>
public sealed record TextureEntry(int Number, int TableIndex, GameTexture Original, GameTexture Current, bool IsChanged, string? Problem)
{
    /// <summary>True if the texture can be shown and replaced.</summary>
    public bool CanEdit => Problem is null;
}

/// <summary>What saving a texture did.</summary>
public enum TextureSaveResult
{
    /// <summary>The project now has the new texture.</summary>
    Changed,

    /// <summary>The image is the same as the current one; nothing was written.</summary>
    Unchanged,

    /// <summary>The image is the original one; the project's replacement was removed.</summary>
    RestoredOriginal,
}
