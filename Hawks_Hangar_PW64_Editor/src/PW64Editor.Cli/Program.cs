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
        "project-new" => ProjectCommands.New(commandArgs),
        "project-extract" => ProjectCommands.Extract(commandArgs),
        "project-revert" => ProjectCommands.Revert(commandArgs),
        "project-status" => ProjectCommands.Status(commandArgs),
        "project-build" => ProjectCommands.Build(commandArgs),
        "project-patch" => ProjectCommands.Patch(commandArgs),
        "project-backup" => ProjectCommands.Backup(commandArgs),
        "project-backups" => ProjectCommands.Backups(commandArgs),
        "project-restore" => ProjectCommands.Restore(commandArgs),
        "project-backup-delete" => ProjectCommands.DeleteBackup(commandArgs),
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
    Console.WriteLine("  fs-replace <rom> <index> <file> <output> [--relocate-audio] [--expand]");
    Console.WriteLine("                             Replace one game file and build a new ROM");
    Console.WriteLine("  rebuild <rom> <output> [--recompress] [--relocate-audio] [--expand] [--add-dummy <bytes>]");
    Console.WriteLine("                             Rebuild the ROM from its files (byte-identical without flags)");
    Console.WriteLine();
    Console.WriteLine("Build flags:");
    Console.WriteLine("  --recompress               Repack every file with our MIO0 compressor (stress test)");
    Console.WriteLine("  --relocate-audio           Always move the audio data directly behind the game files");
    Console.WriteLine("  --expand                   Allow enlarging the ROM to 16/32/64 MiB if data does not fit");
    Console.WriteLine("  --add-dummy <bytes>        (rebuild only) Append an unused file to test relocation/expansion");
    Console.WriteLine("  bps-create <clean rom> <modified rom> <patch>");
    Console.WriteLine("                             Create a BPS patch (verified after creation)");
    Console.WriteLine("  bps-apply <clean rom> <patch> <output>");
    Console.WriteLine("                             Apply a BPS patch, checking all checksums");
    Console.WriteLine("  bps-info <patch>           Show sizes and checksums stored in a BPS patch");
    Console.WriteLine();
    Console.WriteLine("Project commands:");
    Console.WriteLine("  project-new <folder> <clean rom> <name> [--git]");
    Console.WriteLine("                             Create a new hack project (--git adds a .gitignore)");
    Console.WriteLine("  project-extract <folder> <index> [--rom <path>]");
    Console.WriteLine("                             Copy a game file into the project for editing");
    Console.WriteLine("  project-revert <folder> <index>");
    Console.WriteLine("                             Remove a replacement file (use the original again)");
    Console.WriteLine("  project-status <folder> [--rom <path>]");
    Console.WriteLine("                             Show which files the project changes");
    Console.WriteLine("  project-build <folder> [--rom <path>] [--output <path>] [--backup]");
    Console.WriteLine("                             Build and overwrite the hack ROM (--backup: restore point)");
    Console.WriteLine("  project-patch <folder> <patch.bps> [--rom <path>]");
    Console.WriteLine("                             Create a BPS patch of the project");
    Console.WriteLine("  project-backup <folder>    Create a restore point (project files + hack ROM)");
    Console.WriteLine("  project-backups <folder>   List restore points");
    Console.WriteLine("  project-restore <folder> <name>");
    Console.WriteLine("                             Return to a restore point (current state is replaced)");
    Console.WriteLine("  project-backup-delete <folder> <name>");
    Console.WriteLine("                             Delete a restore point");
    Console.WriteLine();
    Console.WriteLine("  help                       Show this help text");
}
