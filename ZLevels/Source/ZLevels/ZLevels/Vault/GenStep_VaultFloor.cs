using RimWorld;
using Verse;

namespace ZLevels
{
    // The vault floor is concrete everywhere. The pocket has no rock, plants, or fog.
    public class GenStep_VaultFloor : GenStep
    {
        public override int SeedPart => 7731041;

        public override void Generate(Map map, GenStepParams parms)
        {
            foreach (IntVec3 cell in map.AllCells)
            {
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            }
        }
    }
}
