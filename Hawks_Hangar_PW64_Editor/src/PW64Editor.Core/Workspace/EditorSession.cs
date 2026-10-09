using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Localization;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
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
public sealed class EditorSession
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
    /// Builds the project and overwrites the hack ROM.
    /// </summary>
    /// <param name="createRestorePoint">Create a restore point after building.</param>
    /// <returns>The build result, the path of the hack ROM and the restore point (if created).</returns>
    public (ProjectBuildResult Result, string RomPath, BackupInfo? RestorePoint) Build(bool createRestorePoint)
    {
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
    /// Builds the project fresh and saves a verified BPS patch.
    /// </summary>
    /// <returns>Size of the patch in bytes.</returns>
    public int ExportPatch(string patchPath)
    {
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
    /// project has one, otherwise the original).
    /// </summary>
    /// <exception cref="InvalidDataException">The text file or the font is damaged.</exception>
    public GameTextLibrary LoadTexts()
    {
        TextFont font = TextFont.Load(CleanFileSystem);
        GameFile textFile = GameTextFile.FindFile(CleanFileSystem);
        byte[] data = GetCurrentFileData(textFile.TableIndex, out bool fromProject);
        return GameTextLibrary.Create(font, data, fromProject, OriginalTextCount);
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

    /// <summary>Lists the restore points, newest first.</summary>
    public IReadOnlyList<BackupInfo> GetRestorePoints() => ProjectBackups.List(Project);

    /// <summary>Creates a restore point of the current state.</summary>
    public BackupInfo CreateRestorePoint() => ProjectBackups.Create(Project);

    /// <summary>Returns the project to a restore point. The current state is replaced.</summary>
    public void RestoreTo(string restorePointName) => Project = ProjectBackups.Restore(Project, restorePointName);

    /// <summary>Deletes a restore point.</summary>
    public void DeleteRestorePoint(string restorePointName) => ProjectBackups.Delete(Project, restorePointName);
}
