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

        private const float Spacing = 6f;

        public static IntVec3 Find(Map pocket)
        {
            IntVec3 centre = pocket.Center;
            IntVec3 best = IntVec3.Invalid;
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
                    best = cell;
                    bestDistance = distance;
                }
            }
            if (!best.IsValid)
            {
                throw new InvalidOperationException("The vault room has no room for another exit.");
            }
            return best;
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
