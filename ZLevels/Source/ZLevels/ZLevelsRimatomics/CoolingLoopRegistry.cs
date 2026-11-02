using System.Collections.Generic;
using Verse;
using ZLevels;

namespace ZLevelsRimatomics
{
    // Pairs cooling columns across adjacent levels. Each loop is found by a breadth-first search at call time, so a column loaded
    // before its neighbour's map exists still pairs. The list is static and can hold stale entries, so every query skips a column
    // that is not spawned or has no map.
    public static class CoolingLoopRegistry
    {
        private static readonly List<CompCoolingColumn> columns = new List<CompCoolingColumn>();

        public static void Register(CompCoolingColumn column)
        {
            if (column != null && !columns.Contains(column)) columns.Add(column);
        }

        public static void Deregister(CompCoolingColumn column)
        {
            columns.Remove(column);
        }

        // The rule of ConnectedPowerNets.RegisterTransmitter: b sits on the level directly below or above a, and the two are less than 3 cells apart.
        // Both directions must hold, so Paired(a, b) and Paired(b, a) always agree.
        public static bool Paired(CompCoolingColumn a, CompCoolingColumn b)
        {
            ZLevelsManager tracker = Current.Game?.GetComponent<ZLevelsManager>();
            if (tracker == null || !Usable(a) || !Usable(b)) return false;
            return SitsWithin3OnAdjacentLevel(tracker, a, b) && SitsWithin3OnAdjacentLevel(tracker, b, a);
        }

        // The connected group of paired columns that contains start. The group includes start when start is usable.
        public static List<CompCoolingColumn> LoopOf(CompCoolingColumn start)
        {
            var loop = new List<CompCoolingColumn>();
            if (!Usable(start)) return loop;
            loop.Add(start);
            for (int i = 0; i < loop.Count; i++)
            {
                CompCoolingColumn current = loop[i];
                foreach (CompCoolingColumn other in columns)
                {
                    if (!loop.Contains(other) && Paired(current, other)) loop.Add(other);
                }
            }
            return loop;
        }

        private static bool Usable(CompCoolingColumn column)
        {
            return column != null && column.parent != null && column.parent.Spawned && column.parent.Map != null;
        }

        private static bool SitsWithin3OnAdjacentLevel(ZLevelsManager tracker, CompCoolingColumn a, CompCoolingColumn b)
        {
            Map lower = tracker.GetLowerLevel(a.parent.Tile, a.parent.Map);
            Map upper = tracker.GetUpperLevel(a.parent.Tile, a.parent.Map);
            if (b.parent.Map != lower && b.parent.Map != upper) return false;
            return a.parent.Position.DistanceTo(b.parent.Position) < 3;
        }
    }
}
