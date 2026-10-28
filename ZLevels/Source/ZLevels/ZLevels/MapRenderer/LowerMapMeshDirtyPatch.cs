using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZLevels
{
    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.MapMeshDirty), new[] { typeof(IntVec3), typeof(ulong), typeof(bool), typeof(bool) })]
    public static class LowerMapMeshDirtyPatch
    {
        [ThreadStatic] private static bool propagating;

        public static void Postfix(Map ___map, IntVec3 loc, ulong dirtyFlags, bool regenAdjacentCells, bool regenAdjacentSections)
        {
            if (propagating || Current.ProgramState != ProgramState.Playing || Current.Game == null || ___map.Disposed)
            {
                return;
            }
            ulong relevantFlags = (ulong)MapMeshFlagDefOf.Things | (ulong)MapMeshFlagDefOf.Terrain | (ulong)MapMeshFlagDefOf.FogOfWar | (ulong)MapMeshFlagDefOf.Snow | (ulong)MapMeshFlagDefOf.Buildings;
            if ((dirtyFlags & relevantFlags) == 0)
            {
                return;
            }
            var tracker = ZUtils.ZTracker;
            if (tracker == null || tracker.ZLevelsTracker == null || !tracker.ZLevelsTracker.ContainsKey(___map.Tile))
            {
                return;
            }
            int level = tracker.GetZIndexFor(___map);
            if (level < 0)
            {
                return;
            }
            try
            {
                propagating = true;
                ulong lowerFlags = dirtyFlags | (ulong)MapMeshFlagDefOf.Things | (ulong)MapMeshFlagDefOf.Terrain | (ulong)MapMeshFlagDefOf.FogOfWar;
                foreach (var upperMap in tracker.GetAllMaps(___map.Tile))
                {
                    if (!upperMap.Disposed && tracker.GetZIndexFor(upperMap) > level && loc.InBounds(upperMap))
                    {
                        upperMap.mapDrawer.MapMeshDirty(loc, lowerFlags, regenAdjacentCells, regenAdjacentSections);
                    }
                }
            }
            finally
            {
                propagating = false;
            }
        }
    }
}
