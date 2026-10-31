using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZLevels
{
	[StaticConstructorOnStartup]
	public static class WealthPatches
	{

		[HarmonyPatch(typeof(WealthWatcher), "ForceRecount")]
		internal static class ForceRecountVaultPocketPatch
		{
			// Vanilla adds each pocket map's wealth to the map it came from. The vault pocket is not part of the surface's wealth, so
			// once the logs and walls are inside it they leave the surface's total. Pawns stay counted, because they are people.
			[HarmonyPostfix]
			public static void Postfix(Map ___map, ref float ___wealthItems, ref float ___wealthBuildings, ref float ___wealthFloorsOnly)
			{
				Map vault = Current.Game?.GetComponent<VaultPocket>()?.Pocket;
				if (vault == null || !(vault.Parent is PocketMapParent parent) || parent.sourceMap != ___map)
				{
					return;
				}
				WealthWatcher watcher = vault.wealthWatcher;
				___wealthItems -= Traverse.Create(watcher).Field<float>("wealthItems").Value;
				___wealthBuildings -= Traverse.Create(watcher).Field<float>("wealthBuildings").Value;
				___wealthFloorsOnly -= Traverse.Create(watcher).Field<float>("wealthFloorsOnly").Value;
			}
		}

		[HarmonyPatch(typeof(WealthWatcher), "WealthItems", MethodType.Getter)]
		internal static class WealthItemsPatch
		{
			[HarmonyPrefix]
			public static bool WealthItems(WealthWatcher __instance, Map ___map, ref float __result, float ___lastCountTick)
			{
				float result = 0;
				foreach (var map in ZUtils.ZTracker.GetAllMaps(___map.Tile))
				{
					if (Find.TickManager.TicksGame - ___lastCountTick > 5000f)
					{
						map.wealthWatcher.ForceRecount();
					}
					var value = map.wealthWatcher.wealthItems;
					result += value;
					//ZLogger.Message("Analyzing wealthItems: " + map + " - value: " + value);
				}
				//ZLogger.Message("New result: " + result);
				__result = result;
				return false;
			}
		}

		[HarmonyPatch(typeof(WealthWatcher), "WealthBuildings", MethodType.Getter)]
		internal static class WealthBuildingsPatch
		{
			[HarmonyPrefix]
			public static bool WealthBuildings(WealthWatcher __instance, Map ___map, ref float __result, float ___lastCountTick)
			{
				float result = 0;
				foreach (var map in ZUtils.ZTracker.GetAllMaps(___map.Tile))
				{
					if (Find.TickManager.TicksGame - ___lastCountTick > 5000f)
					{
						map.wealthWatcher.ForceRecount();
					}
					var value = map.wealthWatcher.wealthBuildings;
					result += value;
					//ZLogger.Message("Analyzing wealthBuildings: " + map + " - value: " + value);
				}
				//ZLogger.Message("New result: " + result);
				__result = result;
				return false;
			}
		}

		[HarmonyPatch(typeof(WealthWatcher), "WealthFloorsOnly", MethodType.Getter)]
		internal static class WealthFloorsOnlyPatch
		{
			[HarmonyPrefix]
			public static bool WealthFloorsOnly(WealthWatcher __instance, Map ___map, ref float __result, float ___lastCountTick)
			{
				float result = 0;
				foreach (var map in ZUtils.ZTracker.GetAllMaps(___map.Tile))
				{
					if ((float)Find.TickManager.TicksGame - ___lastCountTick > 5000f)
					{
						map.wealthWatcher.ForceRecount();
					}
					var value = map.wealthWatcher.wealthFloorsOnly;
					result += value;
					//ZLogger.Message("Analyzing wealthFloorsOnly: " + map + " - value: " + value);
				}
				//ZLogger.Message("New result: " + result);
				__result = result;
				return false;
			}
		}

		[HarmonyPatch(typeof(WealthWatcher), "WealthPawns", MethodType.Getter)]
		internal static class WealthPawnsPatch
		{
			[HarmonyPrefix]
			public static bool WealthPawns(WealthWatcher __instance, Map ___map, ref float __result, float ___lastCountTick)
			{
				float result = 0;
				foreach (var map in ZUtils.ZTracker.GetAllMaps(___map.Tile))
				{
					if ((float)Find.TickManager.TicksGame - ___lastCountTick > 5000f)
					{
						map.wealthWatcher.ForceRecount();
					}
					var value = map.wealthWatcher.wealthPawns;
					result += value;
					//ZLogger.Message("Analyzing wealthPawns: " + map + " - value: " + value);
				}
				//ZLogger.Message("New result: " + result);
				__result = result;
				return false;
			}
		}

		[HarmonyPatch(typeof(WealthWatcher), "WealthTotal", MethodType.Getter)]
		internal static class WealthTotalPatch
		{
			[HarmonyPrefix]
			public static bool WealthTotal(WealthWatcher __instance, Map ___map, ref float __result, float ___lastCountTick)
			{
				float result = 0;
				foreach (var map in ZUtils.ZTracker.GetAllMaps(___map.Tile))
				{
					if ((float)Find.TickManager.TicksGame - ___lastCountTick > 5000f)
					{
						map.wealthWatcher.ForceRecount();
					}
					var value = map.wealthWatcher.wealthItems + map.wealthWatcher.wealthBuildings + map.wealthWatcher.wealthPawns;
					result += value;
					//ZLogger.Message("Analyzing WealthTotal: " + map + " - value: " + value);
				}
				//ZLogger.Message("New result: " + result);
				__result = result;
				return false;
			}
		}
	}
}
