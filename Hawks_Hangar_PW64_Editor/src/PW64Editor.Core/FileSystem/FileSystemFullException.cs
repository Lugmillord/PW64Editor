namespace PW64Editor.Core.FileSystem;

/// <summary>
/// Thrown when game files or the file table do not fit into the space available in the ROM.
/// </summary>
public sealed class FileSystemFullException : Exception
{
    public FileSystemFullException(string message)
        : base(message)
    {
    }
}
