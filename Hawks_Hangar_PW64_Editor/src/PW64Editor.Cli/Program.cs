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
        "fs-list" => FileSystemCommands.List(commandArgs),
        "fs-extract" => FileSystemCommands.Extract(commandArgs),
        "fs-chunks" => FileSystemCommands.Chunks(commandArgs),
        "fs-replace" => BuildCommands.Replace(commandArgs),
        "rebuild" => BuildCommands.Rebuild(commandArgs),
        "bps-create" => PatchCommands.Create(commandArgs),
        "bps-apply" => PatchCommands.Apply(commandArgs),
        "bps-info" => PatchCommands.Info(commandArgs),
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
    Console.WriteLine("  fs-list <rom>              List all game files from the file table");
    Console.WriteLine("  fs-extract <rom> <folder>  Export all game files into a folder");
    Console.WriteLine("  fs-chunks <rom> <index>    Show the chunks inside one game file");
    Console.WriteLine("  fs-replace <rom> <index> <file> <output>");
    Console.WriteLine("                             Replace one game file and build a new ROM");
    Console.WriteLine("  rebuild <rom> <output> [--recompress]");
    Console.WriteLine("                             Rebuild the ROM from its files (byte-identical without");
    Console.WriteLine("                             the flag; --recompress repacks every file as a stress test)");
    Console.WriteLine("  bps-create <clean rom> <modified rom> <patch>");
    Console.WriteLine("                             Create a BPS patch (verified after creation)");
    Console.WriteLine("  bps-apply <clean rom> <patch> <output>");
    Console.WriteLine("                             Apply a BPS patch, checking all checksums");
    Console.WriteLine("  bps-info <patch>           Show sizes and checksums stored in a BPS patch");
    Console.WriteLine("  help                       Show this help text");
}
