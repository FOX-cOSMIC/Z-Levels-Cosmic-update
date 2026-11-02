using System;
using System.Collections.Generic;
using Verse;
using ZLevels;

namespace ZLevelsRimatomics
{
    // Pairs cooling columns across adjacent levels. A column joins the registry when it spawns with a Rimatomics pipe, and it leaves when
    // it despawns. Each entry keeps the game that its column registered in. ZUtils.TeleportThing moves a column to another map without a
    // spawn or a despawn, so an entry holds no map. Loops are found by a breadth-first search at call time, so a column loaded before its
    // neighbor's map exists still pairs.
    public static class CoolingLoopRegistry
    {
        private sealed class Entry
        {
            public Entry(CompCoolingColumn column, Verse.Game game)
            {
                Column = column;
                Game = game;
            }

            public CompCoolingColumn Column { get; }
            public Verse.Game Game { get; }
        }

        private static readonly List<Entry> entries = new List<Entry>();

        // Drops only the entries that are no longer usable for good. Then the column is listed under the current game, unless an entry already
        // holds it. A column that cannot register logs an error before it returns, so no return is silent.
        public static void Register(CompCoolingColumn column)
        {
            entries.RemoveAll(entry => !Usable(entry));
            if (column == null)
            {
                Log.Error("Z-Levels cooling column registration received no column");
                return;
            }
            string name = column.parent?.def?.defName ?? "cooling column";
            if (column.parent == null || !column.parent.Spawned || column.parent.Map == null)
            {
                Log.Error("Z-Levels cooling column " + name + " is not registered: it has no map");
                return;
            }
            if (Current.Game == null)
            {
                Log.Error("Z-Levels cooling column " + name + " is not registered: there is no current game");
                return;
            }
            if (!entries.Exists(entry => entry.Column == column)) entries.Add(new Entry(column, Current.Game));
        }

        public static void Deregister(CompCoolingColumn column)
        {
            entries.RemoveAll(entry => entry.Column == column);
        }

        // True when the column is listed and usable. A column with no pipe never registers, so this is false for it.
        public static bool IsRegistered(CompCoolingColumn column)
        {
            return column != null && entries.Exists(entry => entry.Column == column && Usable(entry));
        }

        // b sits on the level directly below or above a, on the same cell as a (ADR-0005). ConnectedPowerNets keeps its wider rule for power.
        // Both directions must hold, so Paired(a, b) and Paired(b, a) always agree. It returns false unless both columns are registered.
        // It throws when both are registered but the game has no ZLevelsManager, because a live game always has one.
        public static bool Paired(CompCoolingColumn a, CompCoolingColumn b)
        {
            if (!IsRegistered(a) || !IsRegistered(b)) return false;
            ZLevelsManager tracker = Current.Game?.GetComponent<ZLevelsManager>();
            if (tracker == null) throw new InvalidOperationException("ZLevelsManager is missing from the current game");
            return SitsOnSameCellOfAdjacentLevel(tracker, a, b) && SitsOnSameCellOfAdjacentLevel(tracker, b, a);
        }

        // The connected group of paired columns that contains start, found by a breadth-first search at call time. The group includes start
        // when start is registered. An unregistered start returns an empty list, so a caller that reads the loop must check that start is in it.
        public static List<CompCoolingColumn> LoopOf(CompCoolingColumn start)
        {
            var loop = new List<CompCoolingColumn>();
            if (!IsRegistered(start)) return loop;
            loop.Add(start);
            for (int i = 0; i < loop.Count; i++)
            {
                CompCoolingColumn current = loop[i];
                foreach (Entry entry in entries)
                {
                    if (!loop.Contains(entry.Column) && Paired(current, entry.Column)) loop.Add(entry.Column);
                }
            }
            return loop;
        }

        // A column is usable when it is spawned, its map is not disposed, and it registered in the current game. The map is read at each call,
        // so a column that TeleportThing moved to another map is still usable. Thing.Spawned and Thing.Map catch a column whose map was removed,
        // and the game check catches a column left over from an earlier game.
        private static bool Usable(Entry entry)
        {
            CompCoolingColumn column = entry.Column;
            if (column == null || column.parent == null || !column.parent.Spawned || column.parent.Map == null) return false;
            return !column.parent.Map.Disposed && entry.Game == Current.Game;
        }

        private static bool SitsOnSameCellOfAdjacentLevel(ZLevelsManager tracker, CompCoolingColumn a, CompCoolingColumn b)
        {
            Map lower = tracker.GetLowerLevel(a.parent.Tile, a.parent.Map);
            Map upper = tracker.GetUpperLevel(a.parent.Tile, a.parent.Map);
            if (b.parent.Map != lower && b.parent.Map != upper) return false;
            return a.parent.Position == b.parent.Position;
        }
    }
}
