using System;
using System.Collections.Generic;
using Verse;
using ZLevels;

namespace ZLevelsRimatomics
{
    // Pairs cooling columns across adjacent levels. A column joins the registry when it spawns with a Rimatomics pipe, and it leaves when
    // it despawns. Each entry keeps the game and the map that its column registered on, and an entry is usable only while the column is
    // still spawned on that map in that game. Loops are found by a breadth-first search at call time, so a column loaded before its
    // neighbor's map exists still pairs.
    public static class CoolingLoopRegistry
    {
        private sealed class Entry
        {
            public Entry(CompCoolingColumn column, Verse.Game game, Map map)
            {
                Column = column;
                Game = game;
                Map = map;
            }

            public CompCoolingColumn Column { get; }
            public Verse.Game Game { get; }
            public Map Map { get; }
        }

        private static readonly List<Entry> entries = new List<Entry>();

        // Drops every entry that is no longer usable, and any earlier entry for this column. Then the column is added with the current game
        // and the map it spawned on. A column that is not spawned on a map is not added.
        public static void Register(CompCoolingColumn column)
        {
            entries.RemoveAll(entry => !Usable(entry) || entry.Column == column);
            if (column == null || column.parent == null || !column.parent.Spawned || column.parent.Map == null) return;
            entries.Add(new Entry(column, Current.Game, column.parent.Map));
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

        // Thing.Spawned is false, and Thing.Map is null, once Notify_MyMapRemoved has set mapIndexOrState below zero. The Disposed check
        // covers a map that is disposed while its things still report as spawned. The stored map and game catch a column left over from an
        // earlier game, or a column that now stands on another map, which Thing.Map alone does not catch.
        private static bool Usable(Entry entry)
        {
            CompCoolingColumn column = entry.Column;
            if (column == null || column.parent == null || !column.parent.Spawned || column.parent.Map == null) return false;
            return !column.parent.Map.Disposed && column.parent.Map == entry.Map && entry.Game == Current.Game;
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
