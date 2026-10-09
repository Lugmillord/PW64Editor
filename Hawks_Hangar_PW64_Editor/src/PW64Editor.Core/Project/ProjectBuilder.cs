using PW64Editor.Core.Build;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Localization;
using PW64Editor.Core.Patching;
using PW64Editor.Core.Rom;

namespace PW64Editor.Core.Project;

/// <summary>
/// Builds a hack project (clean ROM + replacement files = hack ROM) and creates BPS patches.
/// </summary>
/// <remarks>
/// Building and patching are separate on purpose: saving in the editor rebuilds and overwrites
/// the hack ROM, while a patch is only created when the user asks for one.
/// </remarks>
public static class ProjectBuilder
{
    /// <summary>
    /// Builds the project in memory. Nothing is written to disk; use <see cref="WriteRom"/>.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="cleanRom">The clean base ROM. Must match the project's SHA-1.</param>
    /// <param name="layout">The fixed addresses of the game release.</param>
    /// <exception cref="ProjectException">Wrong base ROM, or an invalid replacement file.</exception>
    /// <exception cref="FileSystemFullException">The files do not fit.</exception>
    public static ProjectBuildResult Build(HackProject project, N64Rom cleanRom, RomLayout layout)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(cleanRom);
        ArgumentNullException.ThrowIfNull(layout);

        string sha1 = cleanRom.ComputeSha1();
        if (!string.Equals(sha1, project.Settings.BaseRomSha1, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectException(
                CoreText.F("The ROM does not match this project (SHA-1 {0}, expected {1}).",
                    sha1, project.Settings.BaseRomSha1));
        }

        GameFileSystem fileSystem = GameFileSystem.Read(cleanRom, layout);
        List<GameFile> files = fileSystem.Files.ToList();
        IReadOnlyList<FileOverride> overrides = project.GetOverrides();

        foreach (FileOverride replacement in overrides)
        {
            int position = files.FindIndex(f => f.TableIndex == replacement.TableIndex);
            string name = Path.GetFileName(replacement.Path);
            if (position < 0)
            {
                throw new ProjectException(CoreText.F("'{0}': the game has no file with table index {1}.",
                    name, replacement.TableIndex));
            }

            files[position] = ApplyOverride(files[position], replacement, name);
        }

        CheckTextFile(project, fileSystem, files);

        RomBuildResult romBuild;
        try
        {
            romBuild = RomBuilder.Build(cleanRom, files, layout, project.BuildOptions);
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(CoreText.F("Build failed: {0}", ex.Message), ex);
        }

        return new ProjectBuildResult(romBuild, overrides.Count);
    }

    /// <summary>
    /// Writes the built ROM to <see cref="HackProject.OutputRomPath"/>, overwriting the previous one.
    /// </summary>
    /// <returns>The path written to.</returns>
    /// <exception cref="ProjectException">The output path is the clean ROM.</exception>
    public static string WriteRom(HackProject project, ProjectBuildResult result)
    {
        string path = Path.GetFullPath(project.OutputRomPath);

        // The one thing that must never happen: overwriting the clean ROM. Every future build
        // and every patch depends on it. (SetOutputRomPath checks this too; this is the last line
        // of defense, e.g. if project.user.json was edited by hand.)
        if (project.IsCleanRomPath(path))
        {
            throw new ProjectException(CoreText.T("The output ROM path points to the clean ROM. Choose a different " +
                "output path."));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        result.RomBuild.Rom.Save(path);
        return path;
    }

    /// <summary>
    /// Creates a BPS patch from the clean ROM to a built hack ROM and verifies it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The patch does not reproduce the ROM (a bug).</exception>
    public static byte[] CreatePatch(HackProject project, N64Rom cleanRom, N64Rom builtRom)
    {
        byte[] patch = BpsWriter.Create(cleanRom.Data, builtRom.Data, CreatePatchMetadata(project));

        // Self-check, so a broken patch can never leave the editor.
        byte[] check = BpsReader.Apply(cleanRom.Data, patch);
        if (!check.AsSpan().SequenceEqual(builtRom.Data))
        {
            throw new InvalidOperationException("Internal error: the created patch does not reproduce the built ROM.");
        }

        return patch;
    }

    /// <summary>
    /// A text file with more texts than the game's tables hold would crash the game: refuse to build it.
    /// </summary>
    private static void CheckTextFile(HackProject project, GameFileSystem cleanFileSystem, List<GameFile> files)
    {
        GameFile originalText;
        try
        {
            originalText = Text.GameTextFile.FindFile(cleanFileSystem);
        }
        catch (InvalidDataException)
        {
            return; // not the retail layout (test data): nothing to check
        }

        GameFile current = files.First(f => f.TableIndex == originalText.TableIndex);
        if (ReferenceEquals(current.Data, originalText.Data))
        {
            return;
        }

        int count, originalCount;
        try
        {
            count = Text.GameTextFile.Parse(current.Data).Entries.Count;
            originalCount = Text.GameTextFile.Parse(originalText.Data).Entries.Count;
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(CoreText.F("The project's text file is damaged: {0}", ex.Message), ex);
        }

        if (Text.CustomTexts.CountProblem(count, originalCount,
                project.Settings.AppliedCodeFixes.Contains(Code.CodeFixes.ExpandedTexts.Id)) is { } problem)
        {
            throw new ProjectException(problem);
        }
    }

    private static GameFile ApplyOverride(GameFile original, FileOverride replacement, string name)
    {
        if (!string.Equals(replacement.FileType, original.FileType, StringComparison.Ordinal))
        {
            throw new ProjectException(
                CoreText.F("'{0}': the name says type '{1}', but file {2} is '{3}'.",
                    name, replacement.FileType, original.TableIndex, original.FileType));
        }

        byte[] data = File.ReadAllBytes(replacement.Path);

        string formType;
        try
        {
            formType = IffForm.ReadHeader(data).FormType;
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(CoreText.F("'{0}' is not a valid game file: {1}", name, ex.Message), ex);
        }

        if (formType != original.FileType)
        {
            throw new ProjectException(
                CoreText.F("'{0}': its content is a '{1}' file, but file {2} must be '{3}'.",
                    name, formType, original.TableIndex, original.FileType));
        }

        return original with { Data = data };
    }

    /// <summary>
    /// Text stored inside the BPS patch. Most patchers ignore it, some display it.
    /// </summary>
    private static string CreatePatchMetadata(HackProject project)
    {
        ProjectSettings s = project.Settings;
        string author = s.Author.Length > 0 ? $" by {s.Author}" : string.Empty;
        return $"{s.Name} {s.Version}{author} - made with Hawk's Hangar for Pilotwings 64 (USA)";
    }
}
