using System.Buffers.Binary;
using PW64Editor.Core.Build;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Hashing;
using PW64Editor.Core.Localization;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Patching;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Project;

/// <summary>How important a note of an import is.</summary>
public enum ImportNoteKind
{
    /// <summary>What was taken over.</summary>
    Info,

    /// <summary>Something that could not be taken over, or a difference of the rebuilt ROM.</summary>
    Warning,
}

/// <summary>One line of the report of an import (in the editor's language).</summary>
public sealed record ImportNote(ImportNoteKind Kind, string Text);

/// <summary>
/// What <see cref="ProjectImporter.Analyze"/> found in a hack ROM: the settings of the new project,
/// the game files and songs that differ from the clean ROM, and notes for the user.
/// </summary>
public sealed class RomImport
{
    /// <summary>The settings of the new project (name, version, code fixes, game patches, features, missions).</summary>
    public required ProjectSettings Settings { get; init; }

    /// <summary>The game files that differ: the clean ROM's file (table index, type, name) with the hack's content.</summary>
    public required IReadOnlyList<GameFile> ChangedFiles { get; init; }

    /// <summary>The songs that differ, by number, in the game's format (they become imported songs).</summary>
    public required IReadOnlyDictionary<int, byte[]> Songs { get; init; }

    /// <summary>What was taken over and what not.</summary>
    public required IReadOnlyList<ImportNote> Notes { get; init; }

    /// <summary>True if the ROM was built by Hawk's Hangar (it has the editor's game module).</summary>
    public bool MadeWithEditor { get; init; }

    /// <summary>The header of the editor's game module, or null.</summary>
    public GameModuleHeader? Module { get; init; }
}

/// <summary>
/// Makes a project from a hack ROM (or a BPS patch): the reverse of <see cref="ProjectBuilder"/>.
/// </summary>
/// <remarks>
/// <para>What is taken over:</para>
/// <list type="bullet">
///   <item>Every game file that differs from the clean ROM becomes a replacement file (the texts too, so all
///         mission texts stay exactly as in the ROM).</item>
///   <item>Every song that differs becomes an imported song, byte for byte.</item>
///   <item>The code fixes and game patches the game code contains.</item>
///   <item>From a ROM built by Hawk's Hangar also: name and version (the information line of the game
///         module), the features (debug output, live values), the mission settings of the data area (conditions,
///         unlimited fuel, hard end time and its jingle), the changed result rows and the hidden "View Demo"
///         buttons.</item>
/// </list>
/// <para>Everything else (other changes of the game code, of the instruments or of other data) cannot be
/// expressed by a project. After the import the project is built and compared with the ROM
/// (<see cref="Compare"/>), so the user sees exactly what is different.</para>
/// </remarks>
public static class ProjectImporter
{
    /// <summary>The end of the patch metadata Hawk's Hangar writes (see ProjectBuilder).</summary>
    private const string MetadataSuffix = " - made with Hawk's Hangar for Pilotwings 64 (USA)";

    /// <summary>Places closer together than this count as one place in the comparison.</summary>
    private const int MergeGap = 16;

    /// <summary>
    /// Applies a BPS patch to the clean ROM.
    /// </summary>
    /// <returns>The patched ROM and the metadata text of the patch.</returns>
    /// <exception cref="ProjectException">The patch is damaged or was made for another ROM.</exception>
    public static (N64Rom Rom, string Metadata) ApplyPatch(N64Rom cleanRom, ReadOnlySpan<byte> patch)
    {
        try
        {
            BpsPatchInfo info = BpsReader.ReadInfo(patch);
            if (info.SourceCrc32 != Crc32.Compute(cleanRom.Data) || info.SourceSize != cleanRom.Size)
            {
                throw new ProjectException(CoreText.T("The patch was not made for the clean ROM of Pilotwings 64 (USA). " +
                    "A project can only be made from a patch for that ROM."));
            }

            return (N64Rom.FromBytes(BpsReader.Apply(cleanRom.Data, patch)), info.Metadata);
        }
        catch (BpsException ex)
        {
            throw new ProjectException(CoreText.F("The patch cannot be used: {0}", ex.Message), ex);
        }
        catch (InvalidRomException ex)
        {
            throw new ProjectException(CoreText.F("The patch does not produce a valid ROM: {0}", ex.Message), ex);
        }
    }

