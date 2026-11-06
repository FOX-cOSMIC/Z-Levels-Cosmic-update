using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ZLevels
{
    // The needs whose search can be set to one floor (floor settings, Task 29). Meditation is a psycaster's need.
    public enum NeedKind
    {
        Sleep,
        Food,
        Fun,
        Meditation
    }

    // Per-colonist floor rules. A lock keeps a colonist's job search to one floor. The lock stores the floor's Z index, not the Map,
    // because a Map is not saved as an object. ZLevelsManager saves the locks with the game.
    public class FloorPolicy
    {
        // Test hook (debug). While it is true, a cross-floor search fails once it has moved a colonist to another floor. Only the
        // -FloorGuard scenario sets it, and the scenario sets it back to false when its call ends (idea "always move the colonist back").
        public static bool ThrowInSearchForTest;

        // Test counter (debug): how many searches have gone through the floor filter (Allowed), which every colonist search in JobPatches uses.
        // The -OneFloorColony scenario reads it. A colony with one floor must add nothing to it.
        public static int CrossFloorSearches;

        // Test counter (debug): how many work searches the copied TryIssueJobPackage runs on a floor other than the colonist's own. The
        // -OwnFloorFirst scenario reads it. The own-floor-first change must leave it at zero when the own floor has work.
        public static int OtherFloorSearchRuns;

        // The search iterators call this right after they move a thing to a floor. It throws only when the hook is on and the floor is not
        // the thing's own floor, so the colonist is away from its floor when the search fails.
        public static void FailSearchIfTesting(Map ownMap, Map map)
        {
            if (ThrowInSearchForTest && map != ownMap)
            {
                throw new InvalidOperationException("Z-Levels floor guard test: the search failed on another floor");
            }
        }

        private readonly Dictionary<Pawn, int> lockedFloor = new Dictionary<Pawn, int>();
        private readonly Dictionary<Pawn, int> lockedUntil = new Dictionary<Pawn, int>();

        // Scribe fills the pawn list only during ResolvingCrossRefs, after the local variables of ExposeLocks are gone. The lists are kept
        // in fields until PostLoadInit.
        private List<Pawn> savedPawns;
        private List<int> savedFloors;
        private List<int> savedUntils;

        // untilTick == int.MaxValue keeps the lock until ClearLock.
        public void SetLock(Pawn pawn, int floorIndex, int untilTick)
        {
            lockedFloor[pawn] = floorIndex;
            lockedUntil[pawn] = untilTick;
        }

        public void ClearLock(Pawn pawn)
        {
            lockedFloor.Remove(pawn);
            lockedUntil.Remove(pawn);
        }

        public int Count => lockedFloor.Count;

        public bool HasLock(Pawn pawn)
        {
            return pawn != null && lockedFloor.ContainsKey(pawn);
        }

        // The floor index the colonist is locked to. Null when there is no lock, or when the lock has run out.
        public int? LockedFloorIndex(Pawn pawn, int nowTick)
        {
            if (pawn == null || !lockedFloor.TryGetValue(pawn, out int floorIndex))
            {
                return null;
            }
            if (nowTick >= lockedUntil[pawn])
            {
                ClearLock(pawn);
                return null;
            }
            return floorIndex;
        }

        // A locked colonist may search only the map of its locked floor. An unlocked colonist may search every map.
        public bool AllowsMap(Pawn pawn, Map map, int nowTick)
        {
            int? floorIndex = LockedFloorIndex(pawn, nowTick);
            if (floorIndex == null)
            {
                return true;
            }
            return ZUtils.ZTracker.GetZIndexFor(map) == floorIndex.Value;
        }

        // The maps a pawn may search, in the order given. A null pawn has no lock, so it keeps every map.
        public IEnumerable<Map> Allowed(Pawn pawn, IEnumerable<Map> maps)
        {
            CrossFloorSearches++;
            if (pawn == null)
            {
                return maps;
            }
            int nowTick = Find.TickManager.TicksGame;
            return maps.Where(map => AllowsMap(pawn, map, nowTick));
        }

        // Needs cool-down (DM5). A colonist is redirected for food or fun at most once per RedirectCooldownTicks, and food and fun share
        // that clock. A very hungry colonist is never held back. The times are kept in memory only, so a load starts with no cool-down.
        public const int RedirectCooldownTicks = 2000;
        private readonly Dictionary<Pawn, int> lastRedirectTick = new Dictionary<Pawn, int>();

        public bool RedirectAllowed(Pawn pawn, int nowTick, bool urgent)
        {
            if (urgent || pawn == null || !lastRedirectTick.TryGetValue(pawn, out int lastTick))
            {
                return true;
            }
            return nowTick - lastTick >= RedirectCooldownTicks;
        }

        public void NoteRedirect(Pawn pawn, int nowTick)
        {
            if (pawn == null)
            {
                return;
            }
            // A time older than the cool-down allows every redirect anyway, so it is dropped here. Dead pawns do not stay in the table.
            List<Pawn> expired = lastRedirectTick.Where(entry => nowTick - entry.Value >= RedirectCooldownTicks).Select(entry => entry.Key).ToList();
            foreach (Pawn oldPawn in expired)
            {
                lastRedirectTick.Remove(oldPawn);
            }
            lastRedirectTick[pawn] = nowTick;
        }

        // Idle search pause (DM6). SearchCount counts the work searches a colonist runs, in memory only. The key is the thingIDNumber, so a
        // dead colonist is not held through a reference.
        private readonly Dictionary<int, int> searchCounts = new Dictionary<int, int>();

        public void CountSearch(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }
            searchCounts.TryGetValue(pawn.thingIDNumber, out int count);
            searchCounts[pawn.thingIDNumber] = count + 1;
        }

        public int SearchCount(Pawn pawn)
        {
            return pawn != null && searchCounts.TryGetValue(pawn.thingIDNumber, out int count) ? count : 0;
        }

        // The pause after a normal work search that finds nothing (DM6). The colonist may search again once the pause is over. The
        // emergency search is never paused: it is cheap and must not be starved by the normal search's pause.
        public const int IdlePauseTicks = 600;
        private readonly Dictionary<int, int> idlePausedUntil = new Dictionary<int, int>();

        public bool IdleSearchAllowed(Pawn pawn, int nowTick)
        {
            return pawn == null || !idlePausedUntil.TryGetValue(pawn.thingIDNumber, out int until) || nowTick >= until;
        }

        public void NoteIdleSearch(Pawn pawn, int nowTick)
        {
            if (pawn == null)
            {
                return;
            }
            idlePausedUntil[pawn.thingIDNumber] = nowTick + IdlePauseTicks;
        }

        // The floor each need's search uses, per colonist (floor settings, Task 29). A colonist with no entry for a need searches every floor.
        // The floor is kept as its Z index, as the locks keep theirs.
        private readonly Dictionary<Pawn, Dictionary<NeedKind, int>> needFloors = new Dictionary<Pawn, Dictionary<NeedKind, int>>();

        public void SetNeedFloor(Pawn pawn, NeedKind kind, int floorIndex)
        {
            if (pawn == null)
            {
                return;
            }
            if (!needFloors.TryGetValue(pawn, out Dictionary<NeedKind, int> byKind))
            {
                byKind = new Dictionary<NeedKind, int>();
                needFloors[pawn] = byKind;
            }
            byKind[kind] = floorIndex;
        }

        // "Any" for the need: the colonist's search for it covers every floor again.
        public void ClearNeedFloor(Pawn pawn, NeedKind kind)
        {
            if (pawn != null && needFloors.TryGetValue(pawn, out Dictionary<NeedKind, int> byKind))
            {
                byKind.Remove(kind);
                if (byKind.Count == 0)
                {
                    needFloors.Remove(pawn);
                }
            }
        }

        // The floor index the need is set to. Null when the need searches every floor.
        public int? NeedFloorFor(Pawn pawn, NeedKind kind)
        {
            if (pawn != null && needFloors.TryGetValue(pawn, out Dictionary<NeedKind, int> byKind) && byKind.TryGetValue(kind, out int floorIndex))
            {
                return floorIndex;
            }
            return null;
        }

        // True when the need may search this map: no floor is set for it, or the map is the chosen floor.
        public bool NeedAllowsMap(Pawn pawn, Map map, NeedKind kind)
        {
            int? floorIndex = NeedFloorFor(pawn, kind);
            return floorIndex == null || ZUtils.ZTracker.GetZIndexFor(map) == floorIndex.Value;
        }

        private List<Pawn> savedNeedPawns;
        private List<int> savedNeedKinds;
        private List<int> savedNeedFloors;

        // Saves the need floors with the game, as three parallel lists. Dead pawns are not saved. The lists are read back at PostLoadInit.
        public void ExposeNeedFloors()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedNeedPawns = new List<Pawn>();
                savedNeedKinds = new List<int>();
                savedNeedFloors = new List<int>();
                foreach (KeyValuePair<Pawn, Dictionary<NeedKind, int>> pawnEntry in needFloors)
                {
                    if (pawnEntry.Key.Dead)
                    {
                        continue;
                    }
                    foreach (KeyValuePair<NeedKind, int> needEntry in pawnEntry.Value)
                    {
                        savedNeedPawns.Add(pawnEntry.Key);
                        savedNeedKinds.Add((int)needEntry.Key);
                        savedNeedFloors.Add(needEntry.Value);
                    }
                }
            }
            Scribe_Collections.Look(ref savedNeedPawns, "needFloorPawns", LookMode.Reference);
            Scribe_Collections.Look(ref savedNeedKinds, "needFloorKinds", LookMode.Value);
            Scribe_Collections.Look(ref savedNeedFloors, "needFloorFloors", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                needFloors.Clear();
                if (savedNeedPawns != null && savedNeedKinds != null && savedNeedFloors != null)
                {
                    for (int i = 0; i < savedNeedPawns.Count && i < savedNeedKinds.Count && i < savedNeedFloors.Count; i++)
                    {
                        if (savedNeedPawns[i] != null && !savedNeedPawns[i].Dead)
                        {
                            SetNeedFloor(savedNeedPawns[i], (NeedKind)savedNeedKinds[i], savedNeedFloors[i]);
                        }
                    }
                }
                savedNeedPawns = null;
                savedNeedKinds = null;
                savedNeedFloors = null;
            }
        }

        // Work types that a floor does not do, keyed by the floor's Z index. A work type is allowed on a floor unless it is listed here, so a
        // floor with no entry does all work (floor work filter, idea 6 on docs/mod-comparison/ideas.html).
        private readonly Dictionary<int, HashSet<WorkTypeDef>> workOffFloors = new Dictionary<int, HashSet<WorkTypeDef>>();

        // Sets whether the floor does the work type. Allowed removes the listing; not allowed lists it.
        public void SetWorkAllowed(WorkTypeDef def, int floorIndex, bool allowed)
        {
            if (def == null)
            {
                return;
            }
            if (allowed)
            {
                if (workOffFloors.TryGetValue(floorIndex, out HashSet<WorkTypeDef> off))
                {
                    off.Remove(def);
                    if (off.Count == 0)
                    {
                        workOffFloors.Remove(floorIndex);
                    }
                }
                return;
            }
            if (!workOffFloors.TryGetValue(floorIndex, out HashSet<WorkTypeDef> listed))
            {
                listed = new HashSet<WorkTypeDef>();
                workOffFloors[floorIndex] = listed;
            }
            listed.Add(def);
        }

        // True when the floor does the work type. A work type with no definition is allowed.
        public bool WorkTypeAllowed(WorkTypeDef def, int floorIndex)
        {
            return def == null || !(workOffFloors.TryGetValue(floorIndex, out HashSet<WorkTypeDef> off) && off.Contains(def));
        }

        // The floor each work table is linked to. The ingredient search of the table's bills tries that floor first (idea "workbench floor link" on
        // docs/mod-comparison/ideas.html). A table with no link keeps the search order.
        private readonly Dictionary<Thing, int> benchFloors = new Dictionary<Thing, int>();

        // Links a work table to a floor, or removes the link when floorIndex is null.
        public void SetBenchFloor(Thing bench, int? floorIndex)
        {
            if (bench == null)
            {
                return;
            }
            if (floorIndex == null)
            {
                benchFloors.Remove(bench);
                return;
            }
            benchFloors[bench] = floorIndex.Value;
        }

        // The floor a work table is linked to, or null when it has no link.
        public int? BenchFloor(Thing bench)
        {
            return bench != null && benchFloors.TryGetValue(bench, out int floorIndex) ? floorIndex : (int?)null;
        }

        private List<Thing> savedBenches;
        private List<int> savedBenchFloors;

        // Saves the links as two parallel lists. A table is saved by reference, and a destroyed table is left out. The links are set again at
        // PostLoadInit, when the tables exist.
        public void ExposeBenchFloors()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedBenches = new List<Thing>();
                savedBenchFloors = new List<int>();
                foreach (KeyValuePair<Thing, int> entry in benchFloors)
                {
                    if (entry.Key.Destroyed)
                    {
                        continue;
                    }
                    savedBenches.Add(entry.Key);
                    savedBenchFloors.Add(entry.Value);
                }
            }
            Scribe_Collections.Look(ref savedBenches, "benchFloorTables", LookMode.Reference);
            Scribe_Collections.Look(ref savedBenchFloors, "benchFloorIndexes", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                benchFloors.Clear();
                if (savedBenches != null && savedBenchFloors != null)
                {
                    for (int i = 0; i < savedBenches.Count && i < savedBenchFloors.Count; i++)
                    {
                        if (savedBenches[i] != null && !savedBenches[i].Destroyed)
                        {
                            SetBenchFloor(savedBenches[i], savedBenchFloors[i]);
                        }
                    }
                }
                savedBenches = null;
                savedBenchFloors = null;
            }
        }

        private List<int> savedWorkFloors;
        private List<string> savedWorkDefNames;

        // Saves the work types each floor does not do, as two parallel lists. The work types are saved by name, since a def is not a saved
        // object. The lists are read back at PostLoadInit, when the defs are loaded.
        public void ExposeWorkOff()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedWorkFloors = new List<int>();
                savedWorkDefNames = new List<string>();
                foreach (KeyValuePair<int, HashSet<WorkTypeDef>> floorEntry in workOffFloors)
                {
                    foreach (WorkTypeDef def in floorEntry.Value)
                    {
                        savedWorkFloors.Add(floorEntry.Key);
                        savedWorkDefNames.Add(def.defName);
                    }
                }
            }
            Scribe_Collections.Look(ref savedWorkFloors, "workOffFloorIndexes", LookMode.Value);
            Scribe_Collections.Look(ref savedWorkDefNames, "workOffWorkTypes", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                workOffFloors.Clear();
                if (savedWorkFloors != null && savedWorkDefNames != null)
                {
                    for (int i = 0; i < savedWorkFloors.Count && i < savedWorkDefNames.Count; i++)
                    {
                        WorkTypeDef def = DefDatabase<WorkTypeDef>.GetNamedSilentFail(savedWorkDefNames[i]);
                        if (def != null)
                        {
                            SetWorkAllowed(def, savedWorkFloors[i], false);
                        }
                    }
                }
                savedWorkFloors = null;
                savedWorkDefNames = null;
            }
        }

        // Saves the locks with the game, as parallel lists like the other per-pawn data. Dead pawns are not saved. The lists are
        // read back at PostLoadInit, when the pawn references are resolved.
        public void ExposeLocks()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedPawns = lockedFloor.Keys.Where(pawn => !pawn.Dead).ToList();
                savedFloors = savedPawns.Select(pawn => lockedFloor[pawn]).ToList();
                savedUntils = savedPawns.Select(pawn => lockedUntil[pawn]).ToList();
            }
            Scribe_Collections.Look(ref savedPawns, "floorLockPawns", LookMode.Reference);
            Scribe_Collections.Look(ref savedFloors, "floorLockFloors", LookMode.Value);
            Scribe_Collections.Look(ref savedUntils, "floorLockUntil", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                lockedFloor.Clear();
                lockedUntil.Clear();
                if (savedPawns != null && savedFloors != null && savedUntils != null)
                {
                    for (int i = 0; i < savedPawns.Count && i < savedFloors.Count && i < savedUntils.Count; i++)
                    {
                        if (savedPawns[i] != null && !savedPawns[i].Dead)
                        {
                            lockedFloor[savedPawns[i]] = savedFloors[i];
                            lockedUntil[savedPawns[i]] = savedUntils[i];
                        }
                    }
                }
                Log.Message("Z-Levels floor locks restored: " + lockedFloor.Count + ".");
                savedPawns = null;
                savedFloors = null;
                savedUntils = null;
            }
        }
    }
}
