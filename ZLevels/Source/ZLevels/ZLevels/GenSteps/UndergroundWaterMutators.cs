using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZLevels
{
	// RiverMaker and BeachMaker drew the underground rivers and ocean beaches before 1.7 and no longer exist.
	// The river and coast tile mutators draw the same features, so the underground levels run them as well.
	// Lakeshore is a coast-type mutator, but it draws lakes, which the underground never had.
	public static class UndergroundWaterMutators
	{
		public static IEnumerable<TileMutatorWorker> RiverAndCoastWorkers(Map map)
		{
			foreach (TileMutatorDef mutator in map.TileInfo.Mutators)
			{
				TileMutatorWorker worker = mutator.Worker;
				if (worker is TileMutatorWorker_River || (worker is TileMutatorWorker_Coast && !(worker is TileMutatorWorker_Lakeshore)))
				{
					yield return worker;
				}
			}
		}
	}
}
