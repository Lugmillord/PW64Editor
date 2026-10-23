using System.Buffers.Binary;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Missions;

/// <summary>Stage 6.5: weather and view per mission.</summary>
public class WeatherTests
{
    private static uint Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    [Fact]
    public void Fog_MatchesTheGame()
    {
        // The game's fog factor 0.996 at the far distance 2000: fog from about 222 m, the command uvGfxSetFogFactor writes.
        Assert.Equal(222, WeatherCatalog.GameFogStart(2000), 0);
        Assert.Equal((32000, -31744), WeatherCatalog.FogCommand(996, 1000));
        (double min, double max) = WeatherCatalog.FogRange(WeatherCatalog.GameFogStart(2000), 100, 2000);
        Assert.Equal(996, min, 3);
        Assert.Equal(1000, max, 3);
        Assert.Equal(500, WeatherCatalog.DistanceOf(WeatherCatalog.Depth(500, 1500), 1500), 3);
        (double back, double backMax) = WeatherCatalog.FogRangeOf(15796, -15617);
        Assert.Equal(994.3, back, 1);
        Assert.Equal(1002.4, backMax, 1);
        // Half density: full fog only at twice the depth range.
        (min, max) = WeatherCatalog.FogRange(300, 50, 2000);
        Assert.Equal((1000 - min) * 2, max - min, 3);
    }

    [Fact]
    public void Wind_Colors_And_Conditions()
    {
        Assert.Equal((90f, 2f), WeatherCatalog.WindOf((2, 0, 0)));
        Assert.Equal((180f, 1.5f), WeatherCatalog.WindOf((0, -1.5f, 0)));
        (float x, float y, float z) = WeatherCatalog.WindVector(45, 2.8284f, 0.5f);
        Assert.Equal(2, x, 3);
        Assert.Equal(2, y, 3);
        Assert.Equal(0.5f, z);
        Assert.Equal(0xFFFF, WeatherCatalog.ToRgba5551(0xFFFFFF));
        Assert.Equal(0x0001, WeatherCatalog.ToRgba5551(0x000000));
        Assert.Equal(0xD1E5FFu, WeatherCatalog.ParseHex("#d1e5ff"));
        Assert.Null(WeatherCatalog.ParseHex("blue"));
        Assert.False(WeatherCatalog.IsOriginal(0, WeatherCondition.DarkOvercast));
        Assert.False(WeatherCatalog.IsOriginal(3, WeatherCondition.Sunset));
        Assert.True(WeatherCatalog.IsOriginal(2, WeatherCondition.DarkOvercast));
        Assert.True(WeatherCatalog.IsAvailable(0, WeatherCondition.DarkOvercast)); // added by the editor
        Assert.False(WeatherCatalog.IsAvailable(4, WeatherCondition.ClearDay));    // no fifth island yet
        Assert.Equal(0x0F1616u, WeatherCatalog.OriginalFogColor(2, WeatherCondition.DarkOvercast));
        Assert.Equal(27, WeatherCatalog.Colors.Count);
        Assert.Equal(6, WeatherCatalog.Colors.Count(c => c.Original));
    }