    /// <summary>
    /// Finds out what a hack ROM changes compared with the clean ROM.
    /// </summary>
    /// <param name="hackRom">The hack ROM.</param>
    /// <param name="cleanRom">The verified clean ROM.</param>
    /// <param name="layout">The fixed addresses of the game release.</param>
    /// <param name="fallbackName">The project name if the ROM has none (usually the file name).</param>
    /// <param name="patchMetadata">The metadata of the BPS patch the ROM came from (author), or null.</param>
    /// <exception cref="ProjectException">The ROM is not Pilotwings 64 (USA), or its files cannot be read.</exception>
    public static RomImport Analyze(N64Rom hackRom, N64Rom cleanRom, RomLayout layout, string fallbackName, string? patchMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(hackRom);
        ArgumentNullException.ThrowIfNull(cleanRom);
        ArgumentNullException.ThrowIfNull(layout);

        if (hackRom.Header.GameCode != cleanRom.Header.GameCode || hackRom.Header.Version != cleanRom.Header.Version)
        {
            throw new ProjectException(CoreText.F("This ROM is not Pilotwings 64 (USA) (game code {0}, expected {1}).",
                hackRom.Header.GameCode, cleanRom.Header.GameCode));
        }

        byte[] hack = hackRom.Data;
        var notes = new List<ImportNote>();
        var settings = new ProjectSettings { Name = fallbackName, BaseRomSha1 = cleanRom.ComputeSha1() };
        if (string.Equals(hackRom.ComputeSha1(), settings.BaseRomSha1, StringComparison.OrdinalIgnoreCase))
        {
            notes.Add(Info(CoreText.T("The ROM is the clean ROM itself; the project starts without changes.")));
        }

        // The editor's game module: name, version and features.
        GameModuleHeader? module = GameModule.ReadHeader(hack);
        if (module is not null)
        {
            if (module.Project is { } project && project.Name.Trim().Length > 0)
            {
                settings.Name = project.Name;
                settings.Version = project.Version;
            }

            settings.Author = AuthorFromMetadata(patchMetadata, settings) ?? string.Empty;
            ReadFeatures(module, settings.Features);
            notes.Add(Info(CoreText.F("The ROM was built with Hawk's Hangar (\"{0}\").", module.Info)));
        }
        else
        {
            notes.Add(Info(CoreText.T("The ROM was not built with Hawk's Hangar: only game files, songs and code fixes can be taken over.")));
        }

        // Game files.
        GameFileSystem cleanFs = GameFileSystem.Read(cleanRom, layout);
        GameFileSystem hackFs;
        try
        {
            hackFs = GameFileSystem.Read(hackRom, layout);
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(CoreText.F("The game files of the ROM cannot be read: {0}", ex.Message), ex);
        }

        // With the game patch "Bonus games and Birdman" the build adds the level texts to the text file; a text file that
        // differs from the original only by them is not a change of its own.
        (int Index, byte[] Data)? patchTexts = null;
        if (GamePatches.BonusGames.AsCodeFix.GetState(hack, CodeFixes.CodeStart, CodeFixes.CodeEnd(layout)) == CodeFixState.Applied)
        {
            try
            {
                GameFile cleanText = Text.GameTextFile.FindFile(cleanFs);
                IReadOnlyList<TestId> birdman = module is not null && GameModule.ReadDataImage(hack, module) is { } image
                    ? ResultTables.BirdmanPointsFromMask(DataArea.ReadBirdmanPoints(image))
                    : [];
                if (ResultTables.AddLevelTexts(cleanText.Data, birdman, Workspace.EditorSession.EncodeFor(Text.TextFont.Load(cleanFs))) is { } built)
                {
                    patchTexts = (cleanText.TableIndex, built);
                }
            }
            catch (InvalidDataException)
            {
                // not the retail layout (test data)
            }
        }

        List<GameFile> changed = CompareFiles(cleanFs, hackFs, notes, patchTexts);
        List<GameFile> hackFiles = cleanFs.Files.Select(f => changed.FirstOrDefault(c => c.TableIndex == f.TableIndex) ?? f).ToList();

        // Music, and where the audio data lies.
        Dictionary<int, byte[]> songs = CompareMusic(hack, cleanRom.Data, layout, notes, out int? sequenceOffset);
        settings.RelocateAudio = sequenceOffset is { } seq && layout.Audio is not null && seq != layout.Audio.SequenceOffset
            && hackFs.EndOffset <= layout.FileSystemLimit;
        settings.AllowExpansion = hack.Length > ProjectBuilder.HackRomSize;

        // Code fixes and game patches.
        int codeStart = CodeFixes.CodeStart, codeEnd = CodeFixes.CodeEnd(layout);
        List<(int Start, int End)> moduleRanges = module is null ? [] : ModulePatchRanges(module);
        foreach (CodeFix fix in CodeFixes.All)
        {
            if (Detect(fix, fix.Name, hack, codeStart, codeEnd, moduleRanges, notes))
            {
                settings.AppliedCodeFixes.Add(fix.Id);
            }
        }

        foreach (GamePatch patch in GamePatches.All)
        {
            if (Detect(patch.AsCodeFix, patch.Name, hack, codeStart, codeEnd, moduleRanges, notes))
            {
                settings.AppliedPatches.Add(patch.Id);
            }
        }

        List<string> taken = [.. CodeFixes.All.Where(f => settings.AppliedCodeFixes.Contains(f.Id)).Select(f => CoreText.T(f.Name)),
            .. GamePatches.All.Where(p => settings.AppliedPatches.Contains(p.Id)).Select(p => CoreText.T(p.Name))];
        if (taken.Count > 0)
        {
            notes.Add(Info(CoreText.F("Code fixes and game patches found: {0}.", string.Join("; ", taken))));
        }

        // Missions.
        ReadMissions(hack, cleanRom.Data, layout, module, hackFiles, settings, notes);

        int textIndex = -1;
        try
        {
            textIndex = Text.GameTextFile.FindFile(cleanFs).TableIndex;
        }
        catch (InvalidDataException)
        {
            // not the retail layout (test data)
        }

        if (changed.Any(f => f.TableIndex == textIndex))
        {
            notes.Add(Info(CoreText.T("The texts are taken over as they are in the ROM; the editor writes mission texts anew only when you change a mission's points.")));
        }

        return new RomImport
        {
            Settings = settings,
            ChangedFiles = changed,
            Songs = songs,
            Notes = notes,
            MadeWithEditor = module is not null,
            Module = module,
        };
    }

