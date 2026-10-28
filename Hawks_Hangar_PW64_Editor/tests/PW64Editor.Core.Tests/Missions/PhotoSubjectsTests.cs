using System.Buffers.Binary;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Missions;

/// <summary>Stage 8: photo subjects of a mission's own (ten slots, single objects, Birdman).</summary>
public class PhotoSubjectsTests
{
    private static readonly TestId HgA1 = new(1, Vehicle.HangGlider, 0);
    private static readonly TestId HgB2 = new(2, Vehicle.HangGlider, 1);
    private static readonly TestId HgP3 = new(3, Vehicle.HangGlider, 2);

    [RealRomFact]
    public void FromGame_TheThreePhotoMissions()
    {
        using var temp = new TempDirectory();
        EditorSession session = Session(temp);
        IReadOnlyList<MissionInfo> missions = session.LoadMissions().Missions;

        List<PhotoSubject> a1 = PhotoSubjects.FromGame(missions.Single(m => m.Id == HgA1).File);
        Assert.Equal([PhotoSubjectKind.Flame], a1.Select(s => s.Kind));
        Assert.Equal(60, a1[0].Points);
        Assert.Equal(100f, a1[0].Curve.Max(p => p.Y));
        Assert.Equal(new PhotoSample(120, 0, 0), PhotoSubjects.DefaultSample(a1[0])); // the game's sample photo of the flame

        List<PhotoSubject> b2 = PhotoSubjects.FromGame(missions.Single(m => m.Id == HgB2).File);
        Assert.Equal([30, 30], b2.Select(s => s.Points));
        Assert.Contains(b2, s => s.Kind == PhotoSubjectKind.Fountain && s.Direction == 90 && s.Tolerance == 260);
        Assert.Contains(b2, s => s.Kind == PhotoSubjectKind.Whale && s.Direction is null);
        Assert.Equal(new PhotoSample(85, 90, 20), PhotoSubjects.DefaultSample(b2.Single(s => s.Kind == PhotoSubjectKind.Fountain)));
        Assert.Equal(new PhotoSample(105, 145, 25), PhotoSubjects.DefaultSample(b2.Single(s => s.Kind == PhotoSubjectKind.Whale)));

        List<PhotoSubject> p3 = PhotoSubjects.FromGame(missions.Single(m => m.Id == HgP3).File);
        Assert.Equal(3, p3.Count);
        Assert.All(p3, s => Assert.Equal(20, s.Points));
        Assert.Equal(60, PhotoSubjects.MaxPoints(p3));

        // The photo row shows the subjects' points.
        MissionInfo p3Mission = missions.Single(m => m.Id == HgP3);
        Assert.Contains(p3Mission.Rows, r => r.Source == PointSource.Photo && r.Value == 60 && r.Kind == PointValueKind.PhotoSubjects);
        Assert.Empty(PhotoSubjects.FromGame(missions.Single(m => m.Id == new TestId(0, Vehicle.HangGlider, 0)).File));
    }

