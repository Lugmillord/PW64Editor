using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Project;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Project;

public class HackProjectTests
{
    private const string Sha1 = "ec771aedf54ee1b214c25404fb4ec51cfd43191a";

    [Fact]
    public void Create_WritesProjectStructure_WithoutGitIgnoreByDefault()
    {
        using var temp = new TempDirectory();
        string folder = temp.Combine("MyHack");

        HackProject project = HackProject.Create(folder, "My Hack", Sha1, cleanRomPath: null);

        Assert.True(File.Exists(Path.Combine(folder, HackProject.ProjectFileName)));
        Assert.True(File.Exists(Path.Combine(folder, HackProject.LocalFileName)));
        Assert.True(Directory.Exists(project.FilesFolder));
        Assert.False(File.Exists(Path.Combine(folder, HackProject.GitIgnoreFileName)));
    }

    [Fact]
    public void Create_WithGitPreparation_WritesGitIgnore()
    {
        using var temp = new TempDirectory();
        string folder = temp.Combine("MyHack");

        HackProject.Create(folder, "My Hack", Sha1, cleanRomPath: null, prepareForGit: true);

        string gitIgnore = File.ReadAllText(Path.Combine(folder, HackProject.GitIgnoreFileName));
        Assert.Contains("project.user.json", gitIgnore);
        Assert.Contains("backups/", gitIgnore);
        Assert.Contains("*.z64", gitIgnore);
    }

    [Fact]
    public void OutputRomPath_DefaultsToProjectFolder_AndCanBeChanged()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "My Hack", Sha1, temp.Combine("clean.z64"));

        Assert.Equal(Path.Combine(project.Folder, "My_Hack.z64"), project.OutputRomPath);

        project.SetOutputRomPath(temp.Combine("emulator", "hack.z64"));
        Assert.Equal(temp.Combine("emulator", "hack.z64"), project.OutputRomPath);

        project.SetOutputRomPath(null);
        Assert.Equal(Path.Combine(project.Folder, "My_Hack.z64"), project.OutputRomPath);
    }

    [Fact]
    public void SetOutputRomPath_RefusesTheCleanRom()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, temp.Combine("clean.z64"));

        Assert.Throws<ProjectException>(() => project.SetOutputRomPath(temp.Combine("clean.z64")));
        Assert.Null(project.Local.OutputRomPath); // nothing was changed
    }

    [Fact]
    public void Create_Throws_ForNonEmptyFolder()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("something.txt"), "x");

        Assert.Throws<ProjectException>(() => HackProject.Create(temp.Path, "Hack", Sha1, null));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsAllSettings()
    {
        using var temp = new TempDirectory();
        string folder = temp.Combine("MyHack");
        HackProject created = HackProject.Create(folder, "My Hack", Sha1, temp.Combine("rom.z64"));
        created.Settings.Version = "2.1";
        created.Settings.Author = "Lugmillord";
        created.Settings.Description = "Test";
        created.Settings.AllowExpansion = true;
        created.Save();

        HackProject loaded = HackProject.Load(folder);

        Assert.Equal("My Hack", loaded.Settings.Name);
        Assert.Equal("2.1", loaded.Settings.Version);
        Assert.Equal("Lugmillord", loaded.Settings.Author);
        Assert.Equal(Sha1, loaded.Settings.BaseRomSha1);
        Assert.True(loaded.Settings.AllowExpansion);
        Assert.True(loaded.BuildOptions.AllowExpansion);
        Assert.Equal(temp.Combine("rom.z64"), loaded.Local.CleanRomPath);
    }

    [Fact]
    public void Load_Throws_WithoutProjectFile()
    {
        using var temp = new TempDirectory();

        Assert.Throws<ProjectException>(() => HackProject.Load(temp.Path));
    }

    [Fact]
    public void Load_Throws_ForNewerFormatVersion()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);
        project.Settings.FormatVersion = ProjectSettings.CurrentFormatVersion + 1;
        project.Save();

        Assert.Throws<ProjectException>(() => HackProject.Load(project.Folder));
    }

    [Fact]
    public void Load_Throws_ForBrokenJson()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);
        File.WriteAllText(Path.Combine(project.Folder, HackProject.ProjectFileName), "{ not json");

        Assert.Throws<ProjectException>(() => HackProject.Load(project.Folder));
    }

    [Fact]
    public void GetOverrides_ParsesNames_AndIgnoresNonBinFiles()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);
        File.WriteAllBytes(Path.Combine(project.FilesFolder, "1063_UPWT_005.bin"), [1]);
        File.WriteAllBytes(Path.Combine(project.FilesFolder, "0042_UVTX_my_new_texture.bin"), [2]);
        File.WriteAllBytes(Path.Combine(project.FilesFolder, "7_PAD .bin"), [3]); // type with a space
        File.WriteAllText(Path.Combine(project.FilesFolder, "notes.txt"), "ignored");

        IReadOnlyList<FileOverride> overrides = project.GetOverrides();

        Assert.Equal([7, 42, 1063], overrides.Select(o => o.TableIndex).ToArray());
        Assert.Equal(["PAD ", "UVTX", "UPWT"], overrides.Select(o => o.FileType).ToArray());
    }

    [Fact]
    public void GetOverrides_Throws_ForUnparsableBinName()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);
        File.WriteAllBytes(Path.Combine(project.FilesFolder, "mission.bin"), [1]);

        Assert.Throws<ProjectException>(() => project.GetOverrides());
    }

    [Fact]
    public void GetOverrides_Throws_ForTwoFilesWithSameIndex()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);
        File.WriteAllBytes(Path.Combine(project.FilesFolder, "1063_UPWT_005.bin"), [1]);
        File.WriteAllBytes(Path.Combine(project.FilesFolder, "1063_UPWT_copy.bin"), [1]);

        Assert.Throws<ProjectException>(() => project.GetOverrides());
    }

    [Fact]
    public void AddOverride_ThenRemoveOverride()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);
        var file = new GameFile(1063, "UPWT", "user", 5, 0x342E2C, [1, 2, 3, 4]);

        string path = project.AddOverride(file);

        Assert.Equal("1063_UPWT_005.bin", Path.GetFileName(path));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(path));
        Assert.Throws<ProjectException>(() => project.AddOverride(file)); // no silent overwrite

        Assert.True(project.RemoveOverride(1063));
        Assert.False(project.RemoveOverride(1063));
        Assert.Empty(project.GetOverrides());
    }

    [Theory]
    [InlineData("My First Hack", "My_First_Hack")]
    [InlineData("Pilotwings: Turbo!", "Pilotwings__Turbo")]
    [InlineData("???", "hack")]
    public void GetOutputBaseName_IsSafeForFileNames(string name, string expected)
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), name, Sha1, null);

        Assert.Equal(expected, project.GetOutputBaseName());
    }
}
