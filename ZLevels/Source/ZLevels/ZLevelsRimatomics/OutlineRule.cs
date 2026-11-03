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

    // One plane. X and Z are the cell the plane belongs to. Side is the direction the plane faces, numbered as Rot4 numbers it: 0 north, 1 east,
    // 2 south, 3 west. Colour is the colour of the plane. A planned call carries the palette colour of its group. The hook records each call with the
    // colour its material read back, so a record shows the colour that reached the draw.
    public struct OutlineDraw
    {
        public readonly int X;
        public readonly int Z;
        public readonly int Side;
        public readonly OutlineColour Colour;

        public OutlineDraw(int x, int z, int side, OutlineColour colour)
        {
            X = x;
            Z = z;
            Side = side;
            Colour = colour;
        }
    }

    // The outline rule. It has no game types, so the hook, the harness, and the standalone check run the same code. A cell draws one plane on each side that
    // leads onto the map and does not lead onto another cell of the same state. A cell whose four sides all lead onto other cells draws no plane.
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

        // The sides of one cell that draw a plane, as bits. Bit 0 is north, bit 1 east, bit 2 south, and bit 3 west. Zero means no plane.
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

        // The plane calls for one state group, in cell order and then side order. Each open side of each cell gives one call, in the colour of the group.
        // The hook calls this for its paired cells and for its unpaired cells. ExpectedCalls calls it for the scenario's cells. One rule decides both.
        public static List<OutlineDraw> PlaneCalls(IList<OutlineCell> group, OutlineColour colour, Func<int, int, bool> onMap)
        {
            var calls = new List<OutlineDraw>();
            foreach (OutlineCell cell in group)
            {
                int openSides = OpenSides(cell.X, cell.Z, onMap, (x, z) => Contains(group, x, z));
                for (int side = 0; side < 4; side++)
                {
                    if ((openSides & (1 << side)) != 0) calls.Add(new OutlineDraw(cell.X, cell.Z, side, colour));
                }
            }
            return calls;
        }

        // The plane calls that the scenario expects. A cell listed twice counts once, and its first entry sets its state. Each state runs through
        // PlaneCalls on its own, so a side that meets a cell of the other state still draws.
        public static List<OutlineDraw> ExpectedCalls(IList<OutlineCell> placed, Func<int, int, bool> onMap)
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
            List<OutlineDraw> expected = PlaneCalls(paired, PairedColour, onMap);
            expected.AddRange(PlaneCalls(unpaired, UnpairedColour, onMap));
            return expected;
        }

        // The verdict for the drawn row. It passes only when the drawn planes equal the expected planes, counted by (cell, side, colour). Each expected
        // plane must be drawn once. A plane drawn twice, or drawn without being expected, fails the row. A colour counts by palette, so a readback one
        // 8-bit step away still matches. planesCounted is the hook's own count of draw calls, and it must equal the number of records.
        public static string DrawnRowVerdict(IList<OutlineDraw> expected, IList<OutlineDraw> drawn, int planesCounted)
        {
            if (expected.Count == 0) return "FAIL: no cooling column stands on the adjacent level, so the outline has nothing to match";
            if (planesCounted != drawn.Count) return "FAIL: the hook counted " + planesCounted + " draw calls but recorded " + drawn.Count;
            Dictionary<PlaneKey, int> expectedCounts = CountsOf(expected);
            Dictionary<PlaneKey, int> drawnCounts = CountsOf(drawn);
            var unexpected = new List<OutlineDraw>();
            var repeated = new List<OutlineDraw>();
            foreach (OutlineDraw extra in Beyond(drawn, expectedCounts))
            {
                if (CountIn(expectedCounts, new PlaneKey(extra)) == 0) unexpected.Add(extra);
                else repeated.Add(extra);
            }
            List<OutlineDraw> missing = Beyond(expected, drawnCounts);
            if (unexpected.Count > 0 || repeated.Count > 0 || missing.Count > 0)
            {
                return "FAIL: the drawn planes are not the expected planes, drawn but not expected " + Describe(unexpected) + ", drawn more than once "
                    + Describe(repeated) + ", expected but not drawn " + Describe(missing);
            }
            return "PASS: " + drawn.Count + " planes match the " + expected.Count + " expected, " + CellCount(drawn, false) + " cells, "
                + CellCount(drawn, true) + " paired";
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

        private static bool Contains(IList<OutlineCell> cells, int x, int z)
        {
            foreach (OutlineCell cell in cells)
            {
                if (cell.X == x && cell.Z == z) return true;
            }
            return false;
        }

        // The number of times each (cell, side, colour) appears in a list.
        private static Dictionary<PlaneKey, int> CountsOf(IList<OutlineDraw> draws)
        {
            var counts = new Dictionary<PlaneKey, int>();
            foreach (OutlineDraw draw in draws)
            {
                PlaneKey key = new PlaneKey(draw);
                counts[key] = CountIn(counts, key) + 1;
            }
            return counts;
        }

        private static int CountIn(Dictionary<PlaneKey, int> counts, PlaneKey key)
        {
            int count;
            return counts.TryGetValue(key, out count) ? count : 0;
        }

        // The draws in the list that go beyond the count that the other counts hold for the same (cell, side, colour). They keep the list's order.
        private static List<OutlineDraw> Beyond(IList<OutlineDraw> draws, Dictionary<PlaneKey, int> otherCounts)
        {
            var result = new List<OutlineDraw>();
            var seen = new Dictionary<PlaneKey, int>();
            foreach (OutlineDraw draw in draws)
            {
                PlaneKey key = new PlaneKey(draw);
                int before = CountIn(seen, key);
                seen[key] = before + 1;
                if (before >= CountIn(otherCounts, key)) result.Add(draw);
            }
            return result;
        }

        // The number of distinct cells among the draws. With pairedOnly set, it counts only the cells that are drawn in the paired colour.
        private static int CellCount(IList<OutlineDraw> draws, bool pairedOnly)
        {
            var cells = new HashSet<long>();
            foreach (OutlineDraw draw in draws)
            {
                if (pairedOnly && PaletteOf(draw.Colour) != 1) continue;
                cells.Add(CellKey(draw.X, draw.Z));
            }
            return cells.Count;
        }

        // One key for each cell. The low 32 bits hold z, and the high 32 bits hold x.
        private static long CellKey(int x, int z)
        {
            return ((long)x << 32) | (uint)z;
        }

        // One key for each (cell, side, colour). A colour in a palette is keyed by its palette, so readback error cannot split it. A colour in neither
        // palette is keyed by its rounded channels.
        private struct PlaneKey : IEquatable<PlaneKey>
        {
            private readonly long cell;
            private readonly int side;
            private readonly int palette;
            private readonly int red;
            private readonly int green;
            private readonly int blue;
            private readonly int alpha;

            public PlaneKey(OutlineDraw draw)
            {
                cell = CellKey(draw.X, draw.Z);
                side = draw.Side;
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

            public bool Equals(PlaneKey other)
            {
                return cell == other.cell && side == other.side && palette == other.palette && red == other.red && green == other.green
                    && blue == other.blue && alpha == other.alpha;
            }

            public override bool Equals(object obj)
            {
                return obj is PlaneKey && Equals((PlaneKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = cell.GetHashCode();
                    hash = hash * 31 + side;
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
            foreach (OutlineDraw draw in draws)
            {
                parts.Add("(" + draw.X + "," + draw.Z + " " + SideName(draw.Side) + " " + ColourName(draw.Colour) + ")");
            }
            return string.Join(" ", parts);
        }

        // The name of a side, as the Rot4 number gives it.
        private static string SideName(int side)
        {
            switch (side)
            {
                case 0: return "north";
                case 1: return "east";
                case 2: return "south";
                case 3: return "west";
                default: return "side " + side;
            }
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
