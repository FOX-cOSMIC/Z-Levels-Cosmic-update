using Verse;

namespace ZLevels
{
	public class GenStep_TerrainUpper : GenStep
	{
		public override int SeedPart => 262606459;

		public override void Generate(Map map, GenStepParams parms)
		{
			using (map.pathing.DisableIncrementalScope())
			{
				foreach (IntVec3 cell in map.AllCells)
				{
					map.terrainGrid.SetTerrain(cell, ZLevelsDefOf.ZL_OutsideTerrain);
				}
			}
		}
	}
}
