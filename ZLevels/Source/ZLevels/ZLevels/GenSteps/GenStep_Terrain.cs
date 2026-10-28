using RimWorld;
using Verse;

namespace ZLevels
{
	public class GenStep_TerrainUnderground : GenStep
	{
		public override int SeedPart => 262606459;

		public override void Generate(Map map, GenStepParams parms)
		{
			MapGenFloatGrid elevation = MapGenerator.Elevation;
			MapGenFloatGrid fertility = MapGenerator.Fertility;
			MapGenFloatGrid caves = MapGenerator.Caves;
			TerrainGrid terrainGrid = map.terrainGrid;
			foreach (IntVec3 allCell in map.AllCells)
			{
				Building edifice = allCell.GetEdifice(map);
				TerrainDef terrainDef = (((edifice == null || edifice.def.Fillage != FillCategory.Full) && !(caves[allCell] > 0f))
					? TerrainFrom(allCell, map, elevation[allCell], fertility[allCell], preferSolid: false)
					: TerrainFrom(allCell, map, elevation[allCell], fertility[allCell], preferSolid: true));
				terrainGrid.SetTerrain(allCell, terrainDef);
			}
			foreach (TileMutatorWorker worker in UndergroundWaterMutators.RiverAndCoastWorkers(map))
			{
				worker.GeneratePostTerrain(map);
			}
			foreach (TileMutatorWorker worker in UndergroundWaterMutators.RiverAndCoastWorkers(map))
			{
				worker.GeneratePostFog(map);
			}
			foreach (TerrainPatchMaker terrainPatchMaker in map.Biome.terrainPatchMakers)
			{
				terrainPatchMaker.Cleanup();
			}
		}

		private bool SurroundedWithWalls(IntVec3 c, Map map)
		{
			int num = 0;
			foreach (var cell in GenAdj.CellsAdjacentCardinal(c, Rot4.South, new IntVec2(1, 1)))
			{
				if (GenGrid.InBounds(cell, map) && (cell.GetFirstBuilding(map) is Mineable
					|| cell.GetTerrain(map) == TerrainDefOf.Gravel))
				{
					num++;
					if (num >= 2)
					{
						return true;
					}
				}
			}
			return false;
		}

		private TerrainDef TerrainFrom(IntVec3 c, Map map, float elevation, float fertility, bool preferSolid)
		{
			if (c.Walkable(map) && SurroundedWithWalls(c, map) && Rand.Chance(0.2f))
			{
				return TerrainDefOf.Gravel;
			}
			if (preferSolid)
			{
				return GenStep_RocksFromGridUnderground.RockDefAt(c).building.naturalTerrain;
			}
			for (int i = 0; i < map.Biome.terrainPatchMakers.Count; i++)
			{
				TerrainDef terrainDef = map.Biome.terrainPatchMakers[i].TerrainAt(c, map, fertility);
				if (terrainDef != null)
				{
					return terrainDef;
				}
			}
			if (elevation > 0.55f && elevation < 0.61f)
			{
				return TerrainDefOf.Gravel;
			}
			if (elevation >= 0.61f)
			{
				return GenStep_RocksFromGrid.RockDefAt(c).building.naturalTerrain;
			}
			TerrainDef terrainDef2 = TerrainThreshold.TerrainAtValue(map.Biome.terrainsByFertility, fertility);
			if (terrainDef2 != null)
			{
				ZLogger.Message("8 Terrain: " + terrainDef2 + " - Loc: " + c);
				return terrainDef2;
			}
			return TerrainDefOf.Sand;
		}
	}
}
