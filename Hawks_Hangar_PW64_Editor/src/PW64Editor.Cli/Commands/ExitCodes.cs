namespace PW64Editor.Cli.Commands;

/// <summary>
/// Process exit codes, so scripts can react to the result of a command.
/// 0 means success; everything else is an error.
/// </summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 1;
    public const int InvalidRom = 2;
    public const int VerificationFailed = 3;
    public const int UnexpectedError = 99;
}
