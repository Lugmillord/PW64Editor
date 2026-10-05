using PW64Editor.Cli.Commands;

// Entry point of the Hawk's Hangar command line tool.
// Usage: pw64cli <command> [arguments]
//
// This file uses "top-level statements": the code below is the body of Main().
// The variable "args" is provided automatically and contains the command line arguments.

if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
{
    PrintUsage();
    return ExitCodes.Success;
}

string command = args[0].ToLowerInvariant();
string[] commandArgs = args[1..]; // everything after the command name

try
{
    return command switch
    {
        "info" => InfoCommand.Run(commandArgs),
        "fixcrc" => FixCrcCommand.Run(commandArgs),
        "mio0-scan" => Mio0Commands.Scan(commandArgs),
        "mio0-extract" => Mio0Commands.Extract(commandArgs),
        _ => UnknownCommand(command),
    };
}
catch (Exception ex)
{
    // Last line of defense: never show a raw stack trace to the user.
    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    return ExitCodes.UnexpectedError;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return ExitCodes.InvalidArguments;
}

static void PrintUsage()
{
    Console.WriteLine("Hawk's Hangar - Pilotwings 64 ROM hacking tool");
    Console.WriteLine();
    Console.WriteLine("Usage: pw64cli <command> [arguments]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  info <rom>                 Show header information and verify the ROM");
    Console.WriteLine("  fixcrc <input> <output>    Recalculate the boot checksum and save to a new file");
    Console.WriteLine("  mio0-scan <rom>            List all MIO0-compressed blocks in the ROM");
    Console.WriteLine("  mio0-extract <rom> <offset> <output>");
    Console.WriteLine("                             Decompress the MIO0 block at <offset> to a file");
    Console.WriteLine("  help                       Show this help text");
}
