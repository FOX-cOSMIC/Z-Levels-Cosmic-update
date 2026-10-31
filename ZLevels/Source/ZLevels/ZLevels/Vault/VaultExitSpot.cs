using System;
using RimWorld;
using Verse;

namespace ZLevels
{
    // Chooses where the next vault exit goes. Exits keep their distance from each other, and each one stands on open floor.
    public static class VaultExitSpot
    {
        private const float Spacing = 6f;

        private const int EdgeMargin = 3;

        public static IntVec3 Find(Map pocket)
        {
            foreach (IntVec3 cell in pocket.AllCells)
            {
                if (cell.DistanceToEdge(pocket) >= EdgeMargin && cell.Standable(pocket) && !NearExit(cell, pocket))
                {
                    return cell;
                }
            }
            throw new InvalidOperationException("The vault pocket has no room for another exit.");
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
