using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZLevels
{
	[DefOf]
	public static class ZLevelsDefOf
	{
		public static ThingDef ZL_StairsUp;

		public static ThingDef ZL_StairsDown;

		public static ThingDef ZL_NaturalHole;

		public static ThingDef ZL_SmallLadderUp;

		public static ThingDef ZL_SmallLadderDown;

		public static ThingDef ZL_LargeLadderUp;

		public static ThingDef ZL_LargeLadderDown;

		public static ThingDef ZL_LargeStairsUp;

		public static ThingDef ZL_LargeStairsDown;

		public static WorldObjectDef ZL_Underground;

		public static WorldObjectDef ZL_Upper;

		public static TerrainDef ZL_OutsideTerrain;

		public static TerrainDef ZL_OutsideTerrainTwo;

		public static TerrainDef ZL_RoofTerrain;

		public static BiomeDef ZL_UndegroundBiome;

		public static BiomeDef ZL_UpperBiome;

		public static GameConditionDef ZL_UndergroundCondition;

		public static KeyBindingDef ZL_switchFloorUpper;

		public static KeyBindingDef ZL_switchFloorLower;

		public static MapGeneratorDef ZL_EmptyMap;

		public static JobDef ZL_GoToStairs;

		public static JobDef ZL_GoToMap;

		public static JobDef ZL_HaulThingToDest;

		public static JobDef ZL_HaulToCell;

		public static JobDef ZL_GoToLocation;
	}
}

