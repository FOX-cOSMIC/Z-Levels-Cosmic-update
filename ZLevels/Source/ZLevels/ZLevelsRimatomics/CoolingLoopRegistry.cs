using System;
using System.Collections.Generic;
using Verse;
using ZLevels;

namespace ZLevelsRimatomics
{
    // Pairs cooling columns across adjacent levels. A column joins the registry when it spawns with a Rimatomics pipe, and it leaves when
    // it despawns. Register also prunes the entries that are dead for good: a column that is no longer spawned, a disposed map, or another game.
    // Each entry keeps the game that its column registered in. A column is usable only while it stands in its map's thing grid
    // at its own cell. ZUtils.TeleportThing moves a column to another map with no spawn, despawn, or grid update, so the grid check is what
    // tells a moved column apart. Loops come from a breadth-first search at call time, so the order in which columns register does not
    // change them.
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

            // Set when the grid check warns for this entry. The next check that finds the column listed in its grid clears it, so each off-grid episode warns once.
            public bool GridWarned { get; set; }
        }

        private static readonly List<Entry> entries = new List<Entry>();

        // Drops the entries that are dead for good: the column is no longer spawned, its map is disposed, or it registered in another game. An
        // entry for a spawned column that is off its grid stays, because that column can stand in its grid again. Then the column is listed under
        // the current game, unless an entry already holds it. A column that cannot register logs an error before it returns, so no return is silent.
        public static void Register(CompCoolingColumn column)
        {
            entries.RemoveAll(DeadForGood);
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

        // True when the column is listed and usable: spawned, on a map that is not disposed, in that map's thing grid at its cell, and registered
        // for the current game. A column with no pipe never registers, so this is false for it.
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

        // An entry is dead for good when its column is no longer spawned, when its map is disposed, or when it registered in a game that is not the
        // current game. A column on a removed map is not spawned, so the spawn check is what drops it. A moved column stays spawned and keeps its entry.
        private static bool DeadForGood(Entry entry)
        {
            Thing thing = entry.Column.parent;
            if (thing == null || !thing.Spawned) return true;
            Map map = thing.Map;
            return map == null || map.Disposed || entry.Game != Current.Game;
        }

        // A column is usable when it is spawned on a map that is not disposed, it stands in that map's thing grid at its own cell, and its entry
        // registered in the current game. The spawn and map checks cover a despawned column and a removed map. The game check covers an entry
        // left over from an earlier game. The grid check covers a column that ZUtils.TeleportThing moved, because that move sets only the map and the position.
        private static bool Usable(Entry entry)
        {
            CompCoolingColumn column = entry.Column;
            if (column == null || column.parent == null || !column.parent.Spawned) return false;
            Map map = column.parent.Map;
            if (map == null || map.Disposed || entry.Game != Current.Game) return false;
            if (ListedInGrid(column.parent))
            {
                entry.GridWarned = false;
                return true;
            }
            WarnOffGridOnce(entry);
            return false;
        }

        // The thing grid lists a one-cell thing at its position. ThingsAt returns nothing for a cell outside the map, where ThingsListAt would log an error.
        private static bool ListedInGrid(Thing thing)
        {
            foreach (Thing listed in thing.Map.thingGrid.ThingsAt(thing.Position))
            {
                if (listed == thing) return true;
            }
            return false;
        }

        private static void WarnOffGridOnce(Entry entry)
        {
            if (entry.GridWarned) return;
            entry.GridWarned = true;
            Log.Warning("Z-Levels cooling column " + (entry.Column.parent.def?.defName ?? "cooling column") + " is spawned, but it is not in its map's thing grid at its cell. It does not pair until it is.");
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
