namespace PW64Editor.Core.Patching;

/// <summary>The reason a BPS patch could not be read or applied.</summary>
public enum BpsError
{
    /// <summary>The file is not a BPS patch or its structure is broken.</summary>
    InvalidFormat,

    /// <summary>The patch's own checksum does not match: the patch file is damaged.</summary>
    PatchCorrupted,

    /// <summary>The input file is not the one the patch was made for (size or CRC32 differs).</summary>
    WrongSource,

    /// <summary>Applying the patch produced data with the wrong checksum. Should never happen
    /// with an intact patch and the correct source.</summary>
    TargetMismatch,
}

/// <summary>
/// Thrown when a BPS patch cannot be read or applied.
/// </summary>
public sealed class BpsException : Exception
{
    public BpsException(BpsError error, string message)
        : base(message)
    {
        Error = error;
    }

    /// <summary>What went wrong, so callers can react (e.g. show a specific hint).</summary>
    public BpsError Error { get; }
}
