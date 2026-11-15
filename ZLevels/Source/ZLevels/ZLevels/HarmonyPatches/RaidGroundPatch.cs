using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZLevels
{
    // Idea 3 (raids land on the ground). A raid aimed at a lower map has no arrival cell there: the walk-in finds no spawn center underground, so
    // the raid does not fire. The raid is aimed at the surface of the same tile instead, where the colonists stand. The faction, points, strategy
    // and arrival mode are kept. A raid on the surface, or on a pocket map, is unchanged.
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    internal class Patch_RaidEnemy_TryExecuteWorker
    {
        [HarmonyPrefix]
        private static void PreFix(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null || map.IsPocketMap)
            {
                return;
            }
            var tracker = ZUtils.ZTracker;
            if (tracker == null || tracker.GetZIndexFor(map) == 0)
            {
                return;
            }
            Map surface;
            try
            {
                surface = tracker.GetMapByIndex(map.Tile, 0);
            }
            catch (Exception exception)
            {
                ZLogger.Error("A raid aimed at " + map + " found no surface map on its tile: " + exception.Message);
                return;
            }
            if (surface == null)
            {
                ZLogger.Error("A raid aimed at " + map + " found no surface map on its tile.");
                return;
            }
            ZLogger.Message("A raid aimed at " + map + " is aimed at the surface " + surface + " instead.");
            parms.target = surface;
        }
    }
}
