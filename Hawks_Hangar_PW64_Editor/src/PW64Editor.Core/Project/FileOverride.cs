namespace PW64Editor.Core.Project;

/// <summary>
/// A game file in the project that replaces the original file from the ROM.
/// </summary>
/// <param name="TableIndex">Index of the replaced file in the game's file table.</param>
/// <param name="FileType">FourCC from the file name, e.g. "UPWT".</param>
/// <param name="Path">Full path of the file in the project's "files" folder.</param>
public sealed record FileOverride(int TableIndex, string FileType, string Path);