    [Fact]
    public void ToModule_AndTheEntry()
    {
        byte[] data = new byte[MissionComm.Size];
        MissionComm comm = MissionComm.Parse(data);
        var weather = new MissionWeather { FogColor = "#D02020", SkyTint = true, ViewDistance = 1200, FogStart = 150, FogDensity = 70, Rain = true };
        Assert.Null(WeatherCatalog.ToModule(new MissionWeather(), comm));

        // Without snow on in the mission file the rain is not used.
        GameModuleWeather module = WeatherCatalog.ToModule(weather, comm)!;
        Assert.Null(module.Effect);
        comm.Snow = true;
        module = WeatherCatalog.ToModule(weather, comm)!;
        Assert.Equal(new GameModuleEffect(true, WeatherCatalog.ToRgba5551(0xB0C4DE), 25, 800), module.Effect);
        Assert.Equal(0xD02020u, module.FogColor);
        Assert.True(module.SkyTint);
        Assert.Equal(1200f, module.ViewDistance);
        Assert.NotNull(module.FogRange);

        // The sky tinted with the environment's own fog colour: that colour is written, and the import stores none.
        var tintOnly = new MissionWeather { SkyTint = true };
        comm.Weather = WeatherCondition.Overcast;
        GameModuleWeather tinted = WeatherCatalog.ToModule(tintOnly, comm)!;
        Assert.Equal(0x374242u, tinted.FogColor);
        Assert.True(tinted.SkyTint);
        MissionWeather tintBack = ProjectImporter.ToWeather(tinted, comm)!;
        Assert.Null(tintBack.FogColor);
        Assert.True(tintBack.SkyTint);

        // A sunset keeps its own fog colour.
        comm.Weather = WeatherCondition.Sunset;
        Assert.Null(WeatherCatalog.ToModule(weather, comm)!.FogColor);
        comm.Weather = WeatherCondition.Night;

        var entry = new GameModuleCondition(0, 0, 0, false, null, new byte[GameModule.ObjectTypes]) { Weather = module };
        byte[] image = DataArea.CreateImage([entry]);
        Assert.Equal(1 | 2 | 4 | 8 | 16, image[DataArea.HeaderSize + 0x80]);
        Assert.Equal(module, DataArea.ReadImage(image).Single().Weather);

        // Back to the settings (the import).
        MissionWeather back = ProjectImporter.ToWeather(module)!;
        Assert.Equal("#D02020", back.FogColor);
        Assert.Equal(1200f, back.ViewDistance);
        Assert.Equal(150, back.FogStart!.Value, 0);
        Assert.Equal(70, back.FogDensity!.Value, 0);
        Assert.True(back.Rain);
        Assert.Null(back.EffectColor);
        Assert.Null(back.FallSpeed);
    }

    [RealRomFact]
    public void Draft_Build_AndImport()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Weather", false, rom, TestRomLocator.RomPath!);

        // Without weather settings: only the Sky Diving change; snow and fog as the game.
        byte[] plain = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(CodeArea.Jal(GameModuleImage.SkyDivingBelowAddress), Word(plain, GameModule.SkyDivingBelowRomOffset));
        Assert.Equal(0x0C0BAD66u, Word(plain, 0xCC818));
        Assert.Equal(0x44800000u, Word(plain, 0x21C50));

        var id = new TestId(0, Vehicle.HangGlider, 0);
        MissionInfo hg = session.LoadMissions().Missions.Single(m => m.Id == id);
        var draft = new MissionDraft(hg, null);
        Assert.Equal(WeatherEffect.None, draft.Effect);
        Assert.Equal(2000f, draft.ViewDistance);
        Assert.True(draft.FogAligned);
        Assert.Equal(0xD1E5FFu, draft.FogColor);

        draft.SetWeather(WeatherCondition.Overcast);
        draft.SetEffect(WeatherEffect.Rain);
        Assert.Equal(25f, draft.FallSpeed);
        draft.SetFallSpeed(25); // the default: no setting
        Assert.Null(draft.Settings.Weather!.FallSpeed);
        draft.SetDensity(1000);
        draft.SetEffectColor(0x40E0F0);
        draft.SetFogColor(0x702080);
        draft.SetSkyTint(true);
        draft.SetViewDistance(1500);
        draft.SetFogAligned(false);
        Assert.Equal(Math.Round(WeatherCatalog.GameFogStart(1500)), draft.FogStart);
        draft.SetFog(300, 60);
        draft.SetWind(270, 4);
        draft.SetWindVariation(1, 2, 30, 10);
        Assert.Equal((270f, 4f), draft.Wind);
        Assert.True(draft.IsChanged);
        Assert.False(draft.Settings.GenerateTexts); // the weather does not touch the point texts
        session.SaveMission(hg, draft.Comm, draft.SettingsToSave());

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(CodeArea.Jal(GameModuleImage.EnvStartAddress), Word(built, 0xCC818));
        Assert.Equal(CodeArea.Jal(GameModuleImage.EnvEndAddress), Word(built, 0xCC8C4));
        Assert.Equal(CodeArea.J(GameModuleImage.FogFactorAddress), Word(built, 0x21C50));
        Assert.Equal(CodeArea.J(GameModuleImage.EffectUpdateAddress), Word(built, 0xC74E4));
        Assert.Equal(CodeArea.J(GameModuleImage.EffectDrawAddress), Word(built, 0xC7460));
        Assert.Equal(0xC4245060u, Word(built, 0x5A6D8)); // cameraInit: lwc1 f4, 0x5060(at)
        MissionComm comm = GameFileSystem.Read(N64Rom.FromBytes(built), RomLayout.PilotwingsUsa).Files
            .Where(f => f.FileType == MissionFile.FormType).Select(f => MissionFile.Parse(f.Data).Comm).Single(c => c.Id == id);
        Assert.Equal(WeatherCondition.Overcast, comm.Weather);
        Assert.True(comm.Snow);
        Assert.Equal(30f, comm.WindDirectionRange);
        Assert.Contains("Weather settings: effect rain", MissionReport.Create(session), StringComparison.Ordinal);

