using System;
using System.Collections.Generic;

namespace ZLevelsRimatomics
{
    // One cell of the outline, with its state. Paired is true when the viewed level also holds a usable cooling column on the cell.
    public struct OutlineCell
    {
        public readonly int X;
        public readonly int Z;
        public readonly bool Paired;

        public OutlineCell(int x, int z, bool paired)
        {
            X = x;
            Z = z;
            Paired = paired;
        }
    }

    // The outline rule for one cell, with no game types, so a standalone check can compile and run it. A cell draws one plane on each side that
    // leads onto the map and does not lead onto another cell of the same state. A cell whose four sides all lead onto other cells draws no plane.
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

        // The cells of one state that draw at least one plane. The cells of that state form the list that each cell's sides are checked against.
        public static List<OutlineCell> DrawnCells(IList<OutlineCell> cellsOfOneState, Func<int, int, bool> onMap)
        {
            var drawn = new List<OutlineCell>();
            foreach (OutlineCell cell in cellsOfOneState)
            {
                int openSides = OpenSides(cell.X, cell.Z, onMap, (x, z) => Listed(cellsOfOneState, x, z));
                if (openSides != 0) drawn.Add(cell);
            }
            return drawn;
        }

        // The cells that the outline should draw for the placed cells. The hook draws each state on its own, so each state is checked on its own here.
        // A cell listed more than once counts once, and its first entry sets its state.
        public static List<OutlineCell> ExpectedCells(IList<OutlineCell> placed, Func<int, int, bool> onMap)
        {
            var paired = new List<OutlineCell>();
            var unpaired = new List<OutlineCell>();
            var seen = new HashSet<long>();
            foreach (OutlineCell cell in placed)
            {
                if (!seen.Add(CellKey(cell.X, cell.Z))) continue;
                if (cell.Paired) paired.Add(cell);
                else unpaired.Add(cell);
            }
            List<OutlineCell> expected = DrawnCells(paired, onMap);
            expected.AddRange(DrawnCells(unpaired, onMap));
            return expected;
        }

        // The verdict for the drawn row. It passes only when the cells that drew a plane are the expected cells, as sets of (cell, colour) pairs, and at
        // least one plane drew. A count cannot tell two different cells apart, and a cell drawn in the other colour is a different pair, so both are compared.
        public static string DrawnRowVerdict(IList<OutlineCell> expected, IList<OutlineCell> drawn, int planes)
        {
            if (expected.Count == 0) return "FAIL: no cooling column stands on the adjacent level, so the outline has nothing to match";
            var expectedPairs = new HashSet<PairKey>();
            foreach (OutlineCell cell in expected) expectedPairs.Add(new PairKey(cell));
            var drawnPairs = new HashSet<PairKey>();
            foreach (OutlineCell cell in drawn) drawnPairs.Add(new PairKey(cell));
            List<OutlineCell> missing = NotIn(expected, drawnPairs);
            List<OutlineCell> extra = NotIn(drawn, expectedPairs);
            if (missing.Count > 0 || extra.Count > 0)
            {
                return "FAIL: the drawn cells are not the expected cells, drawn but not expected " + Describe(extra) + ", expected but not drawn " + Describe(missing);
            }
            if (planes <= 0) return "FAIL: the expected cells drew no planes";
            return "PASS: " + expected.Count + " cells of " + expected.Count + " expected, " + planes + " planes, " + PairedCount(expected) + " paired";
        }

        private static bool Listed(IList<OutlineCell> cells, int x, int z)
        {
            return Contains(cells, x, z);
        }

        private static bool Contains(IList<OutlineCell> cells, int x, int z)
        {
            foreach (OutlineCell cell in cells)
            {
                if (cell.X == x && cell.Z == z) return true;
            }
            return false;
        }

        // The cells whose (cell, colour) pair is not in pairs. Each distinct pair is listed once, even when cells lists it more than once.
        private static List<OutlineCell> NotIn(IList<OutlineCell> cells, HashSet<PairKey> pairs)
        {
            var result = new List<OutlineCell>();
            var listed = new HashSet<PairKey>();
            foreach (OutlineCell cell in cells)
            {
                var key = new PairKey(cell);
                if (!pairs.Contains(key) && listed.Add(key)) result.Add(cell);
            }
            return result;
        }

        private static int PairedCount(IList<OutlineCell> cells)
        {
            int paired = 0;
            foreach (OutlineCell cell in cells)
            {
                if (cell.Paired) paired++;
            }
            return paired;
        }

        // One key for each cell. The low 32 bits hold z, and the high 32 bits hold x.
        private static long CellKey(int x, int z)
        {
            return ((long)x << 32) | (uint)z;
        }

        // One key for each (cell, colour) pair, so the same cell in the other colour is a different pair.
        private struct PairKey : IEquatable<PairKey>
        {
            private readonly long cell;
            private readonly bool paired;

            public PairKey(OutlineCell outline)
            {
                cell = CellKey(outline.X, outline.Z);
                paired = outline.Paired;
            }

            public bool Equals(PairKey other)
            {
                return cell == other.cell && paired == other.paired;
            }

            public override bool Equals(object obj)
            {
                return obj is PairKey && Equals((PairKey)obj);
            }

            public override int GetHashCode()
            {
                return cell.GetHashCode() * 2 + (paired ? 1 : 0);
            }
        }

        private static string Describe(IList<OutlineCell> cells)
        {
            if (cells.Count == 0) return "none";
            var parts = new List<string>();
            foreach (OutlineCell cell in cells) parts.Add("(" + cell.X + "," + cell.Z + (cell.Paired ? " paired" : " unpaired") + ")");
            return string.Join(" ", parts);
        }
    }
}
