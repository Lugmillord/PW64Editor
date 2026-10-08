using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Verification;

namespace PW64Editor.Cli.Commands;

/// <summary>
/// Commands for hack projects: create, extract files for editing, revert, check status, build,
/// create patches and manage restore points.
/// </summary>
/// <remarks>
/// Commands that need the clean ROM take it from "--rom &lt;path&gt;" if given,
/// otherwise from the path stored in the project's local settings (project.user.json).
/// </remarks>
internal static class ProjectCommands
{
    public static int New(string[] args)
    {
        bool prepareForGit = CommandHelpers.ExtractFlag(ref args, "--git");

        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: pw64cli project-new <folder> <clean rom> <hack name> [--git]");
            return ExitCodes.InvalidArguments;
        }

        string folder = args[0], romPath = args[1], name = args[2];

        if (!CommandHelpers.TryLoadRom(romPath, out N64Rom? rom))
        {
            return ExitCodes.InvalidRom;
        }

        // Only a clean, supported ROM may be the base of a project; otherwise the patch
        // would only work for people who have the same modified file.
        RomVerificationResult verification = RomVerifier.Verify(rom);
        if (!verification.IsUsableAsBase)
        {
            Console.Error.WriteLine($"Cannot create project: {verification.Message}");
            return ExitCodes.VerificationFailed;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Create(folder, name, verification.Sha1, romPath, prepareForGit);
            Console.WriteLine($"Created project '{name}' in {project.Folder}");
            if (prepareForGit)
            {
                Console.WriteLine("Added a .gitignore for version control with Git.");
            }

            Console.WriteLine($"Next: pw64cli project-extract \"{folder}\" <table index>   to copy a game file for editing");
        });
    }

    public static int Extract(string[] args)
    {
        if (!TryParseRomOption(ref args, out string? romOverride))
        {
            return ExitCodes.InvalidArguments;
        }

        if (args.Length != 2 || !int.TryParse(args[1], out int index))
        {
            Console.Error.WriteLine("Usage: pw64cli project-extract <folder> <table index> [--rom <clean rom>]");
            return ExitCodes.InvalidArguments;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Load(args[0]);
            GameFileSystem fs = LoadCleanFileSystem(project, romOverride);

            GameFile original = fs.Files.FirstOrDefault(f => f.TableIndex == index)
                ?? throw new ProjectException($"The game has no file with table index {index}.");

            string path = project.AddOverride(original);
            Console.WriteLine($"Copied file {index} ({original.FileType} #{original.GroupIndex}, {original.Size:N0} bytes) to {path}");
            Console.WriteLine("Edit it (e.g. with a hex editor), then run project-build.");
        });
    }

    public static int Revert(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out int index))
        {
            Console.Error.WriteLine("Usage: pw64cli project-revert <folder> <table index>");
            return ExitCodes.InvalidArguments;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Load(args[0]);
            Console.WriteLine(project.RemoveOverride(index)
                ? $"Removed the replacement for file {index}. The build uses the original again."
                : $"The project has no replacement for file {index}.");
        });
    }

    public static int Status(string[] args)
    {
        if (!TryParseRomOption(ref args, out string? romOverride))
        {
            return ExitCodes.InvalidArguments;
        }

        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: pw64cli project-status <folder> [--rom <clean rom>]");
            return ExitCodes.InvalidArguments;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Load(args[0]);
            ProjectSettings s = project.Settings;

            Console.WriteLine($"Project : {s.Name} {s.Version}{(s.Author.Length > 0 ? $" by {s.Author}" : string.Empty)}");
            Console.WriteLine($"Folder  : {project.Folder}");
            Console.WriteLine($"Base ROM: {project.Local.CleanRomPath ?? "(not set, use --rom)"}");
            Console.WriteLine($"Hack ROM: {project.OutputRomPath}");
            Console.WriteLine($"Options : relocate audio {(s.RelocateAudio ? "on" : "off")}, expansion {(s.AllowExpansion ? "on" : "off")}");
            Console.WriteLine();

            GameFileSystem fs = LoadCleanFileSystem(project, romOverride);
            IReadOnlyList<OverrideStatus> statuses = ProjectStatus.Compute(project, fs);

            if (statuses.Count == 0)
            {
                Console.WriteLine("No replacement files yet. The build would produce the original ROM.");
                return;
            }

            Console.WriteLine("  File                                State       Size");
            foreach (OverrideStatus status in statuses)
            {
                string name = Path.GetFileName(status.Override.Path);
                Console.WriteLine($"  {name,-34}  {status.State,-10}  {status.OriginalSize,9:N0} -> {status.NewSize,9:N0}  {status.Message}");
            }
        });
    }

    public static int Build(string[] args)
    {
        // --backup          create a restore point after building (project files + hack ROM)
        // --output <path>   write the hack ROM to this path; remembered for later builds
        bool backup = CommandHelpers.ExtractFlag(ref args, "--backup");
        if (!TryParseRomOption(ref args, out string? romOverride)
            || !TryParseStringOption(ref args, "--output", out string? outputOverride))
        {
            return ExitCodes.InvalidArguments;
        }

        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: pw64cli project-build <folder> [--rom <clean rom>] [--output <hack rom>] [--backup]");
            return ExitCodes.InvalidArguments;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Load(args[0]);
            N64Rom cleanRom = LoadCleanRom(project, romOverride);

            if (outputOverride is not null)
            {
                project.SetOutputRomPath(outputOverride); // throws (and saves nothing) for the clean ROM path
                project.Save();
            }

            Console.WriteLine($"Building '{project.Settings.Name}'...");
            ProjectBuildResult result = ProjectBuilder.Build(project, cleanRom, RomLayout.PilotwingsUsa);
            string romPath = ProjectBuilder.WriteRom(project, result);

            var rom = result.RomBuild;
            Console.WriteLine($"Applied {result.AppliedOverrides} replacement file(s).");
            Console.WriteLine($"Audio data at 0x{rom.AudioOffset:X}{(rom.AudioRelocated ? " (relocated)" : string.Empty)}, " +
                              $"ROM size {rom.Rom.Size / (1024 * 1024)} MiB, room for {rom.FreeSpace:N0} more bytes");
            Console.WriteLine($"Hack ROM written to {romPath}");

            if (backup)
            {
                BackupInfo info = ProjectBackups.Create(project);
                Console.WriteLine($"Restore point created: {info.Name}");
            }
        });
    }

    public static int Fixes(string[] args)
    {
        bool apply = CommandHelpers.ExtractFlag(ref args, "--apply");
        if (!TryParseRomOption(ref args, out string? romOverride))
        {
            return ExitCodes.InvalidArguments;
        }

        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: pw64cli project-fixes <folder> [--apply] [--rom <clean rom>]");
            return ExitCodes.InvalidArguments;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Load(args[0]);
            IReadOnlyList<CodeFix> missing = project.MissingCodeFixes;

            foreach (CodeFix fix in CodeFixes.All)
            {
                bool applied = !missing.Contains(fix);
                Console.WriteLine($"[{(applied ? "x" : " ")}] {fix.Name} ({fix.Id})");
                Console.WriteLine($"      Problem: {fix.Problem}");
                Console.WriteLine($"      Fix:     {fix.Solution}");
            }

            if (missing.Count == 0)
            {
                Console.WriteLine("All code fixes are applied.");
                return;
            }

            if (!apply)
            {
                Console.WriteLine($"{missing.Count} fix(es) not applied. Run again with --apply to apply them and build the hack ROM.");
                return;
            }

            N64Rom cleanRom = LoadCleanRom(project, romOverride);
            foreach (CodeFix fix in missing)
            {
                project.AddCodeFix(fix);
            }

            // Build first, save afterwards: if the build fails, the project stays as it was.
            ProjectBuildResult result = ProjectBuilder.Build(project, cleanRom, RomLayout.PilotwingsUsa);
            string romPath = ProjectBuilder.WriteRom(project, result);
            project.Save();
            Console.WriteLine($"Applied {missing.Count} code fix(es). Hack ROM written to {romPath}");
        });
    }

    public static int Patch(string[] args)
    {
        if (!TryParseRomOption(ref args, out string? romOverride))
        {
            return ExitCodes.InvalidArguments;
        }

        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: pw64cli project-patch <folder> <patch.bps> [--rom <clean rom>]");
            return ExitCodes.InvalidArguments;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Load(args[0]);
            N64Rom cleanRom = LoadCleanRom(project, romOverride);

            // Build fresh from the project instead of reading the hack ROM on disk:
            // that file could be outdated or replaced by hand.
            Console.WriteLine($"Building '{project.Settings.Name}' and creating patch...");
            ProjectBuildResult result = ProjectBuilder.Build(project, cleanRom, RomLayout.PilotwingsUsa);
            byte[] patch = ProjectBuilder.CreatePatch(project, cleanRom, result.RomBuild.Rom);

            File.WriteAllBytes(args[1], patch);
            Console.WriteLine($"Patch saved to {args[1]} ({patch.Length:N0} bytes, verified).");
        });
    }

    public static int Backup(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: pw64cli project-backup <folder>");
            return ExitCodes.InvalidArguments;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Load(args[0]);
            BackupInfo info = ProjectBackups.Create(project);
            Console.WriteLine($"Restore point created: {info.Name} ({info.FileCount} file(s){(info.ContainsRom ? ", with hack ROM" : ", no hack ROM built yet")})");
        });
    }

    public static int Backups(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: pw64cli project-backups <folder>");
            return ExitCodes.InvalidArguments;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Load(args[0]);
            IReadOnlyList<BackupInfo> backups = ProjectBackups.List(project);

            if (backups.Count == 0)
            {
                Console.WriteLine("No restore points yet. Create one with project-backup or project-build --backup.");
                return;
            }

            Console.WriteLine("  Name                   Files  Hack ROM");
            foreach (BackupInfo backup in backups)
            {
                Console.WriteLine($"  {backup.Name,-21}  {backup.FileCount,5}  {(backup.ContainsRom ? "yes" : "no")}");
            }
        });
    }

    public static int Restore(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: pw64cli project-restore <folder> <restore point name>");
            return ExitCodes.InvalidArguments;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Load(args[0]);
            HackProject restored = ProjectBackups.Restore(project, args[1]);
            Console.WriteLine($"Project '{restored.Settings.Name}' restored to {args[1]}.");
        });
    }

    public static int DeleteBackup(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: pw64cli project-backup-delete <folder> <restore point name>");
            return ExitCodes.InvalidArguments;
        }

        return Run(() =>
        {
            HackProject project = HackProject.Load(args[0]);
            ProjectBackups.Delete(project, args[1]);
            Console.WriteLine($"Restore point {args[1]} deleted.");
        });
    }

    /// <summary>
    /// Runs a command body and turns expected errors into friendly messages.
    /// </summary>
    private static int Run(Action body)
    {
        try
        {
            body();
            return ExitCodes.Success;
        }
        catch (ProjectException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCodes.BuildFailed;
        }
        catch (FileSystemFullException ex)
        {
            Console.Error.WriteLine($"Build failed: {ex.Message}");
            Console.Error.WriteLine("Tip: enable \"AllowExpansion\" in project.json to allow a larger ROM.");
            return ExitCodes.BuildFailed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidRomException or InvalidDataException)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCodes.BuildFailed;
        }
    }

    private static bool TryParseRomOption(ref string[] args, out string? romPath) =>
        TryParseStringOption(ref args, "--rom", out romPath);

    private static bool TryParseStringOption(ref string[] args, string option, out string? value)
    {
        try
        {
            value = CommandHelpers.ExtractStringOption(ref args, option);
            return true;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Loads the clean ROM for a project. A path given with --rom is remembered
    /// in the local settings, so it does not have to be repeated next time.
    /// </summary>
    private static N64Rom LoadCleanRom(HackProject project, string? romOverride)
    {
        string? path = romOverride ?? project.Local.CleanRomPath;
        if (path is null)
        {
            throw new ProjectException("The project does not know where the clean ROM is. Add --rom <path>.");
        }

        if (!File.Exists(path))
        {
            throw new ProjectException($"Clean ROM not found at '{path}'. Add --rom <path> with the current location.");
        }

        N64Rom rom = N64Rom.Load(path);
        if (!string.Equals(rom.ComputeSha1(), project.Settings.BaseRomSha1, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectException($"'{path}' is not the clean ROM this project is based on.");
        }

        if (romOverride is not null)
        {
            project.Local.CleanRomPath = Path.GetFullPath(romOverride);
            project.Save();
        }

        return rom;
    }

    private static GameFileSystem LoadCleanFileSystem(HackProject project, string? romOverride) =>
        GameFileSystem.Read(LoadCleanRom(project, romOverride), RomLayout.PilotwingsUsa);
}
