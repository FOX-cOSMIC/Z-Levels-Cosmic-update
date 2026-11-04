using System;
using RimWorld;
using Verse;

namespace ZLevels
{
    // Chooses where the next vault exit goes. The room at the centre of the pocket is cleared by GenStep_VaultRock, and every exit stands in it.
    // The first exit takes the centre. Later exits take the open cell nearest the centre that keeps its distance from the other exits.
    public static class VaultExitSpot
    {
        // Every cell within this distance of the pocket's centre is open floor. GenStep_VaultRock clears the same cells.
        public const float RoomRadius = 10f;

        // Exits stay more than this far from each other. At this spacing the room holds 27 exits, as the -VaultFull run measures.
        private const float Spacing = 3f;

        public static IntVec3 Find(Map pocket)
        {
            if (TryFind(pocket, out IntVec3 spot))
            {
                return spot;
            }
            throw new InvalidOperationException("The vault room has no room for another exit.");
        }

        // False when the room has no open cell that keeps its distance from the other exits. The placement check uses this to refuse an entrance.
        public static bool TryFind(Map pocket, out IntVec3 spot)
        {
            IntVec3 centre = pocket.Center;
            spot = IntVec3.Invalid;
            float bestDistance = float.MaxValue;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(centre, RoomRadius, useCenter: true))
            {
                float distance = cell.DistanceTo(centre);
                if (distance > RoomRadius || !cell.InBounds(pocket) || !cell.Standable(pocket) || NearExit(cell, pocket))
                {
                    continue;
                }
                if (distance < bestDistance)
                {
                    spot = cell;
                    bestDistance = distance;
                }
            }
            return spot.IsValid;
        }

        private static bool NearExit(IntVec3 cell, Map pocket)
        {
            foreach (IntVec3 near in GenRadial.RadialCellsAround(cell, Spacing, useCenter: true))
            {
                if (!near.InBounds(pocket))
                {
                    continue;
                }
                foreach (Thing thing in near.GetThingList(pocket))
                {
                    if (thing is PocketMapExit)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
