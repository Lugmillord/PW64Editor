using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Patching;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Project;

public class ProjectBuilderTests
{
    private static readonly RomLayout Layout = RomLayout.PilotwingsUsa;

    [RealRomFact]
    public void EmptyProject_BuildsOriginalRom()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject project = HackProject.Create(temp.Combine("p"), "Empty", rom.ComputeSha1(), null);

        ProjectBuildResult result = ProjectBuilder.Build(project, rom, Layout);

        Assert.Equal(0, result.AppliedOverrides);
        Assert.Equal(rom.ComputeSha1(), result.RomBuild.Rom.ComputeSha1());
    }

    [RealRomFact]
    public void ModifiedFile_EndsUpInRom_AndPatchReproducesIt()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, Layout);
        HackProject project = HackProject.Create(temp.Combine("p"), "Test Hack", rom.ComputeSha1(), null);

        GameFile mission = fs.Files.First(f => f.TableIndex == 1063);
        string path = project.AddOverride(mission);
        byte[] edited = File.ReadAllBytes(path);
        edited[0x100] ^= 0x01;
        File.WriteAllBytes(path, edited);

        ProjectBuildResult result = ProjectBuilder.Build(project, rom, Layout);

        Assert.Equal(1, result.AppliedOverrides);
        Assert.Equal(edited, GameFileSystem.Read(result.RomBuild.Rom, Layout).Files.First(f => f.TableIndex == 1063).Data);

        byte[] patch = ProjectBuilder.CreatePatch(project, rom, result.RomBuild.Rom);
        Assert.Equal(result.RomBuild.Rom.Data, BpsReader.Apply(rom.Data, patch));
        Assert.Contains("Test Hack", BpsReader.ReadInfo(patch).Metadata);
    }

    [RealRomFact]
    public void WriteRom_OverwritesWithoutCreatingExtraFiles()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject project = HackProject.Create(temp.Combine("p"), "Out Test", rom.ComputeSha1(), null);
        ProjectBuildResult result = ProjectBuilder.Build(project, rom, Layout);

        string path = ProjectBuilder.WriteRom(project, result);
        ProjectBuilder.WriteRom(project, result);

        Assert.Equal(project.OutputRomPath, path);
        Assert.True(File.Exists(path));
        Assert.Single(Directory.EnumerateFiles(project.Folder, "*.z64"));
    }

    [RealRomFact]
    public void WriteRom_RefusesToOverwriteCleanRom()
    {
        using var temp = new TempDirectory();
        string cleanPath = temp.Combine("clean.z64");
        File.Copy(TestRomLocator.RomPath!, cleanPath);
        N64Rom rom = N64Rom.Load(cleanPath);
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", rom.ComputeSha1(), cleanPath);
        project.Local.OutputRomPath = cleanPath; // as if project.user.json had been edited by hand
        ProjectBuildResult result = ProjectBuilder.Build(project, rom, Layout);

        Assert.Throws<ProjectException>(() => ProjectBuilder.WriteRom(project, result));
    }

    [RealRomFact]
    public void Build_Throws_ForWrongBaseRom()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", "0000000000000000000000000000000000000000", null);

        Assert.Throws<ProjectException>(() => ProjectBuilder.Build(project, rom, Layout));
    }

    [RealRomFact]
    public void Build_Throws_WhenFileContentHasWrongType()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", rom.ComputeSha1(), null);
        byte[] texture = IffWriter.BuildForm("UVTX", [IffWriter.BuildChunk("COMM", new byte[8])]);
        File.WriteAllBytes(Path.Combine(project.FilesFolder, "1063_UPWT_005.bin"), texture);

        Assert.Throws<ProjectException>(() => ProjectBuilder.Build(project, rom, Layout));
    }

    [RealRomFact]
    public void Build_Throws_WhenNameTypeDoesNotMatchGameFile()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", rom.ComputeSha1(), null);
        File.WriteAllBytes(Path.Combine(project.FilesFolder, "1063_UVTX.bin"), [0]);

        Assert.Throws<ProjectException>(() => ProjectBuilder.Build(project, rom, Layout));
    }

    [RealRomFact]
    public void Build_Throws_ForUnknownTableIndex()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", rom.ComputeSha1(), null);
        File.WriteAllBytes(Path.Combine(project.FilesFolder, "9999_UPWT.bin"), [0]);

        Assert.Throws<ProjectException>(() => ProjectBuilder.Build(project, rom, Layout));
    }

    [RealRomFact]
    public void Status_ClassifiesFiles()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, Layout);
        HackProject project = HackProject.Create(temp.Combine("p"), "Hack", rom.ComputeSha1(), null);

        project.AddOverride(fs.Files[5]); // unchanged copy
        string modified = project.AddOverride(fs.Files.First(f => f.TableIndex == 1063));
        byte[] data = File.ReadAllBytes(modified);
        data[0x100] ^= 0x01;
        File.WriteAllBytes(modified, data);
        File.WriteAllBytes(Path.Combine(project.FilesFolder, "1064_UVTX.bin"), [0]); // wrong type

        IReadOnlyList<OverrideStatus> status = ProjectStatus.Compute(project, fs);

        Assert.Equal(OverrideState.Unchanged, status[0].State);
        Assert.Equal(OverrideState.Modified, status[1].State);
        Assert.Equal("1 byte changed", status[1].Message);
        Assert.Equal(OverrideState.Invalid, status[2].State);
    }
}
