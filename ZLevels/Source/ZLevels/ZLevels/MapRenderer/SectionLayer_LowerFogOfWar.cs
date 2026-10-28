using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZLevels
{
    public class SectionLayer_LowerFogOfWar : SectionLayer
    {
        public override bool Visible => DebugViewSettings.drawFog;

        public SectionLayer_LowerFogOfWar(Section section) : base(section)
        {
            relevantChangeTypes = (ulong)MapMeshFlagDefOf.FogOfWar | (ulong)MapMeshFlagDefOf.Terrain;
        }

        public override void Regenerate()
        {
            ClearSubMeshes(MeshParts.All);
            var tracker = ZUtils.ZTracker;
            int currentLevel = tracker.GetZIndexFor(Map);
            var maps = tracker.GetAllMaps(Map.Tile).OrderByDescending(map => tracker.GetZIndexFor(map)).ToList();
            foreach (IntVec3 cell in section.CellRect)
            {
                if (Map.terrainGrid.TerrainAt(cell) != ZLevelsDefOf.ZL_OutsideTerrain || cell.Fogged(Map))
                {
                    continue;
                }
                foreach (var lowerMap in maps)
                {
                    int lowerLevel = tracker.GetZIndexFor(lowerMap);
                    if (lowerLevel < 0 || lowerLevel >= currentLevel || !cell.InBounds(lowerMap))
                    {
                        continue;
                    }
                    if (cell.Fogged(lowerMap))
                    {
                        var mesh = GetSubMesh(MatBases.FogOfWar);
                        int start = mesh.verts.Count;
                        float height = AltitudeLayer.FogOfWar.AltitudeFor();
                        float offset = (currentLevel - lowerLevel) / 2f;
                        mesh.verts.Add(new Vector3(cell.x, height, cell.z - offset));
                        mesh.verts.Add(new Vector3(cell.x, height, cell.z + 1 - offset));
                        mesh.verts.Add(new Vector3(cell.x + 1, height, cell.z + 1 - offset));
                        mesh.verts.Add(new Vector3(cell.x + 1, height, cell.z - offset));
                        for (int vertex = 0; vertex < 4; vertex++)
                        {
                            mesh.colors.Add(new Color32(35, 35, 35, 255));
                        }
                        mesh.tris.Add(start);
                        mesh.tris.Add(start + 1);
                        mesh.tris.Add(start + 2);
                        mesh.tris.Add(start);
                        mesh.tris.Add(start + 2);
                        mesh.tris.Add(start + 3);
                        break;
                    }
                    if (lowerMap.terrainGrid.TerrainAt(cell) != ZLevelsDefOf.ZL_OutsideTerrain)
                    {
                        break;
                    }
                }
            }
            FinalizeMesh(MeshParts.All);
        }
    }
}