    [Fact]
    public void Islands_AndProblems()
    {
        Assert.Equal([PhotoSubjectKind.Flame, PhotoSubjectKind.WhalePod, PhotoSubjectKind.GliderToy], PhotoSubjects.OnIsland(3));
        Assert.Equal(4, PhotoSubjects.Count(PhotoSubjectKind.GliderToy, 2));
        Assert.Equal(0, PhotoSubjects.Count(PhotoSubjectKind.Whale, 0));

        PhotoSubject pod = PhotoSubjects.Default(PhotoSubjectKind.WhalePod, 20);
        // Full points around the type's sample photo (110 units, 15° high), none at a quarter or four times of it.
        Assert.Equal(new PhotoSample(110, 0, 15), PhotoSubjects.DefaultSample(pod));
        Assert.Equal([28f, 66f, 176f, 440f], pod.Curve.Select(p => p.X));
        PhotoSubject flame = PhotoSubjects.Default(PhotoSubjectKind.Flame, 10);
        Assert.Equal(new PhotoSample(120, 0, 0), PhotoSubjects.DefaultSample(flame)); // the game's sample photo
        flame.Curve = [new CurvePoint(150, 0), new CurvePoint(200, 100), new CurvePoint(300, 100), new CurvePoint(400, 0)];
        flame.Direction = 30;
        Assert.Equal(new PhotoSample(250, 30, 0), PhotoSubjects.DefaultSample(flame)); // the middle of the full points
        Assert.Equal(18f, PhotoSubjects.DefaultSample(PhotoSubjects.Default(PhotoSubjectKind.GliderToy, 5)).Distance);
        Assert.Null(PhotoSubjects.Problem([pod], 3));
        Assert.NotNull(PhotoSubjects.Problem([pod], 1)); // no whale pod on Crescent Island
        pod.Instance = 5;
        Assert.NotNull(PhotoSubjects.Problem([pod], 3)); // five whales: 0-4
        pod.Instance = 4;
        Assert.Null(PhotoSubjects.Problem([pod], 3));
        Assert.NotNull(PhotoSubjects.Problem(Enumerable.Repeat(PhotoSubjects.Default(PhotoSubjectKind.GliderToy, 5), 11).ToList(), 3));

        List<PhotoSubject> scaled = PhotoSubjects.WithPoints([PhotoSubjects.Default(PhotoSubjectKind.Flame, 30), PhotoSubjects.Default(PhotoSubjectKind.WhalePod, 10)], 80);
        Assert.Equal([60, 20], scaled.Select(s => s.Points));
    }

    [Fact]
    public void Table_RoundTrip()
    {
        PhotoSubject flame = PhotoSubjects.Default(PhotoSubjectKind.Flame, 40);
        PhotoSubject glider = PhotoSubjects.Default(PhotoSubjectKind.GliderToy, 20);
        glider.Instance = 2;
        glider.Direction = 45.5f;
        glider.Tolerance = 60;
        glider.Curve = [new CurvePoint(10, 33.3f), new CurvePoint(200.25f, 87.5f)];
        glider.Sample = new PhotoSample(150.5f, 33, -10);
        byte[] table = PhotoSubjects.CreateTable([(HgA1, [flame, glider]), (new TestId(0, Vehicle.Birdman, 1), [])]);
        Assert.Equal(PhotoSubjects.Magic, BinaryPrimitives.ReadUInt32BigEndian(table));
        Assert.Equal(0, table.Length % 8);

        var read = PhotoSubjects.ReadTable(table);
        Assert.Equal(2, read.Count);
        Assert.Equal(HgA1, read[0].Id);
        Assert.True(read[0].Subjects[0].SameAs(flame));
        Assert.True(read[0].Subjects[1].SameAs(glider));
        Assert.Null(read[0].Subjects[0].Sample); // the default is not stored
        Assert.Empty(read[1].Subjects);
        Assert.Equal(table, PhotoSubjects.CreateTable(read.Select(r => (r.Id, (IReadOnlyList<PhotoSubject>)r.Subjects))));
        Assert.Empty(PhotoSubjects.CreateTable([]));
    }

    [RealRomFact]
    public void Draft_EditsSubjects_AndTheRow()
    {
        using var temp = new TempDirectory();
        EditorSession session = Session(temp);
        MissionInfo a1 = session.LoadMissions().Missions.Single(m => m.Id == HgA1);
        var draft = new MissionDraft(a1, null);
        Assert.False(draft.HasOwnPhotos);
        Assert.Equal(60, draft.Photos.Single().Points);

        List<PhotoSubject> subjects = [.. draft.Photos.Select(s => s.Clone()), PhotoSubjects.Default(PhotoSubjectKind.WhalePod, 20)];
        Assert.Null(draft.SetPhotos(subjects));
        Assert.True(draft.HasOwnPhotos);
        Assert.True(draft.IsChanged);
        int row = draft.Rows.IndexOf(PointSource.Photo);
        Assert.Equal(80, draft.EvaluateRows(_ => null)[row].Value);
        draft.SetRowPoints(row, 40, _ => null);
        Assert.Equal([30, 10], draft.Photos.Select(s => s.Points));
        Assert.NotNull(draft.SetPhotos([PhotoSubjects.Default(PhotoSubjectKind.Whale, 10)])); // no whale on Ever-Frost Island
        draft.ResetPhotos();
        Assert.False(draft.HasOwnPhotos);
    }

