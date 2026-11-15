using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZLevels
{
    // Idea 4 (leaving means walking to the ground first), DM9. A pawn on a lower or upper floor walks to the stairs toward the surface, changes
    // floor, and leaves by the surface exit. On a level map the exit search finds no spot: the level's edges are rock, so no edge cell can be
    // reached. A pawn on the surface, or on a pocket map, is unchanged.
    internal static class LeaveByStairs
    {
        // The walk toward the surface, as a job. Returns false when the pawn is not on a level map, or when no stairs lead toward the surface there;
        // the caller then keeps the vanilla leave job.
        internal static bool TryRouteToSurface(Pawn pawn, out Job job)
        {
            job = null;
            Map map = pawn.MapHeld;
            ZLevelsManager tracker = ZUtils.ZTracker;
            if (map == null || tracker == null || map.IsPocketMap || (pawn.Downed && !pawn.Crawling))
            {
                return false;
            }
            int index = tracker.GetZIndexFor(map);
            if (index == 0)
            {
                return false;
            }
            List<Building_Stairs> stairs = index < 0 ? tracker.GetStairsUpFor(map) : tracker.GetStairsDownFor(map);
            if (stairs == null || stairs.Count == 0)
            {
                ZLogger.Message(pawn + " has no stairs toward the surface on " + map + "; its leave job is unchanged");
                return false;
            }
            Map surface = tracker.GetMapByIndex(map.Tile, 0);
            if (surface == null)
            {
                ZLogger.Error("A pawn on " + map + " found no surface map on its tile; its leave job is unchanged");
                return false;
            }
            tracker.ResetJobTrackerFor(pawn);
            tracker.jobTracker[pawn].targetDest = new TargetInfo(surface.Center, surface);
            job = JobMaker.MakeJob(ZLevelsDefOf.ZL_GoToMap);
            return true;
        }
    }

    // Every exit job the think tree asks for goes through JobGiver_ExitMap.TryGiveJob. The follow-overseer giver calls this base method too.
    [HarmonyPatch(typeof(JobGiver_ExitMap), "TryGiveJob")]
    internal class Patch_JobGiver_ExitMap_TryGiveJob
    {
        [HarmonyPrefix]
        private static bool PreFix(Pawn pawn, ref Job __result)
        {
            if (LeaveByStairs.TryRouteToSurface(pawn, out Job job))
            {
                __result = job;
                return false;
            }
            return true;
        }
    }
}