        RomImport import = ProjectImporter.Analyze(N64Rom.FromBytes(built), rom, RomLayout.PilotwingsUsa, "file");
        MissionWeather weather = import.Settings.Missions.Single(m => m.Id == id).Weather!;
        Assert.True(weather.Rain);
        Assert.Equal(1000, weather.Density);
        Assert.Equal("#40E0F0", weather.EffectColor);
        Assert.Equal("#702080", weather.FogColor);
        Assert.True(weather.SkyTint);
        Assert.Equal(1500f, weather.ViewDistance);
        Assert.InRange(weather.FogStart!.Value, 298, 302); // the game's fog command is in whole numbers
        Assert.Equal(60, weather.FogDensity!.Value, 0);

        // Back to the game's values: no entry, no patches.
        draft = new MissionDraft(session.LoadMissions().Missions.Single(m => m.Id == id), null);
        draft.SetEffect(WeatherEffect.None);
        draft.SetDensity(800);
        draft.SetFogColor(null);
        draft.SetSkyTint(false);
        draft.SetViewDistance(2000);
        draft.SetFogAligned(true);
        draft.SetEffectColor(WeatherCatalog.DefaultEffectColor(WeatherEffect.None));
        Assert.Null(draft.Settings.Weather);
        session.SaveMission(hg, draft.Comm, draft.SettingsToSave());
        built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x0C0BAD66u, Word(built, 0xCC818));
    }

    [Fact]
    public void Environments_TableAndMask()
    {
        Assert.Equal(24, WeatherEnvironments.Environment(0, WeatherCondition.DarkOvercast));
        Assert.Equal(16, WeatherEnvironments.Environment(2, WeatherCondition.Sunset));
        Assert.True(WeatherEnvironments.IsAdded(3, WeatherCondition.Sunset));
        Assert.False(WeatherEnvironments.IsAdded(2, WeatherCondition.Sunset));
        Assert.True(WeatherCatalog.IsAvailable(1, WeatherCondition.Sunset));
        Assert.False(WeatherCatalog.IsOriginal(1, WeatherCondition.Sunset));

        // bit 31 - environment: 5, 16, 25, 27 (the game's test was "== 5 || == 16").
        Assert.Equal((1u << 26) | (1u << 15) | (1u << 6) | (1u << 4), WeatherEnvironments.SunsetMask);

        byte[] table = WeatherEnvironments.Table();
        Assert.Equal(WeatherEnvironments.TableSize, table.Length);
        Assert.Equal([1, 2, 3, 4, 24, 5, 6, 0], table[0..8]);
        Assert.Equal([10, 18, 19, 20, 26, 27, 21, 0], table[24..32]);
        Assert.Equal([10, 4, 0, 0], table[64..68]); // environment 24: tint of 10, palette of 4 (none)
        Assert.Equal([5, 18, 0, 0], table[76..80]); // environment 27: tint of 5, Ever-Frost's day palette

        byte[] image = DataArea.CreateImage([], environmentTable: table);
        Assert.Equal(table, DataArea.ReadEnvironmentTable(image));
        Assert.Null(DataArea.ReadEnvironmentTable(DataArea.CreateImage([])));
    }

    [RealRomFact]
    public void Environments_Files()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        List<GameFile> files = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa).Files.ToList();
        byte[] uven = files.Single(f => f.FileType == "UVEN").Data, uvlv = files.Single(f => f.FileType == "UVLV").Data;
        WeatherEnvironments.AddTo(files);
        List<byte[]> envs = Comm(files.Single(f => f.FileType == "UVEN").Data);
        List<byte[]> levels = Comm(files.Single(f => f.FileType == "UVLV").Data);
        Assert.Equal(28, envs.Count);
        Assert.Equal(0x70 + 28, levels.Count);
        Assert.Equal(uven.Length + (4 * (8 + 72)), files.Single(f => f.FileType == "UVEN").Data.Length);
        byte[] newLevels = files.Single(f => f.FileType == "UVLV").Data;
        Assert.Equal(newLevels.Length, IffForm.ReadHeader(newLevels).TotalSize);
        Assert.True(newLevels.AsSpan(0, uvlv.Length - 0).Slice(12, uvlv.Length - 12).SequenceEqual(uvlv.AsSpan(12)));

        // Holiday Island dark overcast: the dark overcast sky (0x167) and Holiday's far model (0x161), fog 1E2D2D.
        Assert.Equal([2, 0x01, 0x67, 8, 0x01, 0x61, 0], envs[24][0..7]);
        Assert.Equal([0x1E, 0x2D, 0x2D], envs[24][(7 + 4)..(7 + 7)]);
        // Crescent sunset: the sunset sky (0x164) with Crescent's far model (0x166); Ever-Frost: 0x169.
        Assert.Equal([2, 0x01, 0x64, 8, 0x01, 0x66, 0], envs[25][0..7]);
        Assert.Equal([2, 0x01, 0x67, 8, 0x01, 0x69, 0], envs[26][0..7]);
        Assert.Equal([2, 0x01, 0x64, 8, 0x01, 0x69, 0], envs[27][0..7]);
        // Their levels: environment 24 with the models 0x161 and 0x167 and the dark overcast's textures.
        Assert.Equal("0000" + "0000" + "00010018" + "000201610167" + "0000" + "0002001a01ca",
            Convert.ToHexString(levels[0x70 + 24].AsSpan(0, 22)).ToLowerInvariant());
        Assert.Equal("00010019" + "000201660164", Convert.ToHexString(levels[0x70 + 25].AsSpan(4, 10)).ToLowerInvariant());
    }

    private static List<byte[]> Comm(byte[] form)
    {
        IffForm parsed = IffForm.Parse(form);
        return parsed.Chunks.Where(c => c.Tag == "COMM").Select(c => form.AsSpan(c.DataOffset, c.DataSize).ToArray()).ToList();
    }

    [RealRomFact]
    public void Environments_Build_AndImport()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Environments", false, rom, TestRomLocator.RomPath!);
        byte[] plain = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x27BDFFE0u, Word(plain, 0x687E4)); // no mission uses a new one: the game's code

        // Holiday Island, Hang Glider Beginner: dark overcast.
        var id = new TestId(0, Vehicle.HangGlider, 0);
        MissionInfo hg = session.LoadMissions().Missions.Single(m => m.Id == id);
        var draft = new MissionDraft(hg, null);
        draft.SetWeather(WeatherCondition.DarkOvercast);
        Assert.Equal(0x1E2D2Du, draft.FogColor);
        session.SaveMission(hg, draft.Comm, draft.SettingsToSave());

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(CodeArea.J(GameModuleImage.EnvIdAddress), Word(built, 0x687E4));
        Assert.Equal(CodeArea.Jal(GameModuleImage.EnvLevelAddress), Word(built, 0x689B4));
        Assert.Equal(CodeArea.Jal(GameModuleImage.EnvTintAddress), Word(built, 0x689BC));
        Assert.Equal(CodeArea.J(GameModuleImage.EnvPaletteAddress), Word(built, 0x68EC0));
        Assert.Equal(0x3C010400u, Word(built, 0x695A8));
        Assert.Equal(0x34218050u, Word(built, 0x695AC));
        N64Rom builtRom = N64Rom.FromBytes(built);
        var fs = GameFileSystem.Read(builtRom, RomLayout.PilotwingsUsa);
        Assert.Equal(28, Comm(fs.Files.Single(f => f.FileType == "UVEN").Data).Count);
        GameModuleHeader module = GameModule.ReadHeader(built)!;
        Assert.Equal(WeatherEnvironments.Table(), DataArea.ReadEnvironmentTable(GameModule.ReadDataImage(built, module)!));

        // The import takes the mission over, not the environment and level files.
        RomImport import = ProjectImporter.Analyze(builtRom, rom, RomLayout.PilotwingsUsa, "file");
        Assert.DoesNotContain(import.ChangedFiles, f => f.FileType is "UVEN" or "UVLV");
        Assert.Contains(import.ChangedFiles, f => f.FileType == MissionFile.FormType);
    }
}
