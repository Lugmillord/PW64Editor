using PW64Editor.Core.Audio;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Localization;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Text;
using PW64Editor.Core.Textures;

namespace PW64Editor.Core.Workspace;

/// <summary>A custom text to add (see <see cref="EditorSession.SaveTexts(GameTextLibrary, IReadOnlyDictionary{int, string}, IReadOnlyList{NewText}, IReadOnlyCollection{int})"/>).</summary>
/// <param name="Index">Its number: a free number at or above the original text count.</param>
/// <param name="Name">Its unique name.</param>
/// <param name="Markup">Its content.</param>
public sealed record NewText(int Index, string Name, string Markup);

/// <summary>
/// An open project in the editor, together with the clean ROM it is based on.
/// </summary>
/// <remarks>
/// This class bundles every project operation the user interface offers, so the UI code stays
/// small and the logic can be tested without a UI. Methods may take a moment (building
/// recompresses data, patches are verified); call them from a background thread in a UI.
/// </remarks>
public sealed partial class EditorSession
{
    private GameFileSystem? _cleanFileSystem;

    private EditorSession(HackProject project, N64Rom cleanRom, RomLayout layout)
    {
        Project = project;
        CleanRom = cleanRom;
        Layout = layout;
    }

    /// <summary>The open project. Replaced after restoring a restore point.</summary>
    public HackProject Project { get; private set; }

    /// <summary>The clean ROM the project is based on.</summary>
    public N64Rom CleanRom { get; }

    /// <summary>The fixed addresses of the game release.</summary>
    public RomLayout Layout { get; }

    /// <summary>The game files of the clean ROM (read once, then cached).</summary>
    public GameFileSystem CleanFileSystem => _cleanFileSystem ??= GameFileSystem.Read(CleanRom, Layout);

    /// <summary>
    /// Creates a new project and opens it.
    /// </summary>
    /// <param name="folder">Project folder (new or empty).</param>
    /// <param name="name">Name of the hack.</param>
    /// <param name="prepareForGit">Whether to add a .gitignore.</param>
    /// <param name="cleanRom">The verified clean ROM.</param>
    /// <param name="cleanRomPath">Where that ROM is stored (remembered in the project's local
    /// settings, so the command line tool can build the project too).</param>
    public static EditorSession Create(string folder, string name, bool prepareForGit, N64Rom cleanRom, string cleanRomPath)
    {
        HackProject project = HackProject.Create(folder, name, cleanRom.ComputeSha1(), cleanRomPath, prepareForGit);
        return new EditorSession(project, cleanRom, RomLayout.PilotwingsUsa);
    }

