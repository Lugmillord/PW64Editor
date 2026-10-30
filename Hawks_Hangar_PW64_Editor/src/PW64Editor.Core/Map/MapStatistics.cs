using PW64Editor.Core.Map.Rendering;

namespace PW64Editor.Core.Map;

/// <summary>
/// What an island (or sub-area) consists of, for the map's info window: tiles, triangles, objects, textures and the size of
/// the files its level loads.
/// </summary>
/// <param name="Tiles">Used terrain tiles.</param>
/// <param name="Contours">Different contours on them.</param>
/// <param name="TerrainTriangles">Triangles of the terrain.</param>
/// <param name="TerrainVertices">Vertices of the terrain (as drawn, per contour).</param>
/// <param name="DecorationObjects">Decoration objects standing on the terrain.</param>
/// <param name="DecorationModels">Different models among them.</param>
/// <param name="DecorationTriangles">Triangles of the decoration objects (most detailed level, every object counted).</param>
/// <param name="IslandObjects">Objects of the island's own file (pad places, wind objects, toys, warps, sound zones, stars).</param>
/// <param name="CodeObjects">Objects the game's code places (boats, gliders, trigger zones …).</param>
/// <param name="MissionObjects">Objects of the shown missions.</param>
/// <param name="Textures">Different textures of terrain and decoration.</param>
/// <param name="Texels">Texels of those textures together.</param>
/// <param name="LevelModels">Models the level loads.</param>
/// <param name="LevelContours">Contours the level loads.</param>
/// <param name="LevelTextures">Textures the level loads.</param>
/// <param name="LevelBytes">Bytes of the level's models, contours and textures as stored in the ROM.</param>
public sealed record MapStatistics(int Tiles, int Contours, int TerrainTriangles, int TerrainVertices, int DecorationObjects,
    int DecorationModels, int DecorationTriangles, int IslandObjects, int CodeObjects, int MissionObjects, int Textures, long Texels,
    int LevelModels, int LevelContours, int LevelTextures, long LevelBytes)
{
    /// <summary>Counts a scene of an island.</summary>
    public static MapStatistics Of(MapScene scene, MapLibrary library, MapIsland island)
    {
        var tiles = scene.Items.Where(i => i.Layer == SceneLayer.Terrain).ToList();
        var contours = new HashSet<RenderMesh>(tiles.Select(t => t.Mesh), ReferenceEqualityComparer.Instance);
        var decoration = scene.Picks.OfType<DecorationPick>().ToList();
        var decorationItems = scene.Items.Where(i => i.Pick >= 0 && scene.Picks[i.Pick] is DecorationPick).ToList();
        var textures = new HashSet<MapTexture>(ReferenceEqualityComparer.Instance);
        foreach (SceneItem item in tiles.Concat(decorationItems))
        {
            foreach (RenderBatch batch in item.Mesh.Batches)
            {
                if (batch.Texture is { } texture)
                {
                    textures.Add(texture);
                }
            }
        }

        LevelList level = library.Levels[island.Level];
        long bytes = level.Models.Sum(m => (long)library.StoredSize("UVMD", m))
            + level.Contours.Sum(c => (long)library.StoredSize("UVCT", c))
            + level.Textures.Sum(t => (long)library.StoredSize("UVTX", t));
        return new MapStatistics(
            tiles.Count,
            contours.Count,
            tiles.Sum(t => t.Mesh.TriangleCount),
            tiles.Sum(t => t.Mesh.Positions.Length),
            decoration.Count,
            decoration.Select(d => d.Model.Model).Distinct().Count(),
            decorationItems.Sum(i => i.Mesh.TriangleCount),
            scene.Picks.Count(p => p is IslandObjectPick),
            scene.Picks.Count(p => p is IslandExtraPick),
            scene.Picks.Count(p => p is MissionObjectPick),
            textures.Count,
            textures.Sum(t => (long)t.Width * t.Height),
            level.Models.Count,
            level.Contours.Count,
            level.Textures.Count,
            bytes);
    }
}