    /// <summary>
    /// Creates the project of an import: settings, replacement files and imported songs.
    /// </summary>
    /// <param name="import">What <see cref="Analyze"/> found.</param>
    /// <param name="folder">The new project folder (new or empty).</param>
    /// <param name="name">Name of the project.</param>
    /// <param name="cleanRomPath">Where the clean ROM is stored on this computer.</param>
    /// <param name="prepareForGit">Add a .gitignore.</param>
    /// <exception cref="ProjectException">The folder is not empty.</exception>
    public static HackProject CreateProject(RomImport import, string folder, string name, string? cleanRomPath, bool prepareForGit)
    {
        ArgumentNullException.ThrowIfNull(import);
        import.Settings.Name = name;
        HackProject project = HackProject.Create(folder, import.Settings, cleanRomPath, prepareForGit);
        foreach (GameFile file in import.ChangedFiles)
        {
            File.WriteAllBytes(Path.Combine(project.FilesFolder, file.ExportName), file.Data);
        }

        foreach ((int song, byte[] sequence) in import.Songs)
        {
            project.WriteImportedSong(song, sequence);
        }

        return project;
    }

    /// <summary>
    /// Compares the ROM built from the imported project with the imported ROM and says where they differ.
    /// The information line of the game module (it holds the build time) is not compared.
    /// </summary>
    /// <param name="built">The build of the new project.</param>
    /// <param name="hack">The imported ROM.</param>
    /// <param name="layout">The fixed addresses of the game release.</param>
    /// <param name="importedModule">The game module of the imported ROM, or null.</param>
    public static IReadOnlyList<ImportNote> Compare(RomBuildResult built, ReadOnlySpan<byte> hack, RomLayout layout, GameModuleHeader? importedModule)
    {
        ArgumentNullException.ThrowIfNull(built);
        byte[] rom = built.Rom.Data;
        var notes = new List<ImportNote>();
        if (rom.Length != hack.Length)
        {
            notes.Add(Info(CoreText.F("The imported ROM has {0:N0} bytes, the project builds {1:N0} (every project builds a ROM of 16 MiB).",
                hack.Length, rom.Length)));
        }

        (int infoOffset, int infoLength) = GameModule.ReadHeader(rom) is not null ? GameModule.InfoRange(rom.Length) : (-1, 0);
        var regions = new List<(string Name, int Start, int End)>
        {
            (CoreText.T("ROM header"), 0, 0x40),
            (CoreText.T("Boot code"), 0x40, CodeFixes.CodeStart),
            (CoreText.T("Game code"), CodeFixes.CodeStart, CodeFixes.CodeEnd(layout)),
            (CoreText.T("Game files"), layout.FileTableOffset, built.FileSystemEnd),
        };
        if (layout.Audio is { } audio)
        {
            int bank = (int)MipsAddressPatcher.ReadValue(rom, audio.BankReferences[0]);
            regions.Add((CoreText.T("Music and sounds"), built.AudioOffset, bank + (audio.EndOffset - audio.BankOffset)));
        }

        string moduleName = CoreText.T("Game module and data area");
        if (infoOffset >= 0)
        {
            regions.Add((moduleName, rom.Length - GameModule.TotalRomReserve, rom.Length));
        }

        // Where the imported ROM had its module (a ROM of another size: elsewhere).
        if (importedModule is not null)
        {
            int start = importedModule.DataSize > 0 && importedModule.DataRomOffset > 0
                ? Math.Min(importedModule.DataRomOffset, importedModule.RomOffset)
                : importedModule.RomOffset;
            regions.Add((moduleName, start, importedModule.RomOffset + GameModule.RomReserve));
        }

        // The changes of the game code that load and use the module: with another module (or none) in the
        // imported ROM they are expected to differ.
        GameModuleHeader? builtModule = GameModule.ReadHeader(rom);
        bool otherModule = importedModule is null || builtModule is null || importedModule.Version != builtModule.Version;
        List<(int Start, int End)> moduleCode = builtModule is null ? [] : ModulePatchRanges(builtModule);
        int moduleCodeBytes = 0;

        // The differing places, close ones merged.
        int length = Math.Min(rom.Length, hack.Length);
        var places = new List<(int Start, int End)>();
        for (int i = 0; i < length; i++)
        {
            if (rom[i] == hack[i] || (i >= infoOffset && i < infoOffset + infoLength))
            {
                continue;
            }

            if (otherModule && (moduleCode.Any(r => i >= r.Start && i < r.End) || BothModuleCalls(rom, hack, i)))
            {
                moduleCodeBytes++;
                continue;
            }

            if (places.Count > 0 && i - places[^1].End <= MergeGap)
            {
                places[^1] = (places[^1].Start, i + 1);
            }
            else
            {
                places.Add((i, i + 1));
            }
        }

        // The boot checksum follows from the code: only worth a note when nothing else in the checksummed part differs.
        const int checksumStart = 0x10, checksumEnd = 0x18, checksummedEnd = 0x101000;
        if (moduleCodeBytes > 0 || places.Any(p => p.Start >= CodeFixes.CodeStart && p.Start < checksummedEnd))
        {
            places.RemoveAll(p => p.Start >= checksumStart && p.End <= checksumEnd);
        }

        if (moduleCodeBytes > 0)
        {
            notes.Add(Info(importedModule is null
                ? CoreText.T("The project adds the game module of Hawk's Hangar (every hack built by the editor has it, for example for the Expansion Pak check).")
                : CoreText.F("The ROM was built with another version of Hawk's Hangar (game module version {0}, now {1}); " +
                    "the project builds the current game module.", importedModule.Version, GameModule.CurrentVersion)));
        }

        if (places.Count == 0)
        {
            if (rom.Length == hack.Length)
            {
                notes.Add(Info(CoreText.T("The project builds exactly the imported ROM (apart from the build time in the game module).")));
            }

            return notes;
        }

        string otherName = CoreText.T("Other data");
        foreach (var group in places.GroupBy(p => regions.FirstOrDefault(r => p.Start >= r.Start && p.Start < r.End).Name ?? otherName))
        {
            int bytes = group.Sum(p => p.End - p.Start);
            int count = group.Count();
            bool moduleRegion = group.Key == moduleName;
            if (moduleRegion && otherModule)
            {
                continue; // explained above (or the imported ROM had free space there)
            }

            notes.Add(Warning(CoreText.F("Difference in the rebuilt ROM – {0}: {1:N0} bytes in {2:N0} places, the first at 0x{3:X6}.",
                group.Key, bytes, count, group.First().Start)));
        }

        return notes;
    }