    /// <summary>
    /// Opens an existing project.
    /// </summary>
    /// <exception cref="ProjectException">No project in the folder, or it was made for a different ROM.</exception>
    public static EditorSession Open(string folder, N64Rom cleanRom, string cleanRomPath)
    {
        HackProject project = HackProject.Load(folder);

        if (!string.Equals(project.Settings.BaseRomSha1, cleanRom.ComputeSha1(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectException(
                CoreText.F("This project was made for a different base ROM than the one the editor uses. Project " +
                    "base ROM SHA-1: {0}.", project.Settings.BaseRomSha1));
        }

        // Point the project at the editor's clean ROM copy, so the command line tool
        // finds it too when building this project.
        if (!string.Equals(project.Local.CleanRomPath, cleanRomPath, StringComparison.OrdinalIgnoreCase))
        {
            project.Local.CleanRomPath = cleanRomPath;
            project.Save();
        }

        return new EditorSession(project, cleanRom, RomLayout.PilotwingsUsa);
    }

    /// <summary>
    /// Copies the open project into a new folder and opens the copy (see <see cref="HackProject.CopyTo"/>).
    /// </summary>
    /// <exception cref="ProjectException">The folder is not empty or lies inside the project.</exception>
    public EditorSession CopyTo(string folder, string name, bool prepareForGit) =>
        new(Project.CopyTo(folder, name, prepareForGit), CleanRom, Layout);

    /// <summary>
    /// Creates a project from a hack ROM (see <see cref="ProjectImporter"/>), builds it and compares the build with the ROM.
    /// </summary>
    /// <param name="import">What <see cref="ProjectImporter.Analyze"/> found in the ROM.</param>
    /// <param name="hackRom">The imported ROM.</param>
    /// <param name="folder">Project folder (new or empty).</param>
    /// <param name="name">Name of the project.</param>
    /// <param name="prepareForGit">Whether to add a .gitignore.</param>
    /// <param name="cleanRom">The verified clean ROM.</param>
    /// <param name="cleanRomPath">Where that ROM is stored.</param>
    /// <returns>The open project and the report: what was taken over and where the rebuilt ROM differs.</returns>
    /// <exception cref="ProjectException">The folder is not empty.</exception>
    public static (EditorSession Session, IReadOnlyList<ImportNote> Report) CreateFromImport(RomImport import, N64Rom hackRom,
        string folder, string name, bool prepareForGit, N64Rom cleanRom, string cleanRomPath)
    {
        HackProject project = ProjectImporter.CreateProject(import, folder, name, cleanRomPath, prepareForGit);
        var session = new EditorSession(project, cleanRom, RomLayout.PilotwingsUsa);
        var report = new List<ImportNote>(import.Notes);
        try
        {
            (ProjectBuildResult result, _, _) = session.Build(createRestorePoint: false);
            report.AddRange(ProjectImporter.Compare(result.RomBuild, hackRom.Data, session.Layout, import.Module));
        }
        catch (Exception ex) when (ex is ProjectException or FileSystemFullException or InvalidDataException or IOException)
        {
            report.Add(new ImportNote(ImportNoteKind.Warning, CoreText.F("The new project could not be built: {0}", ex.Message)));
        }

        return (session, report);
    }

    /// <summary>
    /// Builds the project and overwrites the hack ROM.
    /// </summary>
    /// <param name="createRestorePoint">Create a restore point after building.</param>
    /// <returns>The build result, the path of the hack ROM and the restore point (if created).</returns>
    public (ProjectBuildResult Result, string RomPath, BackupInfo? RestorePoint) Build(bool createRestorePoint)
    {
        EnsureBirdmanPad();
        ProjectBuildResult result = ProjectBuilder.Build(Project, CleanRom, Layout);
        string romPath = ProjectBuilder.WriteRom(Project, result);
        BackupInfo? restorePoint = createRestorePoint ? ProjectBackups.Create(Project) : null;
        return (result, romPath, restorePoint);
    }

    /// <summary>
    /// Applies code fixes to the project and builds the hack ROM right away. If the build fails,
    /// the project stays unchanged.
    /// </summary>
    /// <param name="fixes">The fixes to apply (already applied ones are skipped).</param>
    /// <returns>The build result and the path of the hack ROM.</returns>
    public (ProjectBuildResult Result, string RomPath) ApplyCodeFixes(IEnumerable<CodeFix> fixes)
    {
        List<string> before = [.. Project.Settings.AppliedCodeFixes];
        foreach (CodeFix fix in fixes)
        {
            Project.AddCodeFix(fix);
        }

        try
        {
            (ProjectBuildResult result, string romPath, _) = Build(createRestorePoint: false);
            Project.Save();
            return (result, romPath);
        }
        catch
        {
            Project.Settings.AppliedCodeFixes = before;
            throw;
        }
    }

    /// <summary>
    /// Applies or undoes a game patch and builds the hack ROM right away. If the build fails, the
    /// project stays unchanged.
    /// </summary>
    /// <param name="patch">The game patch.</param>
    /// <param name="apply">True to apply it, false to undo it.</param>
    /// <returns>The build result and the path of the hack ROM.</returns>
    public (ProjectBuildResult Result, string RomPath) SetGamePatch(GamePatch patch, bool apply)
    {
        List<string> before = [.. Project.Settings.AppliedPatches];
        if (apply && patch.Id == GamePatches.BonusGames.Id && !CanAddTexts)
        {
            throw new ProjectException(CoreText.F("The game patch \"{0}\" adds texts and needs the code fix \"{1}\". Apply it first.",
                CoreText.T(patch.Name), CoreText.T(CodeFixes.ExpandedTexts.Name)));
        }

        Project.Settings.AppliedPatches = GamePatches.AllIds
            .Where(id => id == patch.Id ? apply : before.Contains(id))
            .ToList();

        try
        {
            (ProjectBuildResult result, string romPath, _) = Build(createRestorePoint: false);
            Project.Save();
            return (result, romPath);
        }
        catch
        {
            Project.Settings.AppliedPatches = before;
            throw;
        }
    }

    /// <summary>
    /// Builds the project fresh and saves a verified BPS patch.
    /// </summary>
    /// <returns>Size of the patch in bytes.</returns>
    public int ExportPatch(string patchPath)
    {
        EnsureBirdmanPad();
        ProjectBuildResult result = ProjectBuilder.Build(Project, CleanRom, Layout);
        byte[] patch = ProjectBuilder.CreatePatch(Project, CleanRom, result.RomBuild.Rom);
        File.WriteAllBytes(patchPath, patch);
        return patch.Length;
    }

    /// <summary>Compares the project's replacement files with the originals.</summary>
    public IReadOnlyList<OverrideStatus> GetStatus() => ProjectStatus.Compute(Project, CleanFileSystem);

    /// <summary>
    /// Copies an original game file into the project for editing.
    /// </summary>
    /// <returns>Path of the new file in the project.</returns>
    /// <exception cref="ProjectException">Unknown index, or the file is already in the project.</exception>
    public string AddFileToProject(int tableIndex)
    {
        GameFile file = CleanFileSystem.Files.FirstOrDefault(f => f.TableIndex == tableIndex)
            ?? throw new ProjectException(CoreText.F("The game has no file with table index {0}.", tableIndex));
        return Project.AddOverride(file);
    }

    /// <summary>Removes a file from the project, so the original is used again.</summary>
    public bool RemoveFileFromProject(int tableIndex) => Project.RemoveOverride(tableIndex);

    /// <summary>
    /// Returns the current version of a game file: the project's replacement if there is one,
    /// otherwise the original from the clean ROM.
    /// </summary>
    /// <param name="fromProject">True if the project's replacement was used.</param>
    /// <exception cref="ProjectException">Unknown index.</exception>
    public byte[] GetCurrentFileData(int tableIndex, out bool fromProject)
    {
        FileOverride? replacement = Project.GetOverrides().FirstOrDefault(o => o.TableIndex == tableIndex);
        if (replacement is not null)
        {
            fromProject = true;
            return File.ReadAllBytes(replacement.Path);
        }

        fromProject = false;
        GameFile original = CleanFileSystem.Files.FirstOrDefault(f => f.TableIndex == tableIndex)
            ?? throw new ProjectException(CoreText.F("The game has no file with table index {0}.", tableIndex));
        return original.Data;
    }

    /// <summary>
    /// Loads all game texts in their current version (project copy of the text file if the
    /// project has one, otherwise the original). With the game patch <see cref="GamePatches.BonusGames"/> the texts of
    /// the levels are added if the file lacks them (<see cref="ResultTables.AddLevelTexts"/>).
    /// </summary>
    /// <exception cref="InvalidDataException">The text file or the font is damaged.</exception>
    public GameTextLibrary LoadTexts()
    {
        TextFont font = TextFont.Load(CleanFileSystem);
        GameFile textFile = GameTextFile.FindFile(CleanFileSystem);
        byte[] data = GetCurrentFileData(textFile.TableIndex, out bool fromProject);

        // The game patch "Bonus games and Birdman" needs texts of its own for the levels, and classes with own medal
        // limits get own medal texts: until a text is saved they exist only here (and in the build, which adds them the
        // same way).
        if (ProjectBuilder.AddGeneratedTexts(Project.HasPatch(GamePatches.BonusGames), ProjectBuilder.HasOwnClassTexts(Project),
                Project.Settings.Missions, Project.Settings.Classes, data, font, MissionIdsOrNull()) is { } withTexts)
        {
            data = withTexts;
        }

        return GameTextLibrary.Create(font, data, fromProject, OriginalTextCount);
    }

    /// <summary>Markup to the data of a text (for the texts the editor writes itself).</summary>
    /// <exception cref="InvalidDataException">The markup has errors.</exception>
    internal static Func<string, byte[]> EncodeFor(TextFont font)
    {
        var codec = new TextCodec(font);
        return markup =>
        {
            TextEncodeResult result = codec.Encode(markup);
            return result.Success
                ? TextCodec.ToChunkData(result.Codes, 0)
                : throw new InvalidDataException(result.Errors[0].Message);
        };
    }

    /// <summary>Loads the texts of the original game (ignoring the project), for comparison.</summary>
    public GameTextLibrary LoadOriginalTexts()
    {
        TextFont font = TextFont.Load(CleanFileSystem);
        return GameTextLibrary.Create(font, GameTextFile.FindFile(CleanFileSystem).Data, fromProject: false);
    }

    /// <summary>Number of texts in the original game (439 in the US version).</summary>
    public int OriginalTextCount => _originalTextCount ??= GameTextFile.Parse(GameTextFile.FindFile(CleanFileSystem).Data).Entries.Count;

    private int? _originalTextCount;

    /// <summary>True if the project may contain more texts than the original game (code fix applied).</summary>
    public bool CanAddTexts => Project.Settings.AppliedCodeFixes.Contains(CodeFixes.ExpandedTexts.Id);

    /// <summary>
    /// Saves edited texts into the project's copy of the text file.
    /// </summary>
    /// <param name="library">The texts the edits are based on (from <see cref="LoadTexts"/>).</param>
    /// <param name="changedMarkup">New markup by text index.</param>
    /// <returns>Path of the written text file.</returns>
    /// <exception cref="ProjectException">A text has errors and cannot be encoded.</exception>
    public string SaveTexts(GameTextLibrary library, IReadOnlyDictionary<int, string> changedMarkup) =>
        SaveTexts(library, changedMarkup, [], []);

    /// <summary>
    /// Saves edited, added and removed texts into the project's copy of the text file.
    /// </summary>
    /// <param name="library">The texts the edits are based on (from <see cref="LoadTexts"/>).</param>
    /// <param name="changedMarkup">New markup of existing texts, by text index.</param>
    /// <param name="added">New custom texts. Their numbers must be free (see <see cref="CustomTexts.NextFreeIndex"/>).</param>
    /// <param name="removed">Numbers of custom texts to remove; their slots become free.</param>
    /// <returns>Path of the written text file.</returns>
    /// <exception cref="ProjectException">A text has errors, a rule for custom texts is broken, or the
    /// project lacks the code fix needed for more texts.</exception>
    public string SaveTexts(GameTextLibrary library, IReadOnlyDictionary<int, string> changedMarkup,
        IReadOnlyList<NewText> added, IReadOnlyCollection<int> removed)
    {
        List<TextFileEntry> texts = library.File.ToEntries();
        int originalCount = OriginalTextCount;

        foreach ((int index, string markup) in changedMarkup)
        {
            if (index < 0 || index >= texts.Count || texts[index].IsFree)
            {
                throw new ProjectException(CoreText.F("There is no text number {0}.", index));
            }

            texts[index] = texts[index] with { Data = EncodeForSaving(library, markup, texts[index].Name, library.Texts[index].Entry.Data.Length) };
        }

        foreach (int index in removed)
        {
            if (index < originalCount)
            {
                throw new ProjectException(CoreText.F("Text {0} belongs to the original game and cannot be removed.",
                    index));
            }

            if (index < texts.Count)
            {
                texts[index] = TextFileEntry.FreeSlot;
            }
        }

        foreach (NewText text in added.OrderBy(t => t.Index))
        {
            if (text.Index < originalCount)
            {
                throw new ProjectException(CoreText.F("New texts must come after the {0} original texts (number {1}).",
                    originalCount, text.Index));
            }

            string? nameProblem = CustomTexts.CheckName(text.Name, texts.Where(t => !t.IsFree).Select(t => t.Name));
            if (nameProblem is not null)
            {
                throw new ProjectException(CoreText.F("Text {0}: {1}", text.Index, nameProblem));
            }

            while (texts.Count <= text.Index)
            {
                texts.Add(TextFileEntry.FreeSlot);
            }

            if (!texts[text.Index].IsFree)
            {
                throw new ProjectException(CoreText.F("Text number {0} is already used by {1}.",
                    text.Index, texts[text.Index].Name));
            }

            texts[text.Index] = new TextFileEntry(text.Name, EncodeForSaving(library, text.Markup, text.Name, 0));
        }

        // Free slots at the end are not needed: no text behind them has to keep its number.
        while (texts.Count > originalCount && texts[^1].IsFree)
        {
            texts.RemoveAt(texts.Count - 1);
        }

        if (CustomTexts.CountProblem(texts.Count, originalCount, CanAddTexts) is { } problem)
        {
            throw new ProjectException(problem);
        }

        GameFile original = GameTextFile.FindFile(CleanFileSystem);
        return Project.WriteOverride(original, library.File.Build(texts));
    }

    private static byte[] EncodeForSaving(GameTextLibrary library, string markup, string name, int minimumSize)
    {
        TextEncodeResult result = library.Codec.Encode(markup);
        if (!result.Success)
        {
            throw new ProjectException(CoreText.F("Text {0} has errors: {1}", name, result.Errors[0].Message));
        }

        return TextCodec.ToChunkData(result.Codes, minimumSize);
    }

    // ----------------------------------------------------------------- music

    private InstrumentBank? _instrumentBank;
    private SequenceBank? _sequenceBank;

    /// <summary>The game's instrument bank (read once).</summary>
    /// <exception cref="InvalidDataException">The bank cannot be read.</exception>
    public InstrumentBank InstrumentBank => _instrumentBank ??= ReadInstrumentBank();

    private SequenceBank OriginalSequences => _sequenceBank ??= SequenceBank.Parse(
        CleanRom.Data.AsSpan(AudioLayout.SequenceOffset, AudioLayout.SequenceSize));

    private AudioLayout AudioLayout => Layout.Audio ?? throw new ProjectException(CoreText.T("This game version has no known music data."));

    /// <summary>All songs with their channels and the project's changes.</summary>
    /// <exception cref="InvalidDataException">An imported song of the project is damaged.</exception>
    public IReadOnlyList<SongInfo> LoadSongs()
    {
        var songs = new List<SongInfo>();
        for (int i = 0; i < OriginalSequences.Sequences.Count; i++)
        {
            IReadOnlyList<SequenceChannel> channels = CompactSequence.Parse(SongBase(i)).GetChannels();
            SongSettings settings = Project.Settings.Music.FirstOrDefault(m => m.Song == i) ?? new SongSettings { Song = i };
            string name = i < MusicCatalog.SongNames.Count ? MusicCatalog.SongNames[i] : CoreText.F("Song {0}", i);
            songs.Add(new SongInfo(i, name, channels, settings) { IsImported = Project.HasImportedSong(i) });
        }

        return songs;
    }

    /// <summary>
    /// Largest size of a song: the longest original song. The game reserves a buffer of exactly
    /// this size in its audio memory when it starts; larger songs would need more of that memory.
    /// </summary>
    public int MaxSongSize => OriginalSequences.Sequences.Max(s => s.Length);

    /// <summary>A song as MIDI file: the imported or original song with the project's changes.</summary>
    /// <param name="song">The song number.</param>
    /// <param name="trackName">Optional name of each channel's track, by channel.</param>
    /// <exception cref="ProjectException">The song cannot be built.</exception>
    public byte[] ExportSongAsMidi(int song, Func<int, string?>? trackName = null)
    {
        SongSettings settings = Project.Settings.Music.FirstOrDefault(m => m.Song == song) ?? new SongSettings { Song = song };
        try
        {
            return StandardMidi.Export(CompactSequence.Parse(SequenceEditor.Apply(SongBase(song), settings)), trackName);
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Replaces a song with a MIDI file. The song is stored in the project (files/music); the
    /// song's instrument changes and silenced voices are removed, because its voices are new.
    /// </summary>
    /// <returns>What was changed or left out during the conversion.</returns>
    /// <exception cref="ProjectException">The file cannot be used.</exception>
    public IReadOnlyList<string> ImportSongFromMidi(int song, byte[] midi)
    {
        if (song < 0 || song >= OriginalSequences.Sequences.Count)
        {
            throw new ProjectException(CoreText.F("The game has no song {0}.", song));
        }

        MidiImportResult result;
        try
        {
            result = StandardMidi.Import(midi, InstrumentBank.Instruments.ContainsKey, DefaultProgram, MaxSongSize);
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(ex.Message, ex);
        }

        Project.WriteImportedSong(song, result.Sequence);
        if (Project.Settings.Music.RemoveAll(m => m.Song == song) > 0)
        {
            Project.Save();
        }

        return result.Warnings;
    }

    /// <summary>The song the project's changes apply to: an imported one, or the original.</summary>
    private byte[] SongBase(int song) => Project.ReadImportedSong(song) ?? OriginalSequences.Sequences[song];

    /// <summary>The instrument a channel has before the song selects one.</summary>
    public int DefaultProgram(int channel) =>
        CompactSequence.DefaultProgram(channel, InstrumentBank.PercussionProgram, InstrumentBank.FirstProgram);

    /// <summary>
    /// Stores the changes of a song in the project (project.json). They are checked first: every
    /// instrument must exist, and the song must be changeable that way.
    /// </summary>
    /// <exception cref="ProjectException">A change is not possible.</exception>
    public void SaveSong(SongSettings settings)
    {
        if (settings.Song < 0 || settings.Song >= OriginalSequences.Sequences.Count)
        {
            throw new ProjectException(CoreText.F("The game has no song {0}.", settings.Song));
        }

        foreach (InstrumentChange change in settings.Instruments)
        {
            if (!InstrumentBank.Instruments.ContainsKey(change.Instrument))
            {
                throw new ProjectException(CoreText.F("Instrument {0} does not exist.", change.Instrument));
            }
        }

        // Changes back to the original instrument are no changes.
        settings.Instruments.RemoveAll(c => c.Instrument == (c.Original >= 0 ? c.Original : DefaultProgram(c.Channel)));
        try
        {
            byte[] changed = SequenceEditor.Apply(SongBase(settings.Song), settings);
            if (changed.Length > MaxSongSize)
            {
                throw new ProjectException(CoreText.F("Song {0:D2} needs {1:N0} bytes, but the game has room for at most {2:N0}.",
                    settings.Song, changed.Length, MaxSongSize));
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(ex.Message, ex);
        }

        Project.Settings.Music.RemoveAll(m => m.Song == settings.Song);
        if (!settings.IsEmpty)
        {
            Project.Settings.Music.Add(settings);
            Project.Settings.Music.Sort((a, b) => a.Song.CompareTo(b.Song));
        }

        Project.Save();
    }

    /// <summary>Removes all changes of a song, also an imported version.</summary>
    /// <returns>False if the song had no changes.</returns>
    public bool RestoreSong(int song)
    {
        bool imported = Project.RemoveImportedSong(song);
        if (Project.Settings.Music.RemoveAll(m => m.Song == song) == 0)
        {
            return imported;
        }

        Project.Save();
        return true;
    }

    private InstrumentBank ReadInstrumentBank()
    {
        AudioLayout audio = AudioLayout;
        return Audio.InstrumentBank.Parse(
            CleanRom.Data.AsSpan(audio.BankOffset, audio.TableOffset - audio.BankOffset).ToArray(),
            CleanRom.Data.AsSpan(audio.TableOffset, audio.EndOffset - audio.TableOffset).ToArray());
    }

    // ----------------------------------------------------------------- textures

    /// <summary>
    /// Loads all textures of the game, each with its current version in the project. Textures
    /// whose original cannot be read are left out (does not happen with the supported ROM).
    /// </summary>
    public IReadOnlyList<TextureEntry> LoadTextures()
    {
        Dictionary<int, FileOverride> overrides = Project.GetOverrides().ToDictionary(o => o.TableIndex);
        var textures = new List<TextureEntry>();
        foreach (GameFile file in TextureFiles)
        {
            if (LoadTexture(file, overrides.GetValueOrDefault(file.TableIndex)) is { } texture)
            {
                textures.Add(texture);
            }
        }

        return textures;
    }

    /// <summary>Loads one texture with its current version in the project (null if its original cannot be read).</summary>
    /// <exception cref="ProjectException">Unknown number.</exception>
    public TextureEntry? LoadTexture(int number)
    {
        GameFile file = FindTextureFile(number);
        return LoadTexture(file, Project.GetOverrides().FirstOrDefault(o => o.TableIndex == file.TableIndex));
    }

    /// <summary>
    /// Replaces a texture's image. The image is converted to the texture's format, and the smaller
    /// copies (mipmaps) are computed from it. An image that looks like the original (see
    /// <see cref="RgbaImage.LooksLike"/>) removes the project's replacement, so the file stays
    /// exactly the original.
    /// </summary>
    /// <param name="number">The texture number.</param>
    /// <param name="image">The new image; it must have the texture's size.</param>
    /// <exception cref="ProjectException">Unknown number, wrong size, or the texture cannot be edited.</exception>
    public TextureSaveResult SaveTexture(int number, RgbaImage image)
    {
        GameFile file = FindTextureFile(number);
        TextureEntry texture = LoadTexture(number)
            ?? throw new ProjectException(CoreText.T("The editor cannot read the format of this texture."));
        if (texture.Problem is not null)
        {
            throw new ProjectException(texture.Problem);
        }

        GameTexture original = texture.Original;
        if (image.Width != original.Width || image.Height != original.Height)
        {
            throw new ProjectException(CoreText.F("Wrong size: {0} × {1} instead of {2} × {3}",
                image.Width, image.Height, original.Width, original.Height));
        }

        RgbaImage stored = original.Quantize(image);
        if (stored.LooksLike(original.DecodeImage()))
        {
            return texture.IsChanged && Project.RemoveOverride(file.TableIndex) ? TextureSaveResult.RestoredOriginal : TextureSaveResult.Unchanged;
        }

        if (texture.IsChanged && stored.LooksLike(texture.Current.DecodeImage()))
        {
            return TextureSaveResult.Unchanged;
        }

        byte[] data = TextureFile.Parse(file.Data).Build(original.WithImage(image));
        Project.WriteOverride(file, data);
        return TextureSaveResult.Changed;
    }

    /// <summary>Removes the project's replacement of a texture, so the original is used again.</summary>
    /// <returns>False if the project did not replace the texture.</returns>
    /// <exception cref="ProjectException">Unknown number.</exception>
    public bool RestoreTexture(int number) => Project.RemoveOverride(FindTextureFile(number).TableIndex);

    private IEnumerable<GameFile> TextureFiles => CleanFileSystem.Files.Where(f => f.FileType == TextureFile.FormType);

    private GameFile FindTextureFile(int number) =>
        TextureFiles.FirstOrDefault(f => f.GroupIndex == number)
        ?? throw new ProjectException(CoreText.F("There is no texture with the number {0}", number));

    private static TextureEntry? LoadTexture(GameFile file, FileOverride? replacement)
    {
        GameTexture original;
        try
        {
            original = TextureFile.Parse(file.Data).Texture;
        }
        catch (InvalidDataException)
        {
            return null;
        }

        if (!original.IsSupported)
        {
            return new TextureEntry(file.GroupIndex, file.TableIndex, original, original, false,
                CoreText.T("The editor cannot read the format of this texture."));
        }

        if (replacement is null)
        {
            return new TextureEntry(file.GroupIndex, file.TableIndex, original, original, false, null);
        }

        try
        {
            byte[] data = File.ReadAllBytes(replacement.Path);
            if (data.AsSpan().SequenceEqual(file.Data))
            {
                return new TextureEntry(file.GroupIndex, file.TableIndex, original, original, false, null);
            }

            GameTexture current = TextureFile.Parse(data).Texture;
            string? problem = current.Format != original.Format || current.Width != original.Width || current.Height != original.Height
                ? CoreText.T("The project's file of this texture has another size or format than the original. Restore the original to edit it here.")
                : null;
            return new TextureEntry(file.GroupIndex, file.TableIndex, original, current, true, problem);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new TextureEntry(file.GroupIndex, file.TableIndex, original, original, true,
                CoreText.F("The project's file of this texture cannot be read: {0}", ex.Message));
        }
    }

    // ----------------------------------------------------------------- missions

    /// <summary>
    /// Reads all missions (their current version in the project) with their texts and point rows.
    /// The tables of the game code come from the clean ROM, because the editor does not change
    /// them yet.
    /// </summary>
    public MissionCatalogResult LoadMissions()
    {
        var sources = new List<MissionSource>();
        var problems = new List<string>();
        try
        {
            foreach (ProjectMission mission in CurrentMissions())
            {
                sources.Add(new MissionSource(mission.File.TableIndex, mission.File.GroupIndex, mission.File.Data,
                    mission.Added || HasOverride(mission.File.TableIndex)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectException)
        {
            problems.Add(ex.Message);
        }

        MissionTables? tables = Layout.Missions is { } layout ? new MissionTables(CleanRom.Data, layout, Project.HasPatch(GamePatches.BonusGames),
            ResultTables.BirdmanPointMissions(Project.Settings.Missions)) : null;
        MissionCatalogResult result = MissionCatalog.Build(sources, tables, Project.Settings.Missions);
        return problems.Count == 0 ? result : result with { Problems = [.. problems, .. result.Problems] };
    }

    /// <summary>
    /// Saves the changes of a mission: its COMM block into the project's copy of the mission file
    /// (removed again if it equals the original) and its settings into project.json.
    /// </summary>
    /// <param name="mission">The mission as loaded (<see cref="LoadMissions"/>).</param>
    /// <param name="comm">The changed COMM block.</param>
    /// <param name="settings">The settings (result rows, generated texts), or null for none.</param>
    /// <exception cref="ProjectException">The mission file cannot be found.</exception>
    public void SaveMission(MissionInfo mission, MissionComm comm, MissionSettings? settings)
    {
        GameFile original = CleanFileSystem.Files.FirstOrDefault(f => f.TableIndex == mission.TableIndex)
            ?? AddedMissionFile(mission)
            ?? throw new ProjectException(CoreText.F("The game has no file with table index {0}.", mission.TableIndex));
        byte[] data = mission.File.WithComm(comm);

        // Until the level editor: the first Birdman mission with points gets the landing pad of Holiday Island (as the Hang
        // Glider's first mission has it), so a good landing is possible there.
        if (settings?.BirdmanPoints == true && mission.Id == BirdmanPadMission && Project.HasPatch(GamePatches.BonusGames)
            && MissionFile.Parse(data).Comm.GetObjectCount(MissionObjectKind.LandingPad) == 0 && HangGliderPad() is { } pad)
        {
            data = MissionFile.Parse(data).WithLandingPad(pad);
        }

        if (original.TableIndex < CleanFileSystem.Files.Count && data.AsSpan().SequenceEqual(original.Data))
        {
            Project.RemoveOverride(original.TableIndex);
        }
        else
        {
            Project.WriteOverride(original, data);
        }

        Project.Settings.Missions.RemoveAll(m => m.Id == mission.Id);
        if (settings is { IsEmpty: false })
        {
            Project.Settings.Missions.Add(settings.Clone());
            Project.Settings.Missions.Sort((a, b) => (a.Vehicle, a.Class, a.Test).CompareTo((b.Vehicle, b.Class, b.Test)));
        }

        Project.Save();
    }

    /// <summary>
    /// Gives <see cref="BirdmanPadMission"/> its landing pad if it has points but no pad yet (for example when the points
    /// were switched on before the editor added the pad on saving). Writes the project's mission file only then.
    /// </summary>
    private void EnsureBirdmanPad()
    {
        if (Project.Settings.Missions.FirstOrDefault(m => m.Id == BirdmanPadMission) is not { BirdmanPoints: true } settings
            || !Project.HasPatch(GamePatches.BonusGames))
        {
            return;
        }

        if (LoadMissions().Missions.FirstOrDefault(m => m.Id == BirdmanPadMission) is { } mission
            && mission.File.Comm.GetObjectCount(MissionObjectKind.LandingPad) == 0)
        {
            SaveMission(mission, mission.File.Comm, settings);
        }
    }

    /// <summary>The classes of every vehicle with their medal limits and islands (Classes tab).</summary>
    public IReadOnlyList<Classes.ClassInfo> LoadClasses()
    {
        IReadOnlyList<MissionInfo> missions = LoadMissions().Missions;
        MissionTables? tables = Layout.Missions is { } layout ? new MissionTables(CleanRom.Data, layout) : null;
        bool bonusPatch = Project.HasPatch(GamePatches.BonusGames);
        IReadOnlySet<TestId> birdman = bonusPatch ? ResultTables.BirdmanPointMissions(Project.Settings.Missions) : new HashSet<TestId>();
        bool ownTexts = ProjectBuilder.HasOwnClassTexts(Project);
        var result = new List<Classes.ClassInfo>();
        foreach ((Vehicle vehicle, int classIndex) in Classes.ClassLimits.All)
        {
            Classes.ClassSettings? settings = Project.Settings.Classes.FirstOrDefault(c => c.VehicleId == vehicle && c.Class == classIndex);
            int tests = missions.Count(m => m.Id.Vehicle == vehicle && m.Id.Class == classIndex);
            int pointMissions = vehicle == Vehicle.Birdman ? birdman.Count(m => m.Class == classIndex) : 0;
            int? originalIsland = tables?.ClassIsland(vehicle, classIndex);
            result.Add(new Classes.ClassInfo(vehicle, classIndex, settings?.Clone(),
                Classes.ClassLimits.Original(vehicle, classIndex, bonusPatch),
                Classes.ClassLimits.Effective(null, vehicle, classIndex, bonusPatch, pointMissions),
                Classes.ClassLimits.Effective(settings, vehicle, classIndex, bonusPatch, pointMissions),
                Classes.ClassLimits.Perfect(vehicle, tests, pointMissions),
                tests, pointMissions, originalIsland, settings?.Island ?? originalIsland,
                ownTexts && Classes.ClassTexts.HasOwnTexts(vehicle)));
        }

        return result;
    }

    /// <summary>Saves the settings of a class (removed from the project when nothing is changed).</summary>
    /// <exception cref="ArgumentException">The limits cannot be used.</exception>
    public void SaveClass(Classes.ClassSettings settings)
    {
        Project.Settings.Classes.RemoveAll(c => c.Vehicle == settings.Vehicle && c.Class == settings.Class);
        if (!settings.IsEmpty)
        {
            Project.Settings.Classes.Add(settings.Clone());
            Project.Settings.Classes.Sort((a, b) => (a.Vehicle, a.Class).CompareTo((b.Vehicle, b.Class)));
        }

        Project.Save();
    }

    /// <summary>The unlock conditions of the menu fields that count (the project's, or the game's).</summary>
    public List<Classes.FieldUnlock> LoadUnlocks() => Classes.MenuUnlocks.Effective(Project.Settings.Unlocks);

    /// <summary>
    /// Saves the unlock conditions of the menu fields; rules equal to the game's are stored as none (null).
    /// </summary>
    /// <exception cref="ArgumentException">The rules cannot be used (<see cref="Classes.MenuUnlocks.Problem"/>).</exception>
    public void SaveUnlocks(IReadOnlyList<Classes.FieldUnlock>? rules)
    {
        if (rules is not null && Classes.MenuUnlocks.Problem(rules, CurrentSaveLayout()) is { } problem)
        {
            throw new ArgumentException(problem, nameof(rules));
        }

        Project.Settings.Unlocks = rules is null || Classes.MenuUnlocks.IsOriginal(rules) ? null : [.. rules.Select(r => r.Clone())];
        Project.Save();
    }

    /// <summary>The Birdman mission that gets a landing pad when it has points (stage 1, mission 1, Holiday Island).</summary>
    public static readonly TestId BirdmanPadMission = new(0, Vehicle.Birdman, 0);

    /// <summary>The landing pad of the Hang Glider's Beginner mission (Holiday Island, the island's only pad), or null.</summary>
    private byte[]? HangGliderPad()
    {
        foreach (GameFile file in CleanFileSystem.Files.Where(f => f.FileType == MissionFile.FormType))
        {
            MissionFile mission = MissionFile.Parse(file.Data);
            if (mission.Comm.Id == new TestId(0, Vehicle.HangGlider, 0) && mission.GetObjectData(MissionObjectKind.LandingPad) is { Length: >= MissionFile.LandingPadSize } pads)
            {
                return pads[..MissionFile.LandingPadSize].ToArray();
            }
        }

        return null;
    }

    /// <summary>The COMM block of a mission in the original game.</summary>
    /// <exception cref="ProjectException">The mission file cannot be found.</exception>
    public MissionComm OriginalMissionComm(MissionInfo mission)
    {
        GameFile original = CleanFileSystem.Files.FirstOrDefault(f => f.TableIndex == mission.TableIndex)
            ?? throw new ProjectException(CoreText.F("The game has no file with table index {0}.", mission.TableIndex));
        return MissionFile.Parse(original.Data).Comm;
    }

    /// <summary>The medal limits of the game (from the clean ROM), or empty if unknown.</summary>
    public IReadOnlyList<MedalLimits> LoadMedalLimits() =>
        Layout.Missions is { } layout ? new MissionTables(CleanRom.Data, layout, Project.HasPatch(GamePatches.BonusGames)).MedalLimits() : [];

    // ----------------------------------------------------------------- features

    /// <summary>Saves which additional features are built into the game (Features tab).</summary>
    public void SaveFeatures(GameFeatureSettings features)
    {
        Project.Settings.Features = features.Clone();
        Project.Save();
    }

    /// <summary>Lists the restore points, newest first.</summary>
    public IReadOnlyList<BackupInfo> GetRestorePoints() => ProjectBackups.List(Project);

    /// <summary>Creates a restore point of the current state.</summary>
    public BackupInfo CreateRestorePoint() => ProjectBackups.Create(Project);

    /// <summary>Returns the project to a restore point. The current state is replaced.</summary>
    public void RestoreTo(string restorePointName) => Project = ProjectBackups.Restore(Project, restorePointName);

    /// <summary>Deletes a restore point.</summary>
    public void DeleteRestorePoint(string restorePointName) => ProjectBackups.Delete(Project, restorePointName);
}
