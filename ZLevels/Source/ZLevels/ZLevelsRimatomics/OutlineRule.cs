using System;
using System.Collections.Generic;
using System.Globalization;

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

    // A colour as the four channels of a Unity Color. The rule keeps plain floats, so it compiles with no game types.
    public struct OutlineColour
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;
        public readonly float A;

        public OutlineColour(float r, float g, float b, float a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    // One Graphics.DrawMesh call for an outline plane. X and Z are the cell the plane belongs to. Colour is the colour that the call's material had.
    // The hook records each call from the material that the call receives, so a record shows the colour that reached the draw.
    public struct OutlineDraw
    {
        public readonly int X;
        public readonly int Z;
        public readonly OutlineColour Colour;

        public OutlineDraw(int x, int z, OutlineColour colour)
        {
            X = x;
            Z = z;
            Colour = colour;
        }
    }

    // The outline rule for one cell, with no game types, so a standalone check can compile and run it. A cell draws one plane on each side that
    // leads onto the map and does not lead onto another cell of the same state. A cell whose four sides all lead onto other cells draws no plane.
    // Side numbers match Rot4: 0 is north, 1 east, 2 south, 3 west.
    public static class OutlineRule
    {
        // The two outline colours. The hook draws with these, and the drawn row expects these.
        public static readonly OutlineColour PairedColour = new OutlineColour(0.25f, 0.9f, 0.25f, 1f);
        public static readonly OutlineColour UnpairedColour = new OutlineColour(1f, 0.75f, 0f, 1f);

        // The step to the neighbour on each side. It matches GenAdj.CardinalDirections, where z grows toward the north.
        private static readonly int[] StepX = { 0, 1, 0, -1 };
        private static readonly int[] StepZ = { 1, 0, -1, 0 };

        // A material's colour reads back rounded to one of 256 steps per channel, so a palette colour can come back one step away from its value.
        // Two colours are the same when every channel is within one step. The two palette colours differ by far more than one step.
        private const float ChannelTolerance = 1f / 255f;

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

        // The draws that the scenario expects: one for each expected cell, in the colour of its state.
        public static List<OutlineDraw> ExpectedDraws(IList<OutlineCell> expected)
        {
            var draws = new List<OutlineDraw>();
            foreach (OutlineCell cell in expected)
            {
                draws.Add(new OutlineDraw(cell.X, cell.Z, cell.Paired ? PairedColour : UnpairedColour));
            }
            return draws;
        }

        // The verdict for the drawn row. It passes only when the (cell, colour) pairs of the draw calls are the expected pairs, as sets, and at least one
        // plane drew. A count cannot tell two different cells apart, and a call in the other colour is a different pair, so both are compared. The row
        // prints the distinct pairs that were drawn and the distinct pairs that were expected, each counted from its own list.
        public static string DrawnRowVerdict(IList<OutlineCell> expected, IList<OutlineDraw> drawn, int planes)
        {
            if (expected.Count == 0) return "FAIL: no cooling column stands on the adjacent level, so the outline has nothing to match";
            List<OutlineDraw> expectedDraws = ExpectedDraws(expected);
            var expectedPairs = new HashSet<PairKey>();
            foreach (OutlineDraw draw in expectedDraws) expectedPairs.Add(new PairKey(draw));
            var drawnPairs = new HashSet<PairKey>();
            foreach (OutlineDraw draw in drawn) drawnPairs.Add(new PairKey(draw));
            List<OutlineDraw> missing = NotIn(expectedDraws, drawnPairs);
            List<OutlineDraw> extra = NotIn(drawn, expectedPairs);
            if (missing.Count > 0 || extra.Count > 0)
            {
                return "FAIL: the drawn cells are not the expected cells, drawn but not expected " + Describe(extra) + ", expected but not drawn " + Describe(missing);
            }
            if (planes <= 0) return "FAIL: the expected cells drew no planes";
            return "PASS: " + drawnPairs.Count + " cells of " + expectedPairs.Count + " expected, " + planes + " planes, " + PairedCount(drawn) + " paired";
        }

        // True when two colours match on every channel, to within one step of ChannelTolerance.
        public static bool SameColour(OutlineColour first, OutlineColour second)
        {
            return Math.Abs(first.R - second.R) <= ChannelTolerance && Math.Abs(first.G - second.G) <= ChannelTolerance
                && Math.Abs(first.B - second.B) <= ChannelTolerance && Math.Abs(first.A - second.A) <= ChannelTolerance;
        }

        // The palette a colour is in: 1 for the paired colour, 2 for the unpaired colour, and 0 for neither.
        private static int PaletteOf(OutlineColour colour)
        {
            if (SameColour(colour, PairedColour)) return 1;
            if (SameColour(colour, UnpairedColour)) return 2;
            return 0;
        }

        // A channel rounded to the nearest of 256 steps. It keys a colour that is in neither palette.
        private static int Step(float channel)
        {
            return (int)Math.Round(channel * 255f);
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

        // The draws whose (cell, colour) pair is not in pairs. Each distinct pair is listed once, even when draws lists it more than once.
        private static List<OutlineDraw> NotIn(IList<OutlineDraw> draws, HashSet<PairKey> pairs)
        {
            var result = new List<OutlineDraw>();
            var listed = new HashSet<PairKey>();
            foreach (OutlineDraw draw in draws)
            {
                var key = new PairKey(draw);
                if (!pairs.Contains(key) && listed.Add(key)) result.Add(draw);
            }
            return result;
        }

        // The number of distinct drawn pairs whose colour is the paired colour.
        private static int PairedCount(IList<OutlineDraw> draws)
        {
            var seen = new HashSet<PairKey>();
            int paired = 0;
            foreach (OutlineDraw draw in draws)
            {
                if (seen.Add(new PairKey(draw)) && SameColour(draw.Colour, PairedColour)) paired++;
            }
            return paired;
        }

        // One key for each cell. The low 32 bits hold z, and the high 32 bits hold x.
        private static long CellKey(int x, int z)
        {
            return ((long)x << 32) | (uint)z;
        }

        // One key for each (cell, colour) pair. A colour in a palette is keyed by its palette, so readback error cannot split it in two. A colour in
        // neither palette is keyed by its channels, so two such colours are the same pair only when their channels round alike.
        private struct PairKey : IEquatable<PairKey>
        {
            private readonly long cell;
            private readonly int palette;
            private readonly int red;
            private readonly int green;
            private readonly int blue;
            private readonly int alpha;

            public PairKey(OutlineDraw draw)
            {
                cell = CellKey(draw.X, draw.Z);
                palette = PaletteOf(draw.Colour);
                if (palette != 0)
                {
                    red = 0;
                    green = 0;
                    blue = 0;
                    alpha = 0;
                    return;
                }
                red = Step(draw.Colour.R);
                green = Step(draw.Colour.G);
                blue = Step(draw.Colour.B);
                alpha = Step(draw.Colour.A);
            }

            public bool Equals(PairKey other)
            {
                return cell == other.cell && palette == other.palette && red == other.red && green == other.green && blue == other.blue
                    && alpha == other.alpha;
            }

            public override bool Equals(object obj)
            {
                return obj is PairKey && Equals((PairKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = cell.GetHashCode();
                    hash = hash * 31 + palette;
                    hash = hash * 31 + red;
                    hash = hash * 31 + green;
                    hash = hash * 31 + blue;
                    return hash * 31 + alpha;
                }
            }
        }

        private static string Describe(IList<OutlineDraw> draws)
        {
            if (draws.Count == 0) return "none";
            var parts = new List<string>();
            foreach (OutlineDraw draw in draws) parts.Add("(" + draw.X + "," + draw.Z + " " + ColourName(draw.Colour) + ")");
            return string.Join(" ", parts);
        }

        // The palette name of a colour, or its channels when it is neither palette colour.
        private static string ColourName(OutlineColour colour)
        {
            if (SameColour(colour, PairedColour)) return "paired";
            if (SameColour(colour, UnpairedColour)) return "unpaired";
            return "colour " + colour.R.ToString("F3", CultureInfo.InvariantCulture) + "," + colour.G.ToString("F3", CultureInfo.InvariantCulture)
                + "," + colour.B.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