    private static ImportNote Info(string text) => new(ImportNoteKind.Info, text);

    private static ImportNote Warning(string text) => new(ImportNoteKind.Warning, text);

    /// <summary>The author from the metadata Hawk's Hangar writes into its patches ("name version by author - made with …").</summary>
    private static string? AuthorFromMetadata(string? metadata, ProjectSettings settings)
    {
        if (metadata is null || !metadata.EndsWith(MetadataSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        string text = metadata[..^MetadataSuffix.Length];
        string prefix = $"{settings.Name} {settings.Version} by ";
        return text.StartsWith(prefix, StringComparison.Ordinal) && text.Length > prefix.Length ? text[prefix.Length..] : null;
    }

    private static void ReadFeatures(GameModuleHeader module, GameFeatureSettings features)
    {
        features.DebugOutput = module.DebugOutput;
        if (!module.DebugOutput)
        {
            return; // the other switches only count with the debug output
        }

        features.StartupInfo = module.StartupInfo;
        features.GameMessages = module.GameMessages;
        features.OnScreen = module.OnScreen;
        features.LiveValues = module.LiveValuesShown && module.Live != LiveValues.None;
        if (features.LiveValues)
        {
            features.LiveDistance = module.Live.HasFlag(LiveValues.Distance);
            features.LiveAltitude = module.Live.HasFlag(LiveValues.HighestAltitude);
            features.LiveHorizontalSpeed = module.Live.HasFlag(LiveValues.HorizontalSpeed);
            features.LiveVerticalSpeed = module.Live.HasFlag(LiveValues.VerticalSpeed);
        }
    }

    /// <summary>The game files of the hack that differ from the clean ROM (with the clean ROM's table index and name).</summary>
    /// <param name="clean">The clean ROM's files.</param>
    /// <param name="hack">The hack's files.</param>
    /// <param name="notes">The report.</param>
    /// <param name="builtTextFile">The text file a build makes from the original one by itself (game patch "Bonus games and
    /// Birdman"), or null: the hack's text file is no change if it is this one.</param>
    private static List<GameFile> CompareFiles(GameFileSystem clean, GameFileSystem hack, List<ImportNote> notes, (int Index, byte[] Data)? builtTextFile = null)
    {
        var changed = new List<GameFile>();
        int otherType = 0;
        foreach (GameFile file in hack.Files)
        {
            GameFile? original = clean.Files.FirstOrDefault(f => f.TableIndex == file.TableIndex);
            if (original is null)
            {
                continue; // counted below
            }

            if (original.FileType != file.FileType)
            {
                otherType++;
            }
            else if (!original.Data.AsSpan().SequenceEqual(file.Data)
                && !(builtTextFile is { } text && text.Index == file.TableIndex && text.Data.AsSpan().SequenceEqual(file.Data)))
            {
                changed.Add(original with { Data = file.Data });
            }
        }

        int added = hack.Files.Count(f => clean.Files.All(c => c.TableIndex != f.TableIndex));
        int missing = clean.Files.Count(c => hack.Files.All(f => f.TableIndex != c.TableIndex));
        notes.Add(Info(CoreText.F("{0} changed game files taken over as replacement files.", changed.Count)));
        if (added > 0)
        {
            notes.Add(Warning(CoreText.F("The ROM has {0} additional game files. A project can only replace the game's files, so they are not taken over.", added)));
        }

        if (missing > 0 || otherType > 0)
        {
            notes.Add(Warning(CoreText.F("{0} game files of the ROM are missing or of another type than in the original. They are not taken over.", missing + otherType)));
        }

        return changed;
    }

    /// <summary>The songs of the hack that differ from the clean ROM.</summary>
    /// <param name="sequenceOffset">Where the music file of the hack lies, or null if it cannot be read.</param>
    private static Dictionary<int, byte[]> CompareMusic(byte[] hack, byte[] clean, RomLayout layout, List<ImportNote> notes, out int? sequenceOffset)
    {
        var songs = new Dictionary<int, byte[]>();
        sequenceOffset = null;
        if (layout.Audio is not { } audio)
        {
            return songs;
        }

        Audio.SequenceBank cleanBank = Audio.SequenceBank.Parse(clean.AsSpan(audio.SequenceOffset, audio.SequenceSize));
        Audio.SequenceBank hackBank;
        int seq, bank;
        try
        {
            seq = audio.ReadCurrentOffset(hack);
            bank = (int)MipsAddressPatcher.ReadValue(hack, audio.BankReferences[0]);
            if (seq < 0 || bank <= seq || bank + (audio.EndOffset - audio.BankOffset) > hack.Length)
            {
                throw new InvalidDataException("The audio data lies outside the ROM.");
            }

            hackBank = Audio.SequenceBank.Parse(hack.AsSpan(seq, bank - seq));
        }
        catch (InvalidDataException)
        {
            notes.Add(Warning(CoreText.T("The music of the ROM cannot be read; no songs are taken over.")));
            return songs;
        }

        sequenceOffset = seq;
        if (hackBank.Sequences.Count != cleanBank.Sequences.Count)
        {
            notes.Add(Warning(CoreText.F("The ROM has {0} songs instead of {1}; no songs are taken over.", hackBank.Sequences.Count, cleanBank.Sequences.Count)));
            return songs;
        }

        int maxSize = cleanBank.Sequences.Max(s => s.Length);
        for (int i = 0; i < cleanBank.Sequences.Count; i++)
        {
            byte[] song = hackBank.Sequences[i];
            if (song.AsSpan().SequenceEqual(cleanBank.Sequences[i]))
            {
                continue;
            }

            if (song.Length > maxSize)
            {
                notes.Add(Warning(CoreText.F("Song {0:D2} is larger than the game has room for; it is not taken over.", i)));
                continue;
            }

            songs[i] = song;
        }

        if (songs.Count > 0)
        {
            notes.Add(Info(CoreText.F("{0} changed songs taken over as imported songs: {1}.", songs.Count,
                string.Join(", ", songs.Keys.Select(k => k.ToString("D2", System.Globalization.CultureInfo.InvariantCulture))))));
        }

        // Instruments and samples behind the music file: a project cannot change them.
        int bankLength = audio.EndOffset - audio.BankOffset;
        if (!hack.AsSpan(bank, bankLength).SequenceEqual(clean.AsSpan(audio.BankOffset, bankLength)))
        {
            notes.Add(Warning(CoreText.T("The instruments or sound samples of the ROM are changed. A project cannot change them, so they are not taken over.")));
        }

        return songs;
    }

    /// <summary>
    /// The ROM ranges the game module changes in the game code (with every feature on). Code fixes that share
    /// these places (the memory manager, the code cave) are judged by their other pieces.
    /// </summary>
    private static List<(int Start, int End)> ModulePatchRanges(GameModuleHeader module)
    {
        var all = new GameModuleOptions(true, true, true, true)
        {
            Live = LiveValues.Distance,
            Conditions = [new GameModuleCondition(0, 0, 0, true, 1, new byte[GameModule.ObjectTypes], UnlimitedFuel: true, HardEnd: HardEndScoring.Scored)],
            PenaltyDigits = new ushort[10],
            CountdownTexts = Enumerable.Repeat<IReadOnlyList<ushort>>([0xFFFF], 6).ToList(),
            DistanceRow = true,
        };
        return GameModule.Patches(all, module.RomOffset, Math.Max(module.ImageSize, 8))
            .Select(p => (p.RomOffset, p.RomOffset + (p.Replacement.Length * 4)))
            .ToList();
    }

    /// <summary>True if the hack contains the code fix (or game patch); notes pieces that are only partly there.</summary>
    private static bool Detect(CodeFix fix, string name, byte[] hack, int codeStart, int codeEnd, List<(int Start, int End)> moduleRanges, List<ImportNote> notes)
    {
        int replaced = 0, original = 0, other = 0;
        foreach (CodePatch piece in fix.Patches)
        {
            int end = piece.RomOffset + piece.Original.Length;
            CodeFixState state = new CodeFix(fix.Id, fix.Name, fix.Problem, fix.Solution, [piece]).GetState(hack, codeStart, codeEnd);
            if (state is not (CodeFixState.Applied or CodeFixState.NotApplied) && moduleRanges.Any(r => piece.RomOffset < r.End && end > r.Start))
            {
                continue; // the game module changed this place too: judged by the other pieces
            }

            if (state is not (CodeFixState.Applied or CodeFixState.NotApplied) && MatchesWithOtherModuleCalls(hack, piece))
            {
                state = CodeFixState.Applied; // built with another module version: its functions lie elsewhere
            }

            switch (state)
            {
                case CodeFixState.Applied:
                    replaced++;
                    break;
                case CodeFixState.NotApplied:
                    original++;
                    break;
                default:
                    other++;
                    break;
            }
        }

        if (replaced > 0 && original == 0 && other == 0)
        {
            return true;
        }

        if (replaced > 0)
        {
            notes.Add(Warning(CoreText.F("\"{0}\" is only partly in the ROM, so it is not taken over. You can apply it in the project settings.", CoreText.T(name))));
        }
        else if (other > 0)
        {
            notes.Add(Warning(CoreText.F("The game code at the places of \"{0}\" is changed in another way; this change is not taken over.", CoreText.T(name))));
        }

        return false;
    }

    /// <summary>
    /// True if the hack has the replacement of a piece, where calls into the game module may go to other
    /// addresses (a ROM built with another version of the module).
    /// </summary>
    private static bool MatchesWithOtherModuleCalls(byte[] hack, CodePatch piece)
    {
        if (piece.RomOffset < 0 || piece.RomOffset + piece.Replacement.Length > hack.Length || piece.Replacement.Length % 4 != 0)
        {
            return false;
        }

        for (int i = 0; i < piece.Replacement.Length; i += 4)
        {
            uint want = BinaryPrimitives.ReadUInt32BigEndian(piece.Replacement.AsSpan(i));
            uint have = BinaryPrimitives.ReadUInt32BigEndian(hack.AsSpan(piece.RomOffset + i));
            if (want != have && !(IsModuleCall(want) && IsModuleCall(have)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True if the word around a game code byte is a call into the game module in both ROMs (to other addresses).</summary>
    private static bool BothModuleCalls(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int offset)
    {
        int word = offset & ~3;
        return word >= CodeFixes.CodeStart && word + 4 <= a.Length && word + 4 <= b.Length
            && IsModuleCall(BinaryPrimitives.ReadUInt32BigEndian(a[word..])) && IsModuleCall(BinaryPrimitives.ReadUInt32BigEndian(b[word..]));
    }

    /// <summary>True if the instruction is a jal into the code area of the game module.</summary>
    private static bool IsModuleCall(uint word)
    {
        uint target = 0x80000000u | ((word & 0x03FFFFFFu) << 2);
        return word >> 26 == 3 && target >= CodeArea.Start && target < CodeArea.End;
    }

    /// <summary>The mission settings: the entries of the data area, changed result rows and hidden demo buttons.</summary>
    private static void ReadMissions(byte[] hack, byte[] clean, RomLayout layout, GameModuleHeader? module, IReadOnlyList<GameFile> hackFiles,
        ProjectSettings settings, List<ImportNote> notes)
    {
        List<MissionComm> comms = hackFiles.Where(f => f.FileType == MissionFile.FormType)
            .Select(f => MissionFile.Parse(f.Data).Comm)
            .ToList();

        IReadOnlyList<GameModuleCondition> entries = [];
        if (module is not null)
        {
            try
            {
                entries = GameModule.ReadConditions(hack, module);
            }
            catch (InvalidDataException)
            {
                notes.Add(Warning(CoreText.T("The mission settings of the game module cannot be read; they are not taken over.")));
            }
        }

        // Game patch "Bonus games and Birdman": the Birdman missions with points and their rows (in the data area's tables).
        byte[]? image = module is null ? null : GameModule.ReadDataImage(hack, module);
        IReadOnlyList<TestId> birdmanPoints = image is null ? [] : ResultTables.BirdmanPointsFromMask(DataArea.ReadBirdmanPoints(image));
        byte[]? resultTables = image is null ? null : DataArea.ReadResultTables(image);

        MissionTables? cleanTables = layout.Missions is null ? null : new MissionTables(clean, layout.Missions);
        MissionTables? hackTables = layout.Missions is null ? null : new MissionTables(hack, layout.Missions);
        HashSet<Vehicle> hiddenDemos = HiddenDemos(hack);

        int withSettings = 0, rowsChanged = 0;
        foreach (MissionComm comm in comms)
        {
            TestId id = comm.Id;
            MissionSettings mission = MissionSettings.For(id);
            if (entries.FirstOrDefault(e => e.Vehicle == (byte)id.Vehicle && e.Class == id.Class && e.Test == id.Test) is { } entry)
            {
                if (entry.HasConditions)
                {
                    mission.Conditions = ToConditions(entry);
                }

                mission.UnlimitedFuel = entry.UnlimitedFuel;
                mission.HardEndZero = entry.HardEnd == HardEndScoring.Zero;
                if (entry.HardEnd != HardEndScoring.Retail)
                {
                    mission.HardEndJingle = JingleOf(id.Vehicle, entry.HardEndMusic, mission, notes);
                }

                // Landing: the Gyrocopter's touchdown curve, the pad radius and the vehicle's speed limits.
                if (entry.TouchdownCurve is { Count: > 0 } touchdown && id.Vehicle == Vehicle.Gyrocopter)
                {
                    mission.TouchdownCurve = [.. touchdown];
                }

                if (entry.PadRadius is { } radius && radius != LandingValues.Original(LandingValue.PadRadius))
                {
                    mission.Landing[LandingValue.PadRadius] = radius;
                }

                IReadOnlyList<LandingValue> speeds = LandingValues.SpeedsOf(id.Vehicle);
                for (int i = 0; i < speeds.Count && i < entry.VehicleLimits.Count; i++)
                {
                    if (entry.VehicleLimits[i] != LandingValues.Original(speeds[i]))
                    {
                        mission.Landing[speeds[i]] = entry.VehicleLimits[i];
                    }
                }
            }

            if (cleanTables is not null && hackTables is not null)
            {
                IReadOnlyList<int> hackRows = hackTables.ResultRows(id);
                if (hackRows.Count > 0 && !hackRows.SequenceEqual(cleanTables.ResultRows(id)))
                {
                    // Stored bottom row first, empty rows (0) at the top.
                    List<int> rows = hackRows.Take(hackRows.ToList().FindLastIndex(r => r != 0) + 1).Reverse().ToList();
                    if (rows.All(PointSources.IsKnown))
                    {
                        mission.ResultRows = rows;
                        rowsChanged++;
                    }
                    else
                    {
                        notes.Add(Warning(CoreText.F("The result sheet of {0} has rows the editor does not know; they are not taken over.", MissionCatalog.Label(id))));
                    }
                }
            }

            if (birdmanPoints.Contains(id))
            {
                mission.BirdmanPoints = true;
                if (resultTables is not null && ResultTables.RowsOffset(id) is { } offset && offset + MissionLayout.ResultRows <= resultTables.Length)
                {
                    List<int> stored = [.. resultTables.AsSpan(offset, MissionLayout.ResultRows).ToArray().Select(b => (int)b)];
                    List<int> rows = stored.Take(stored.FindLastIndex(r => r != 0) + 1).Reverse().ToList();
                    if (rows.Count > 0 && rows.All(PointSources.IsKnown))
                    {
                        mission.ResultRows = rows;
                    }
                }
            }

            if (DemoButton.HasButton(id) && hiddenDemos.Contains(id.Vehicle))
            {
                mission.HideDemo = true;
            }

            if (!mission.IsEmpty)
            {
                settings.Missions.Add(mission);
                withSettings++;
            }
        }

        int orphans = entries.Count(e => !comms.Any(c => (byte)c.Id.Vehicle == e.Vehicle && c.Id.Class == e.Class && c.Id.Test == e.Test));
        if (orphans > 0)
        {
            notes.Add(Warning(CoreText.F("The game module has settings for {0} missions the ROM does not have; they are not taken over.", orphans)));
        }

        if (withSettings > 0)
        {
            notes.Add(Info(CoreText.F("Settings of {0} missions taken over ({1} of them with changed result rows).", withSettings, rowsChanged)));
        }
    }

    private static MissionConditions ToConditions(GameModuleCondition entry)
    {
        var conditions = new MissionConditions
        {
            CrashGivesZero = entry.CrashGivesZero,
            CrashTimePoints = entry.CrashTimePoints,
            MinimumAltitude = entry.MinimumAltitude,
            MinimumDistance = entry.MinimumDistance,
        };
        for (int t = 0; t < entry.Required.Count && t < GameModule.ObjectTypes; t++)
        {
            if (entry.Required[t] != 0)
            {
                conditions.Required[(ConditionObject)t] = entry.Required[t] == GameModule.RequiredAll ? MissionConditions.All : entry.Required[t];
            }
        }

        return conditions;
    }

    /// <summary>The jingle setting that gives this music number: null for the default, otherwise the one set.</summary>
    private static HardEndJingle? JingleOf(Vehicle vehicle, byte music, MissionSettings mission, List<ImportNote> notes)
    {
        if (HardEndJingles.Music(vehicle, HardEndJingles.Effective(mission)) == music)
        {
            return null;
        }

        foreach (HardEndJingle jingle in Enum.GetValues<HardEndJingle>())
        {
            if (HardEndJingles.Music(vehicle, jingle) == music)
            {
                return jingle;
            }
        }

        notes.Add(Warning(CoreText.F("The jingle of the hard end time of {0} (music {1}) is not one the editor offers; the default is used.",
            MissionCatalog.Label(mission.Id), music)));
        return null;
    }

    /// <summary>The vehicles whose "View Demo" button the ROM hides (the editor's change, see <see cref="DemoButton"/>).</summary>
    private static HashSet<Vehicle> HiddenDemos(byte[] hack)
    {
        if (DemoButton.MatchesRom(hack))
        {
            return [];
        }

        Vehicle[] vehicles = [Vehicle.HangGlider, Vehicle.RocketBelt, Vehicle.Gyrocopter];
        for (int mask = 1; mask < 8; mask++)
        {
            List<Vehicle> hidden = vehicles.Where((_, i) => (mask & (1 << i)) != 0).ToList();
            if (DemoButton.Patches(hidden).All(p => p.Offset + p.Data.Length <= hack.Length && hack.AsSpan(p.Offset, p.Data.Length).SequenceEqual(p.Data)))
            {
                return [.. hidden];
            }
        }

        return [];
    }
}
