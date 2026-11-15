using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ZLevels
{
    // Moves items of one def from one floor to another (idea "pipes between floors", decision DM18). Only items in storage on the sending floor
    // move, so a loose item on the floor is left alone. A forbidden item and a reserved one are left alone too. The items land next to the receiving
    // end, and the normal haul logic of that floor stores them.
    public static class FloorPipeMover
    {
        // Moves up to count items of the def, and returns how many moved. A stack is split when only part of it is needed.
        public static int MoveItems(ThingDef def, int count, Map from, Map to, IntVec3 near)
        {
            if (def == null || count <= 0 || from == null || to == null || from == to)
            {
                return 0;
            }
            List<Thing> candidates = from.listerThings.ThingsOfDef(def)
                .Where(thing => thing.Spawned && thing.IsInAnyStorage() && !thing.IsForbidden(Faction.OfPlayer) && !from.reservationManager.IsReserved(thing))
                .ToList();
            int moved = 0;
            foreach (Thing thing in candidates)
            {
                if (moved >= count)
                {
                    break;
                }
                IntVec3 origin = thing.Position;
                int take = Math.Min(thing.stackCount, count - moved);
                Thing piece = take == thing.stackCount ? thing : thing.SplitOff(take);
                if (piece == thing)
                {
                    thing.DeSpawn();
                }
                if (!GenPlace.TryPlaceThing(piece, near, to, ThingPlaceMode.Near))
                {
                    // The receiving floor has no room for it, so it goes back to where it was, and the move is not counted.
                    GenPlace.TryPlaceThing(piece, origin, from, ThingPlaceMode.Near);
                    continue;
                }
                moved += take;
            }
            return moved;
        }
    }
}
