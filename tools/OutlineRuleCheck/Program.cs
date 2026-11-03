using System;
using System.Collections.Generic;
using ZLevelsRimatomics;

// Usage, from the repository root: dotnet run --project tools/OutlineRuleCheck
// Runs the outline rule and the drawn-row verdict on small grids, with no game types. Each check prints PASS or FAIL. The exit code is the number of
// checks that failed, so 0 means every check passed.
static class Program
{
    static int checks;
    static int failures;

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

    // The mask for one cell of a list of tuples on a square map, the same way the hook asks for it.
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

    static bool HasCell(IList<OutlineCell> cells, int x, int z)
    {
        foreach (OutlineCell cell in cells) if (cell.X == x && cell.Z == z) return true;
        return false;
    }

    static bool Has(IList<OutlineCell> cells, int x, int z, bool paired)
    {
        foreach (OutlineCell cell in cells) if (cell.X == x && cell.Z == z && cell.Paired == paired) return true;
        return false;
    }

    // The planes that a list of placed cells draws. Each colour is checked against its own cells, as the hook checks it.
    static int PlanesIn(IList<OutlineCell> cells, Func<int, int, bool> onMap)
    {
        int planes = 0;
        foreach (OutlineCell cell in cells)
        {
            var sameColour = new List<OutlineCell>();
            foreach (OutlineCell other in cells) if (other.Paired == cell.Paired) sameColour.Add(other);
            planes += Planes(OutlineRule.OpenSides(cell.X, cell.Z, onMap, (x, z) => HasCell(sameColour, x, z)));
        }
        return planes;
    }

    // The plus shape: a centre and its four arms, on a 5 by 5 map. The centre is enclosed by its arms when they share its colour.
    static List<OutlineCell> Plus(bool centrePaired = false)
    {
        return new List<OutlineCell> { C(2, 2, centrePaired), C(2, 1), C(1, 2), C(3, 2), C(2, 3) };
    }

    static bool IsFail(string verdict)
    {
        return verdict.StartsWith("FAIL", StringComparison.Ordinal);
    }

