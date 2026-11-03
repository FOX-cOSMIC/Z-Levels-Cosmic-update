using System;
using System.Collections.Generic;
using ZLevelsRimatomics;

// Usage, from the repository root: dotnet run --project tools/OutlineRuleCheck
// Runs the outline rule on small grids, with no game types. It checks three things: which planes a cell draws (OpenSides and PlaneCalls), how the cells split
// by state (SplitStates, which PlannedCalls calls, and the hook plans its calls with PlannedCalls), and the verdict on a drawn row (RowVerdict). It does not
// check the draw loop in CoolingColumnHighlight.cs, which calls Graphics.DrawMesh with the matrix and colour it builds. The game run checks that loop. This
// project compiles the same OutlineRule.cs that the hook and the harness run. Each check prints PASS or FAIL. The exit code is the number of checks that failed,
// so 0 means every check passed. The check writes no files of its own. dotnet run builds it into bin and obj, which .gitignore covers.
static class Program
{
    static int checks;
    static int failures;

    // The altitude that the rows use here. The rule takes the altitude as a parameter, so any value works as long as the plan and the drawn calls agree.
    const float Altitude = 0.25f;

    static void Check(string name, bool ok, string detail)
    {
        checks++;
        if (!ok) failures++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + " -> " + detail);
    }

    static int Planes(int mask)
    {
        int planes = 0;
        for (int side = 0; side < 4; side++) if ((mask & (1 << side)) != 0) planes++;
        return planes;
    }

    // The mask for one cell of a list of tuples on a square map.
    static int MaskFor(int x, int z, int size, List<(int, int)> cells)
    {
        return OutlineRule.OpenSides(x, z,
            (nx, nz) => nx >= 0 && nz >= 0 && nx < size && nz < size,
            (nx, nz) => cells.Contains((nx, nz)));
    }

    // Counts the cells that draw at least one plane, and the planes drawn, for the whole list of tuples.
    static void Totals(List<(int, int)> cells, int size, out int drawnCells, out int planes)
    {
        drawnCells = 0;
        planes = 0;
        foreach (var cell in cells)
        {
            int planesHere = Planes(MaskFor(cell.Item1, cell.Item2, size, cells));
            planes += planesHere;
            if (planesHere > 0) drawnCells++;
        }
    }

    static Func<int, int, bool> Square(int size)
    {
        return (x, z) => x >= 0 && z >= 0 && x < size && z < size;
    }

    static Func<int, int, bool> Box(int low, int high)
    {
        return (x, z) => x >= low && z >= low && x <= high && z <= high;
    }

    static Func<int, int, bool> Anywhere()
    {
        return (x, z) => true;
    }

    static OutlineCell C(int x, int z, bool paired = false)
    {
        return new OutlineCell(x, z, paired);
    }

    static List<OutlineCell> Plus(bool centrePaired = false)
    {
        return new List<OutlineCell> { C(2, 2, centrePaired), C(2, 1), C(1, 2), C(3, 2), C(2, 3) };
    }

    static List<OutlineCell> NoCells()
    {
        return new List<OutlineCell>();
    }

    // The calls the hook draws for the placed cells: the plan, with each call's pose as the plan gives it. That is the pose a correct draw reaches.
    static List<OutlineDraw> DrawnFor(IList<OutlineCell> placed, Func<int, int, bool> onMap)
    {
        return OutlineRule.PlannedCalls(placed, onMap, Altitude);
    }

    // The planes that the row expects for the placed cells.
    static List<OutlineDraw> Expected(IList<OutlineCell> placed, Func<int, int, bool> onMap)
    {
        return OutlineRule.PlannedCalls(placed, onMap, Altitude);
    }

    // The verdict on a drawn row, with the given lost cells, the hook's counter, and the paired requirement.
    static string Row(IList<OutlineCell> placed, IList<OutlineCell> lost, IList<OutlineDraw> drawn, int counted, Func<int, int, bool> onMap, bool requirePaired)
    {
        return OutlineRule.RowVerdict(placed, lost, drawn, counted, onMap, Altitude, requirePaired);
    }

    // The verdict on a drawn row when every placed column stands and the counter agrees with the records.
    static string Verdict(IList<OutlineCell> placed, IList<OutlineDraw> drawn, Func<int, int, bool> onMap)
    {
        return Row(placed, NoCells(), drawn, drawn.Count, onMap, false);
    }

    static int CountFor(IList<OutlineDraw> calls, int x, int z)
    {
        int count = 0;
        foreach (OutlineDraw call in calls) if (call.X == x && call.Z == z) count++;
        return count;
    }

    static bool AllPaired(IList<OutlineDraw> calls, int x, int z)
    {
        int seen = 0;
        foreach (OutlineDraw call in calls)
        {
            if (call.X != x || call.Z != z) continue;
            seen++;
            if (!OutlineRule.SameColour(call.Colour, OutlineRule.PairedColour)) return false;
        }
        return seen > 0;
    }

    // Every call of one cell takes the given colour, as a fault that recolours the whole cell does. The poses stay as they were.
    static List<OutlineDraw> Recolour(IList<OutlineDraw> calls, int x, int z, OutlineColour colour)
    {
        var result = new List<OutlineDraw>();
        foreach (OutlineDraw call in calls)
        {
            result.Add(call.X == x && call.Z == z ? new OutlineDraw(x, z, call.Side, colour, call.Pose) : call);
        }
        return result;
    }

    // Only the first call of one cell takes the given colour, as a fault at one draw call does.
    static List<OutlineDraw> RecolourFirst(IList<OutlineDraw> calls, int x, int z, OutlineColour colour)
    {
        var result = new List<OutlineDraw>(calls);
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].X == x && result[i].Z == z)
            {
                result[i] = new OutlineDraw(x, z, result[i].Side, colour, result[i].Pose);
                break;
            }
        }
        return result;
    }

    // The first call of one cell on one side is left out, as a skipped plane is.
    static List<OutlineDraw> Drop(IList<OutlineDraw> calls, int x, int z, int side)
    {
        var result = new List<OutlineDraw>(calls);
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].X == x && result[i].Z == z && result[i].Side == side)
            {
                result.RemoveAt(i);
                break;
            }
        }
        return result;
    }

    // The first call of one cell on one side is made again, at the end of the list.
    static List<OutlineDraw> Repeat(IList<OutlineDraw> calls, int x, int z, int side)
    {
        var result = new List<OutlineDraw>(calls);
        foreach (OutlineDraw call in calls)
        {
            if (call.X == x && call.Z == z && call.Side == side)
            {
                result.Add(call);
                break;
            }
        }
        return result;
    }

    // The first call of one cell on one side turns to face another side, and reaches the draw at that side's planned pose.
    static List<OutlineDraw> Turn(IList<OutlineDraw> calls, int x, int z, int fromSide, int toSide)
    {
        var result = new List<OutlineDraw>(calls);
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].X == x && result[i].Z == z && result[i].Side == fromSide)
            {
                result[i] = new OutlineDraw(x, z, toSide, result[i].Colour, OutlineRule.PlannedPose(x, z, toSide, Altitude));
                break;
            }
        }
        return result;
    }

    // The first call of one cell on one side reaches the draw with the given pose.
    static List<OutlineDraw> WithPoseOf(IList<OutlineDraw> calls, int x, int z, int side, OutlinePose pose)
    {
        var result = new List<OutlineDraw>(calls);
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].X == x && result[i].Z == z && result[i].Side == side)
            {
                result[i] = OutlineRule.WithPose(result[i], pose);
                break;
            }
        }
        return result;
    }

    // The first call of one cell on one side reaches the draw offset from its planned pose.
    static List<OutlineDraw> OffsetPose(IList<OutlineDraw> calls, int x, int z, int side, float dx, float dy, float dz)
    {
        OutlinePose planned = OutlineRule.PlannedPose(x, z, side, Altitude);
        var moved = new OutlinePose(planned.X + dx, planned.Y + dy, planned.Z + dz, planned.QX, planned.QY, planned.QZ, planned.QW);
        return WithPoseOf(calls, x, z, side, moved);
    }

    // The first call of one cell on one side reaches the draw with the planned rotation negated, which is the same rotation.
    static List<OutlineDraw> NegatedRotation(IList<OutlineDraw> calls, int x, int z, int side)
    {
        OutlinePose planned = OutlineRule.PlannedPose(x, z, side, Altitude);
        var negated = new OutlinePose(planned.X, planned.Y, planned.Z, -planned.QX, -planned.QY, -planned.QZ, -planned.QW);
        return WithPoseOf(calls, x, z, side, negated);
    }

    // The first call of each cell, and no other call.
    static List<OutlineDraw> FirstCallOfEachCell(IList<OutlineDraw> calls)
    {
        var result = new List<OutlineDraw>();
        var seen = new HashSet<(int, int)>();
        foreach (OutlineDraw call in calls)
        {
            if (seen.Add((call.X, call.Z))) result.Add(call);
        }
        return result;
    }

    static bool IsFail(string verdict)
    {
        return verdict.StartsWith("FAIL", StringComparison.Ordinal);
    }

    static bool IsPass(string verdict)
    {
        return verdict.StartsWith("PASS:", StringComparison.Ordinal);
    }

    static int Main()
    {
        // Shapes. A lone cell draws four planes. A cell whose four neighbours share its state draws none.
        var lone = new List<(int, int)> { (2, 2) };
        Check("lone cell draws a plane on each side", MaskFor(2, 2, 5, lone) == 15, "mask " + MaskFor(2, 2, 5, lone));
        var enclosed = new List<(int, int)> { (2, 2), (2, 3), (3, 2), (2, 1), (1, 2) };
        Check("enclosed cell draws no plane", MaskFor(2, 2, 5, enclosed) == 0, "mask " + MaskFor(2, 2, 5, enclosed));
        Totals(enclosed, 5, out int enclosedCells, out int enclosedPlanes);
        Check("enclosed cell is not counted", enclosedCells == 4 && enclosedPlanes == 12, "counted " + enclosedCells + " of 5 cells, planes " + enclosedPlanes);
        var edge = new List<(int, int)> { (0, 2), (1, 2) };
        int edgeMask = MaskFor(0, 2, 5, edge);
        Check("edge cell draws north and south only", edgeMask == 5, "mask " + edgeMask + " (north is bit 0, south is bit 2)");
        var line = new List<(int, int)> { (1, 2), (2, 2), (3, 2) };
        int middleMask = MaskFor(2, 2, 5, line);
        Check("middle cell of a line draws north and south only", middleMask == 5, "mask " + middleMask);
        Totals(line, 5, out int lineCells, out int linePlanes);
        Check("line of three counts three cells and eight planes", lineCells == 3 && linePlanes == 8, "counted " + lineCells + ", planes " + linePlanes);
        var corner = new List<(int, int)> { (0, 0) };
        int cornerMask = MaskFor(0, 0, 5, corner);
        Check("corner cell draws north and east only", cornerMask == 3, "mask " + cornerMask);

        // The plus shape in one colour. The four arms draw twelve planes, and the enclosed centre draws none.
        var plus = Plus();
        Func<int, int, bool> onFive = Square(5);
        List<OutlineDraw> plusExpected = Expected(plus, onFive);
        Check("plus: the expected planes are the twelve arm planes", plusExpected.Count == 12, "count " + plusExpected.Count);
        Check("plus: the enclosed centre has no expected plane", CountFor(plusExpected, 2, 2) == 0, "centre planes " + CountFor(plusExpected, 2, 2));
        List<OutlineDraw> plusCalls = DrawnFor(plus, onFive);
        string plusVerdict = Verdict(plus, plusCalls, onFive);
        Check("plus: the calls match the expected planes", IsPass(plusVerdict), plusVerdict);
        const string plusText = "PASS: 12 planes match the 12 expected planes, 4 cells of 4 expected cells, 0 paired of 0 expected paired";
        Check("plus: the PASS text counts planes, cells and paired cells", plusVerdict == plusText, plusVerdict);

        // Colours. The centre is green and the arms are amber. Each arm draws four planes, because the centre is in the other state.
        var mixed = Plus(centrePaired: true);
        List<OutlineDraw> mixedExpected = Expected(mixed, onFive);
        Check("colour: the green centre draws four planes and each amber arm draws four",
            mixedExpected.Count == 20 && CountFor(mixedExpected, 2, 2) == 4 && CountFor(mixedExpected, 2, 1) == 4 && AllPaired(mixedExpected, 2, 2),
            "count " + mixedExpected.Count);
        List<OutlineDraw> mixedCalls = DrawnFor(mixed, onFive);
        string rightColours = Verdict(mixed, mixedCalls, onFive);
        Check("colour: the right colours pass", IsPass(rightColours), rightColours);
        Check("colour: the mixed PASS text counts 20 planes, 5 cells and 1 paired",
            rightColours == "PASS: 20 planes match the 20 expected planes, 5 cells of 5 expected cells, 1 paired of 1 expected paired", rightColours);
        string centreAmber = Verdict(mixed, Recolour(mixedCalls, 2, 2, OutlineRule.UnpairedColour), onFive);
        Check("colour: the green centre drawn amber fails the row", IsFail(centreAmber), centreAmber);
        string armGreen = Verdict(mixed, Recolour(mixedCalls, 3, 2, OutlineRule.PairedColour), onFive);
        Check("colour: an amber arm drawn green fails the row", IsFail(armGreen), armGreen);
        string readBack = Verdict(mixed, Recolour(mixedCalls, 2, 2, new OutlineColour(0.251f, 0.902f, 0.251f, 1f)), onFive);
        Check("colour: a paired readback one step from the palette still passes", IsPass(readBack), readBack);
        string shifted = Verdict(mixed, Recolour(mixedCalls, 2, 2, new OutlineColour(0.35f, 0.9f, 0.25f, 1f)), onFive);
        Check("colour: a paired colour shifted by a tenth fails the row", IsFail(shifted), shifted);
        string offPalette = Verdict(mixed, Recolour(mixedCalls, 3, 2, new OutlineColour(0.5f, 0.5f, 0.5f, 1f)), onFive);
        Check("colour: a colour in neither palette fails the row", IsFail(offPalette), offPalette);

        // One call in the wrong colour. The first call of cell 2,1 faces north, so the row names that plane in both colours.
        string oneCall = Verdict(mixed, RecolourFirst(mixedCalls, 2, 1, OutlineRule.PairedColour), onFive);
        Check("call: one call in the wrong colour names its cell, side, and both colours",
            IsFail(oneCall) && oneCall.Contains("(2,1 north paired)") && oneCall.Contains("(2,1 north unpaired)"), oneCall);

        // Planes. Each expected plane is drawn once. A skip, a repeat, or a turn fails the row, and the row names the plane.
        string skip = Verdict(mixed, Drop(mixedCalls, 3, 2, 0), onFive);
        Check("planes: a skipped plane fails and names it", IsFail(skip) && skip.Contains("expected but not drawn (3,2 north unpaired)"), skip);
        string repeat = Verdict(mixed, Repeat(mixedCalls, 2, 1, 0), onFive);
        Check("planes: a repeated call fails and names the plane", IsFail(repeat) && repeat.Contains("drawn more than once (2,1 north unpaired)"), repeat);
        string turn = Verdict(mixed, Turn(mixedCalls, 2, 1, 0, 1), onFive);
        Check("planes: a plane that faces another side fails and names both sides",
            IsFail(turn) && turn.Contains("expected but not drawn (2,1 north unpaired)") && turn.Contains("drawn more than once (2,1 east unpaired)"), turn);

        // The old row passed six cells with one call each. Each of these cells draws four planes, so the expected set has 24 planes.
        var six = new List<OutlineCell> { C(1, 1), C(1, 4), C(4, 1), C(4, 4), C(7, 7), C(8, 2) };
        Func<int, int, bool> onTen = Square(10);
        List<OutlineDraw> sixExpected = Expected(six, onTen);
        List<OutlineDraw> sixOneEach = FirstCallOfEachCell(DrawnFor(six, onTen));
        string sixVerdict = Verdict(six, sixOneEach, onTen);
        Check("planes: six cells with one call each fail, because the expected planes are 24",
            sixExpected.Count == 24 && sixOneEach.Count == 6 && IsFail(sixVerdict), "expected " + sixExpected.Count + ", " + sixVerdict);

        // The hook's counter and its records must agree, and something must be expected.
        string counter = Row(mixed, NoCells(), mixedCalls, mixedCalls.Count + 1, onFive, false);
        Check("planes: a counter that disagrees with the records fails", IsFail(counter), counter);
        string zeroCounter = Row(mixed, NoCells(), mixedCalls, 0, onFive, false);
        Check("planes: matching calls with a zero counter fail", IsFail(zeroCounter), zeroCounter);
        string nothingExpected = Row(NoCells(), NoCells(), mixedCalls, mixedCalls.Count, onFive, false);
        Check("planes: nothing expected fails and names the drawn planes",
            IsFail(nothingExpected) && nothingExpected.Contains("The drawn planes are (2,2 north paired)"), nothingExpected);

        // Placed columns that are no longer usable. The cell 3,2 is missing from the placed cells and from the drawn calls, as a column removed from both
        // lists would be. Its placement record still stands, so the cell goes into lost, and the row fails naming it.
        var fourArms = new List<OutlineCell> { C(2, 2), C(2, 1), C(1, 2), C(2, 3) };
        List<OutlineDraw> fourDrawn = DrawnFor(fourArms, onFive);
        string withoutLoss = Verdict(fourArms, fourDrawn, onFive);
        Check("lost: the four cells alone pass, so the loss is what fails the row", IsPass(withoutLoss), withoutLoss);
        string lostCell = Row(fourArms, new List<OutlineCell> { C(3, 2) }, fourDrawn, fourDrawn.Count, onFive, false);
        Check("lost: a column removed from both lists fails the row and names its cell", IsFail(lostCell) && lostCell.Contains("at (3,2), so"), lostCell);
        string lostTwice = Row(fourArms, new List<OutlineCell> { C(3, 2), C(3, 2) }, fourDrawn, fourDrawn.Count, onFive, false);
        Check("lost: a cell lost twice is named once", IsFail(lostTwice) && lostTwice.Contains("at (3,2), so") && !lostTwice.Contains("(3,2) (3,2)"), lostTwice);

        // A with-pair row needs a paired cell drawn. A row with none fails, and a row that draws one passes.
        string zeroPaired = Row(plus, NoCells(), plusCalls, plusCalls.Count, onFive, true);
        Check("pair: a with-pair row with zero paired cells fails",
            IsFail(zeroPaired) && zeroPaired.Contains("no cell is drawn in the paired colour"), zeroPaired);
        string withPair = Row(mixed, NoCells(), mixedCalls, mixedCalls.Count, onFive, true);
        Check("pair: a with-pair row that draws a paired cell passes", IsPass(withPair), withPair);

        // The row compares the pose that reached the draw with the planned pose. A pose that differs fails, and a rounding error does not.
        string moved = Verdict(mixed, OffsetPose(mixedCalls, 3, 2, 0, 0.25f, 0f, 0f), onFive);
        Check("pose: a plane that reached the draw a quarter cell away fails the row", IsFail(moved) && moved.Contains("reached Graphics.DrawMesh"), moved);
        string raised = Verdict(mixed, OffsetPose(mixedCalls, 3, 2, 0, 0f, 0.5f, 0f), onFive);
        Check("pose: a plane raised half a unit fails the row", IsFail(raised) && raised.Contains("reached Graphics.DrawMesh"), raised);
        string wrongFacing = Verdict(mixed, WithPoseOf(mixedCalls, 3, 2, 0, OutlineRule.PlannedPose(3, 2, 1, Altitude)), onFive);
        Check("pose: a plane that reached the draw at another side's pose fails the row",
            IsFail(wrongFacing) && wrongFacing.Contains("reached Graphics.DrawMesh"), wrongFacing);
        string rounding = Verdict(mixed, OffsetPose(mixedCalls, 3, 2, 0, 0.000001f, 0f, 0f), onFive);
        Check("pose: a rounding error of a millionth passes", IsPass(rounding), rounding);
        string sign = Verdict(mixed, NegatedRotation(mixedCalls, 3, 2, 0), onFive);
        Check("pose: the opposite quaternion sign is the same rotation and passes", IsPass(sign), sign);

        // The split is one function. The centre is the only paired cell of the mixed plus.
        List<OutlineCell> splitPaired;
        List<OutlineCell> splitUnpaired;
        OutlineRule.SplitStates(mixed, out splitPaired, out splitUnpaired);
        Check("split: the centre is the only paired cell, and the arms are the unpaired cells",
            splitPaired.Count == 1 && splitPaired[0].X == 2 && splitPaired[0].Z == 2 && splitUnpaired.Count == 4,
            "paired " + splitPaired.Count + ", unpaired " + splitUnpaired.Count);

        // Duplicates in the placed cells. A cell listed twice counts once, and its first state sets it.
        var doubled = new List<OutlineCell> { C(2, 2), C(2, 2), C(2, 1), C(1, 2), C(3, 2), C(2, 3), C(2, 1) };
        int doubledPlanes = Expected(doubled, onFive).Count;
        Check("duplicates: a placed cell listed twice counts once", doubledPlanes == 12, "planes " + doubledPlanes);
        var conflicting = new List<OutlineCell> { C(2, 2, true), C(2, 2, false), C(2, 1), C(1, 2), C(3, 2), C(2, 3) };
        List<OutlineDraw> conflictExpected = Expected(conflicting, onFive);
        Check("duplicates: the first state listed for a cell sets it", conflictExpected.Count == 20 && AllPaired(conflictExpected, 2, 2),
            "planes " + conflictExpected.Count);

        // Corners.
        int corner1 = Expected(new List<OutlineCell> { C(0, 0) }, onFive).Count;
        Check("corner: a lone corner cell draws two planes", corner1 == 2, "planes " + corner1);
        int cornerPair = Expected(new List<OutlineCell> { C(0, 0), C(1, 0) }, onFive).Count;
        Check("corner: a corner with a listed neighbour draws three planes in all", cornerPair == 3, "planes " + cornerPair);
        int opposite = Expected(new List<OutlineCell> { C(0, 0), C(4, 4) }, onFive).Count;
        Check("corner: opposite corners draw four planes in all", opposite == 4, "planes " + opposite);
        int oneByOne = Expected(new List<OutlineCell> { C(0, 0) }, Square(1)).Count;
        Check("corner: a one by one map draws nothing", oneByOne == 0, "planes " + oneByOne);

        // Negative coordinates. The map runs from -2 to 2, so the cells on its low side have negative neighbours.
        Func<int, int, bool> offset = Box(-2, 2);
        var offsetPlus = new List<OutlineCell> { C(-1, -1), C(-1, -2), C(-2, -1), C(0, -1), C(-1, 0) };
        List<OutlineDraw> offsetExpected = Expected(offsetPlus, offset);
        Check("negative: the plus centred at -1,-1 draws no plane on its centre",
            CountFor(offsetExpected, -1, -1) == 0 && CountFor(offsetExpected, -1, -2) > 0 && CountFor(offsetExpected, 0, -1) > 0,
            "planes " + offsetExpected.Count);
        var negativeOne = new List<OutlineCell> { C(-2, -1) };
        string keysApart = Verdict(negativeOne, DrawnFor(new List<OutlineCell> { C(-1, -2) }, Anywhere()), Anywhere());
        Check("negative: cells -2,-1 and -1,-2 are different cells", IsFail(keysApart), keysApart);

        // Enclosure. A two by two block draws its outer sides only, eight planes over four cells.
        var block = new List<OutlineCell> { C(1, 1), C(2, 1), C(1, 2), C(2, 2) };
        int blockPlanes = Expected(block, onFive).Count;
        Check("enclosure: a two by two block draws eight planes over four cells", blockPlanes == 8, "planes " + blockPlanes);

        Console.WriteLine("checks: " + checks + ", failures: " + failures);
        return failures;
    }
}