    [RealRomFact]
    public void Build_WritesTheTable_AndImportTakesItBack()
    {
        using var temp = new TempDirectory();
        EditorSession session = Session(temp);
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        PhotoSubject glider = PhotoSubjects.Default(PhotoSubjectKind.GliderToy, 15);
        glider.Instance = 1;
        MissionSettings settings = MissionSettings.For(HgA1);
        settings.PhotoSubjects = [PhotoSubjects.Default(PhotoSubjectKind.Flame, 45), glider];
        session.Project.Settings.Missions.Add(settings);
        session.Project.Save();
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;

        // The table behind the data area's image, its size in the header.
        DataImages images = GameModule.ReadDataImages(built, GameModule.ReadHeader(built)!)!;
        Assert.NotNull(images.Photos);
        Assert.Equal((uint)images.Photos!.Length, BinaryPrimitives.ReadUInt32BigEndian(images.Main.AsSpan(DataArea.PhotoTableSizeOffset)));
        var table = PhotoSubjects.ReadTable(images.Photos);
        Assert.Equal(HgA1, table.Single().Id);
        Assert.Equal(2, table.Single().Subjects.Count);
        Assert.Equal(CodeArea.Jal(GameModuleImage.SnapPhotoAddress), BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(0xC1334)));
        // Radar marks of all subjects' objects (snapInit) and their map labels (map3dLoad, pause menu's map).
        Assert.Equal(CodeArea.Jal(GameModuleImage.SnapInitAddress), BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(0xBF908)));
        Assert.Equal(CodeArea.Jal(GameModuleImage.MapPhotosAddress), BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(0x976A0)));
        Assert.Equal(CodeArea.Jal(GameModuleImage.MapPauseAddress), BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(0xB439C)));

        RomImport import = ProjectImporter.Analyze(N64Rom.FromBytes(built), rom, RomLayout.PilotwingsUsa, "file");
        MissionSettings imported = import.Settings.Missions.Single(m => m.Id == HgA1);
        Assert.Equal(2, imported.PhotoSubjects!.Count);
        Assert.True(imported.PhotoSubjects[1].SameAs(glider));
        (EditorSession _, IReadOnlyList<ImportNote> report) = EditorSession.CreateFromImport(
            import, N64Rom.FromBytes(built), temp.Combine("imported"), "Photos", false, rom, TestRomLocator.RomPath!);
        Assert.Contains(report, n => n.Text.StartsWith("The project builds exactly the imported ROM", StringComparison.Ordinal));

        // Without own subjects there is no table.
        session.Project.Settings.Missions.Clear();
        session.Project.Save();
        byte[] plain = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Null(GameModule.ReadDataImages(plain, GameModule.ReadHeader(plain)!)!.Photos);
    }

    [RealRomFact]
    public void Build_RefusesSubjectsTheIslandLacks()
    {
        using var temp = new TempDirectory();
        EditorSession session = Session(temp);
        MissionSettings settings = MissionSettings.For(HgA1);
        settings.PhotoSubjects = [PhotoSubjects.Default(PhotoSubjectKind.Ferry, 10)];
        session.Project.Settings.Missions.Add(settings);
        session.Project.Save();
        ProjectException problem = Assert.Throws<ProjectException>(() => session.Build(false));
        Assert.Contains("Ferry", problem.Message, StringComparison.Ordinal);
    }

    private static EditorSession Session(TempDirectory temp)
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        return EditorSession.Create(temp.Combine("hack"), "Photos", false, rom, TestRomLocator.RomPath!);
    }
}