    static int Main()
    {
        // Single cells and enclosure, with tuples.
        var lone = new List<(int, int)> { (2, 2) };
        int loneMask = MaskFor(2, 2, 5, lone);
        Check("lone cell draws a plane on each side", loneMask == 15, "mask " + loneMask + ", planes " + Planes(loneMask));

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

        // The plus shape with one colour: the four arms are expected, and the enclosed centre is not.
        var plus = Plus();
        Func<int, int, bool> onFive = Square(5);
        List<OutlineCell> expected = OutlineRule.ExpectedCells(plus, onFive);
        Check("plus: expected cells are the four arms", expected.Count == 4 && HasCell(expected, 2, 1) && HasCell(expected, 1, 2) && HasCell(expected, 3, 2) && HasCell(expected, 2, 3) && !HasCell(expected, 2, 2), "count " + expected.Count);
        int plusPlanes = PlanesIn(plus, onFive);
        Check("plus: the arms draw 12 planes", plusPlanes == 12, "planes " + plusPlanes);
        string plusVerdict = OutlineRule.DrawnRowVerdict(expected, expected, plusPlanes);
        Check("plus: the drawn row passes", plusVerdict == "PASS: 4 cells of 4 expected, 12 planes, 0 paired", plusVerdict);

        // Colours. The centre is green, the arms are amber. Each arm is checked against the other arms, so each draws all four sides.
        var mixed = Plus(centrePaired: true);
        List<OutlineCell> mixedExpected = OutlineRule.ExpectedCells(mixed, onFive);
        int mixedPlanes = PlanesIn(mixed, onFive);
        Check("colour: the green centre and the amber arms are all expected", mixedExpected.Count == 5 && Has(mixedExpected, 2, 2, true) && Has(mixedExpected, 2, 1, false) && Has(mixedExpected, 1, 2, false) && Has(mixedExpected, 3, 2, false) && Has(mixedExpected, 2, 3, false), "count " + mixedExpected.Count);
        Check("colour: the mixed plus draws 20 planes", mixedPlanes == 20, "planes " + mixedPlanes);
        string rightColours = OutlineRule.DrawnRowVerdict(mixedExpected, mixedExpected, mixedPlanes);
        Check("colour: the right colours pass and count one green cell", rightColours == "PASS: 5 cells of 5 expected, 20 planes, 1 paired", rightColours);

        var centreAmber = new List<OutlineCell>(mixedExpected);
        for (int i = 0; i < centreAmber.Count; i++) if (centreAmber[i].X == 2 && centreAmber[i].Z == 2) centreAmber[i] = C(2, 2, false);
        string centreVerdict = OutlineRule.DrawnRowVerdict(mixedExpected, centreAmber, mixedPlanes);
        Check("colour: the green centre drawn amber fails the row", IsFail(centreVerdict), centreVerdict);

        var armGreen = new List<OutlineCell>(mixedExpected);
        for (int i = 0; i < armGreen.Count; i++) if (armGreen[i].X == 3 && armGreen[i].Z == 2) armGreen[i] = C(3, 2, true);
        string armVerdict = OutlineRule.DrawnRowVerdict(mixedExpected, armGreen, mixedPlanes);
        Check("colour: an amber arm drawn green fails the row", IsFail(armVerdict), armVerdict);

        // Duplicates. A placed cell listed twice counts once. A drawn cell listed twice does not change the verdict.
        var doubled = new List<OutlineCell> { C(2, 2), C(2, 2), C(2, 1), C(1, 2), C(3, 2), C(2, 3), C(2, 1) };
        List<OutlineCell> doubledExpected = OutlineRule.ExpectedCells(doubled, onFive);
        Check("duplicates: a placed cell listed twice counts once", doubledExpected.Count == 4, "count " + doubledExpected.Count);
        var drawnTwice = new List<OutlineCell>(expected) { expected[0] };
        string twice = OutlineRule.DrawnRowVerdict(expected, drawnTwice, plusPlanes);
        Check("duplicates: a drawn cell listed twice does not change the verdict", twice == "PASS: 4 cells of 4 expected, 12 planes, 0 paired", twice);
        var conflicting = new List<OutlineCell> { C(2, 2, true), C(2, 2, false), C(2, 1), C(1, 2), C(3, 2), C(2, 3) };
        List<OutlineCell> conflictExpected = OutlineRule.ExpectedCells(conflicting, onFive);
        Check("duplicates: the first colour listed for a cell sets it", conflictExpected.Count == 5 && Has(conflictExpected, 2, 2, true) && !Has(conflictExpected, 2, 2, false), "count " + conflictExpected.Count);

        // Corners.
        List<OutlineCell> corner1 = OutlineRule.ExpectedCells(new List<OutlineCell> { C(0, 0) }, onFive);
        int corner1Planes = PlanesIn(new List<OutlineCell> { C(0, 0) }, onFive);
        Check("corner: a corner cell alone is expected and draws two planes", corner1.Count == 1 && corner1Planes == 2, "expected " + corner1.Count + ", planes " + corner1Planes);
        var cornerPair = new List<OutlineCell> { C(0, 0), C(1, 0) };
        int cornerPairPlanes = PlanesIn(cornerPair, onFive);
        Check("corner: a corner with a listed neighbour draws three planes in all", OutlineRule.ExpectedCells(cornerPair, onFive).Count == 2 && cornerPairPlanes == 3, "planes " + cornerPairPlanes);
        var opposite = new List<OutlineCell> { C(0, 0), C(4, 4) };
        Check("corner: opposite corners draw four planes in all", PlanesIn(opposite, onFive) == 4, "planes " + PlanesIn(opposite, onFive));
        Check("corner: a one by one map draws nothing", OutlineRule.ExpectedCells(new List<OutlineCell> { C(0, 0) }, Square(1)).Count == 0, "expected count " + OutlineRule.ExpectedCells(new List<OutlineCell> { C(0, 0) }, Square(1)).Count);

        // Negative coordinates. The map here runs from -2 to 2, so the cells on its low side have negative neighbours.
        Func<int, int, bool> offset = Box(-2, 2);
        List<OutlineCell> offsetPlus = new List<OutlineCell> { C(-1, -1), C(-1, -2), C(-2, -1), C(0, -1), C(-1, 0) };
        List<OutlineCell> offsetExpected = OutlineRule.ExpectedCells(offsetPlus, offset);
        Check("negative: a plus centred at -1,-1 expects its four arms", offsetExpected.Count == 4 && !HasCell(offsetExpected, -1, -1) && HasCell(offsetExpected, -1, -2), "count " + offsetExpected.Count);
        var negativeOne = new List<OutlineCell> { C(-2, -1) };
        string keysApart = OutlineRule.DrawnRowVerdict(OutlineRule.ExpectedCells(negativeOne, Anywhere()), new List<OutlineCell> { C(-1, -2) }, 4);
        Check("negative: cells (-2,-1) and (-1,-2) are different cells", IsFail(keysApart), keysApart);

        // Enclosure. A two by two block draws its outer sides only.
        var block = new List<OutlineCell> { C(1, 1), C(2, 1), C(1, 2), C(2, 2) };
        Check("enclosure: a two by two block draws eight planes over four cells", OutlineRule.ExpectedCells(block, onFive).Count == 4 && PlanesIn(block, onFive) == 8, "planes " + PlanesIn(block, onFive));

        // The verdict's failure cases.
        string nothing = OutlineRule.DrawnRowVerdict(new List<OutlineCell>(), expected, plusPlanes);
        Check("verdict: nothing expected fails", IsFail(nothing), nothing);
        string noPlanes = OutlineRule.DrawnRowVerdict(expected, expected, 0);
        Check("verdict: matching cells with no planes fail", IsFail(noPlanes), noPlanes);
        var swapped = new List<OutlineCell> { C(2, 1), C(1, 2), C(3, 2), C(2, 2) };
        string swappedVerdict = OutlineRule.DrawnRowVerdict(expected, swapped, plusPlanes);
        Check("verdict: the same count with one wrong cell fails", IsFail(swappedVerdict), swappedVerdict);
        string missing = OutlineRule.DrawnRowVerdict(expected, new List<OutlineCell> { C(2, 1), C(1, 2), C(3, 2) }, 9);
        Check("verdict: a missing arm fails", IsFail(missing), missing);
        var extra = new List<OutlineCell>(expected) { C(4, 4) };
        string extraVerdict = OutlineRule.DrawnRowVerdict(expected, extra, plusPlanes + 4);
        Check("verdict: an extra cell fails", IsFail(extraVerdict), extraVerdict);

        Console.WriteLine("checks: " + checks + ", failures: " + failures);
        return failures;
    }
}
