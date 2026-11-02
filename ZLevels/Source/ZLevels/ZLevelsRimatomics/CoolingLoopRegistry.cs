using System;
using System.Collections.Generic;
using Verse;
using ZLevels;

namespace ZLevelsRimatomics
{
    // Pairs cooling columns across adjacent levels. Each loop is found by a breadth-first search at call time, so a column loaded
    // before its neighbor's map exists still pairs. The list is static, and the game does not always call PostDeSpawn when a map is
    // removed, so an entry can outlive its map. Usable rejects such an entry, and Register drops every unusable entry before it adds one.
    public static class CoolingLoopRegistry
    {
        private static readonly List<CompCoolingColumn> columns = new List<CompCoolingColumn>();

        public static void Register(CompCoolingColumn column)
        {
            columns.RemoveAll(entry => !Usable(entry));
            if (column != null && !columns.Contains(column)) columns.Add(column);
        }

        public static void Deregister(CompCoolingColumn column)
        {
            columns.Remove(column);
        }

        // The rule of ConnectedPowerNets.RegisterTransmitter: b sits on the level directly below or above a, and the two are less than 3 cells apart.
        // Both directions must hold, so Paired(a, b) and Paired(b, a) always agree. It returns false when either column is not usable.
        // It throws when both are usable but the game has no ZLevelsManager, because a live game always has one.
        public static bool Paired(CompCoolingColumn a, CompCoolingColumn b)
        {
            if (!Usable(a) || !Usable(b)) return false;
            ZLevelsManager tracker = Current.Game?.GetComponent<ZLevelsManager>();
            if (tracker == null) throw new InvalidOperationException("ZLevelsManager is missing from the current game");
            return SitsWithin3OnAdjacentLevel(tracker, a, b) && SitsWithin3OnAdjacentLevel(tracker, b, a);
        }

        // The connected group of paired columns that contains start, found by a breadth-first search at call time. The group includes start
        // when start is usable. An unusable start returns an empty list, so a caller that reads the loop must check that start is in it.
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

        // Thing.Spawned is false, and Thing.Map is null, once Notify_MyMapRemoved has set mapIndexOrState below zero. The Disposed check
        // covers a map that is disposed while its things still report as spawned.
        private static bool Usable(CompCoolingColumn column)
        {
            return column != null && column.parent != null && column.parent.Spawned && column.parent.Map != null && !column.parent.Map.Disposed;
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
