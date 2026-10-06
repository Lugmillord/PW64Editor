using PW64Editor.Core.Project;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Project;

public class ProjectBackupsTests
{
    private const string Sha1 = "ec771aedf54ee1b214c25404fb4ec51cfd43191a";

    [Fact]
    public void List_IsEmpty_ForNewProject()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);

        Assert.Empty(ProjectBackups.List(project));
        Assert.False(Directory.Exists(ProjectBackups.GetFolder(project))); // no folder until needed
    }

    [Fact]
    public void Create_SavesProjectFilesAndRom()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);
        WriteFile(project, "1063_UPWT_005.bin", [1, 2, 3, 4]);
        File.WriteAllBytes(project.OutputRomPath, [9, 9, 9, 9]);

        BackupInfo backup = ProjectBackups.Create(project);

        Assert.Equal(1, backup.FileCount);
        Assert.True(backup.ContainsRom);
        Assert.Single(ProjectBackups.List(project));
    }

    [Fact]
    public void Create_WorksWithoutBuiltRom()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);

        BackupInfo backup = ProjectBackups.Create(project);

        Assert.False(backup.ContainsRom);
    }

    [Fact]
    public void Restore_BringsBackFilesSettingsAndRom()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);
        WriteFile(project, "1063_UPWT_005.bin", [1, 2, 3, 4]);
        File.WriteAllBytes(project.OutputRomPath, [9, 9, 9, 9]);
        BackupInfo backup = ProjectBackups.Create(project);

        // Change everything after the restore point.
        WriteFile(project, "1063_UPWT_005.bin", [5, 5, 5, 5]);
        WriteFile(project, "1064_UPWT_006.bin", [6, 6, 6, 6]);
        project.Settings.Version = "2.0";
        project.Save();
        File.WriteAllBytes(project.OutputRomPath, [0, 0, 0, 0]);

        HackProject restored = ProjectBackups.Restore(project, backup.Name);

        Assert.Equal("1.0", restored.Settings.Version);
        Assert.Equal([1063], restored.GetOverrides().Select(o => o.TableIndex).ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(restored.GetOverrides()[0].Path));
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(restored.OutputRomPath));
    }

    [Fact]
    public void Restore_KeepsLocalSettings()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, temp.Combine("clean.z64"));
        BackupInfo backup = ProjectBackups.Create(project);
        project.SetOutputRomPath(temp.Combine("new_place.z64"));
        project.Save();

        HackProject restored = ProjectBackups.Restore(project, backup.Name);

        Assert.Equal(temp.Combine("new_place.z64"), restored.OutputRomPath);
    }

    [Fact]
    public void TwoBackupsInSameSecond_GetDifferentNames()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);

        BackupInfo first = ProjectBackups.Create(project);
        BackupInfo second = ProjectBackups.Create(project);

        Assert.NotEqual(first.Name, second.Name);
        Assert.Equal(2, ProjectBackups.List(project).Count);
    }

    [Fact]
    public void Delete_RemovesBackup()
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);
        BackupInfo backup = ProjectBackups.Create(project);

        ProjectBackups.Delete(project, backup.Name);

        Assert.Empty(ProjectBackups.List(project));
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("..")]
    [InlineData("../../somewhere")]
    [InlineData("")]
    public void Restore_Throws_ForUnknownOrUnsafeName(string name)
    {
        using var temp = new TempDirectory();
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", Sha1, null);

        Assert.Throws<ProjectException>(() => ProjectBackups.Restore(project, name));
    }

    private static void WriteFile(HackProject project, string name, byte[] data) =>
        File.WriteAllBytes(Path.Combine(project.FilesFolder, name), data);
}
