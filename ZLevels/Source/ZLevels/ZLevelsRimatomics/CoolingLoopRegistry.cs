using System;
using System.Collections.Generic;
using Verse;
using ZLevels;

namespace ZLevelsRimatomics
{
    // One cell that the partner highlight outlines on the viewed level. Paired is true when the viewed level also holds a usable cooling column there.
    public struct CoolingTarget
    {
        public readonly IntVec3 Cell;
        public readonly bool Paired;

        public CoolingTarget(IntVec3 cell, bool paired)
        {
            Cell = cell;
            Paired = paired;
        }
    }

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

            // Set when the grid check warns for this entry. The next query that sees the column listed in its grid clears it, so the warning is
            // once per episode that a query sees.
            public bool GridWarned { get; set; }
        }

        private static readonly List<Entry> entries = new List<Entry>();

        // Removes the entries that are dead for good, and logs each removal with its reason. Then the column is listed under the current game,
        // unless an entry already holds it. A column that cannot register logs an error before it returns, so no return is silent.
        public static void Register(CompCoolingColumn column)
        {
            PruneDeadEntries();
            if (column == null)
            {
                Log.Error("Z-Levels cooling column registration received no column");
                return;
            }
            string name = column.parent?.def?.defName ?? "cooling column";
            if (column.parent == null || !column.parent.Spawned)
            {
                Log.Error("Z-Levels cooling column " + name + " is not registered: it is not spawned");
                return;
            }
            if (column.parent.Map == null)
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
        // The usable columns of this moment, as a set. A query that tests many pairs reads the set once and passes it to the overloads below, so the grid
        // check runs once per entry and not once per pair. A set read before a change is stale, so each query reads its own.
        public static HashSet<CompCoolingColumn> UsableColumns()
        {
            var usable = new HashSet<CompCoolingColumn>();
            foreach (Entry entry in entries)
            {
                if (Usable(entry)) usable.Add(entry.Column);
            }
            return usable;
        }

        // The ZLevelsManager of the current game. It throws when there is none, because a live game always has one.
        public static ZLevelsManager TrackerOf()
        {
            ZLevelsManager tracker = Current.Game?.GetComponent<ZLevelsManager>();
            if (tracker == null) throw new InvalidOperationException("ZLevelsManager is missing from the current game");
            return tracker;
        }

        // Paired(a, b) for this moment. It reads the usable set and the tracker itself.
        public static bool Paired(CompCoolingColumn a, CompCoolingColumn b)
        {
            HashSet<CompCoolingColumn> usable = UsableColumns();
            if (!usable.Contains(a) || !usable.Contains(b)) return false;
            return Paired(a, b, usable, TrackerOf());
        }

        // Paired(a, b) with the usable set and the tracker passed in, so one tick does not repeat the grid check or the tracker lookup for each pair.
        public static bool Paired(CompCoolingColumn a, CompCoolingColumn b, HashSet<CompCoolingColumn> usable, ZLevelsManager tracker)
        {
            if (!usable.Contains(a) || !usable.Contains(b)) return false;
            return SitsOnSameCellOfAdjacentLevel(tracker, a, b) && SitsOnSameCellOfAdjacentLevel(tracker, b, a);
        }

        // The connected group of paired columns that contains start, found by a breadth-first search at call time. The group includes start
        // when start is registered. An unregistered start returns an empty list, so a caller that reads the loop must check that start is in it.
        public static List<CompCoolingColumn> LoopOf(CompCoolingColumn start)
        {
            HashSet<CompCoolingColumn> usable = UsableColumns();
            if (!usable.Contains(start)) return new List<CompCoolingColumn>();
            return LoopOf(start, usable, TrackerOf());
        }

        // LoopOf with the usable set and the tracker passed in (see Paired). The group includes start when start is in the usable set.
        public static List<CompCoolingColumn> LoopOf(CompCoolingColumn start, HashSet<CompCoolingColumn> usable, ZLevelsManager tracker)
        {
            var loop = new List<CompCoolingColumn>();
            if (!usable.Contains(start)) return loop;
            loop.Add(start);
            for (int i = 0; i < loop.Count; i++)
            {
                CompCoolingColumn current = loop[i];
                foreach (Entry entry in entries)
                {
                    if (!loop.Contains(entry.Column) && Paired(current, entry.Column, usable, tracker)) loop.Add(entry.Column);
                }
            }
            return loop;
        }

        // The cells the partner highlight outlines on the viewed level: each cell where a usable cooling column stands on the level directly above or
        // below it. Paired is true when the viewed level also holds a usable column on that cell. Each cell is listed once. It throws when the game
        // has no ZLevelsManager, as Paired does.
        public static List<CoolingTarget> HighlightTargets(Map viewed)
        {
            var targets = new List<CoolingTarget>();
            if (viewed == null || viewed.Disposed || Current.Game == null) return targets;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            if (tracker == null) throw new InvalidOperationException("ZLevelsManager is missing from the current game");
            Map lower = tracker.GetLowerLevel(viewed.Tile, viewed);
            Map upper = tracker.GetUpperLevel(viewed.Tile, viewed);
            foreach (Entry entry in entries)
            {
                if (!Usable(entry)) continue;
                Thing thing = entry.Column.parent;
                if (thing.Map != lower && thing.Map != upper) continue;
                IntVec3 cell = thing.Position;
                if (targets.Exists(target => target.Cell == cell)) continue;
                targets.Add(new CoolingTarget(cell, HasUsableColumnAt(viewed, cell)));
            }
            return targets;
        }

        // True when a usable registered column stands on the cell of the map.
        private static bool HasUsableColumnAt(Map map, IntVec3 cell)
        {
            foreach (Entry entry in entries)
            {
                if (!Usable(entry)) continue;
                Thing thing = entry.Column.parent;
                if (thing.Map == map && thing.Position == cell) return true;
            }
            return false;
        }

        // Removes each entry that is dead for good. Each removal logs one warning that names the column and the reason, so a column that leaves
        // the registry leaves a trace in the log. Entries are visited from the end, so a removal does not skip the entry after it.
        private static void PruneDeadEntries()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                string reason = DeadReason(entries[i]);
                if (reason == null) continue;
                // The entry leaves the registry before the warning is logged, so a logger that throws cannot leave the entry behind.
                string column = Describe(entries[i].Column);
                entries.RemoveAt(i);
                Log.Warning("Z-Levels cooling column " + column + " left the cooling registry: " + reason + ".");
            }
        }

        // The reason an entry is dead for good, or null when it is alive. A column that is not spawned, or has no map, is named as not spawned.
        // A column on a removed map is not spawned, so the spawn check drops it. A moved column stays spawned, so it keeps its entry.
        private static string DeadReason(Entry entry)
        {
            Thing thing = entry.Column?.parent;
            if (thing == null || !thing.Spawned || thing.Map == null) return "not spawned";
            if (thing.Map.Disposed) return "map disposed";
            if (entry.Game != Current.Game) return "another game";
            return null;
        }

        // The def and cell of a column, for a log line.
        private static string Describe(CompCoolingColumn column)
        {
            Thing thing = column?.parent;
            if (thing == null) return "cooling column";
            return thing.def.defName + " at " + thing.Position;
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
