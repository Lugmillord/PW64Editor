namespace PW64Editor.Core.Rom;

/// <summary>
/// Thrown when a file cannot be loaded because it is not a valid N64 ROM.
/// </summary>
public sealed class InvalidRomException : Exception
{
    public InvalidRomException(string message)
        : base(message)
    {
    }
}
