using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZLevels
{
    // Per-colonist floor rules. A lock keeps a colonist's job search to one floor. The lock stores the floor's Z index, not the Map,
    // because a Map is not saved as an object. ZLevelsManager saves the locks with the game.
    public class FloorPolicy
    {
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
