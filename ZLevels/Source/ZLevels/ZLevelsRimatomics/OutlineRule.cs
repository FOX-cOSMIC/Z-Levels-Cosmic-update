using System;

namespace ZLevelsRimatomics
{
    // The outline rule for one cell, with no game types, so a standalone check can compile and run it. A cell draws one plane on each side that
    // leads onto the map and does not lead onto another cell of the outline. A cell whose four sides all lead onto other cells draws no plane.
    // Side numbers match Rot4: 0 is north, 1 east, 2 south, 3 west.
    public static class OutlineRule
    {
        // The step to the neighbour on each side. It matches GenAdj.CardinalDirections, where z grows toward the north.
        private static readonly int[] StepX = { 0, 1, 0, -1 };
        private static readonly int[] StepZ = { 1, 0, -1, 0 };

        // Returns one bit for each side that gets a plane. Bit 0 is north, bit 1 east, bit 2 south, and bit 3 west. Zero means no plane.
        public static int OpenSides(int x, int z, Func<int, int, bool> onMap, Func<int, int, bool> listed)
        {
            int openSides = 0;
            for (int side = 0; side < 4; side++)
            {
                int neighbourX = x + StepX[side];
                int neighbourZ = z + StepZ[side];
                if (onMap(neighbourX, neighbourZ) && !listed(neighbourX, neighbourZ)) openSides |= 1 << side;
            }
            return openSides;
        }
    }
}
