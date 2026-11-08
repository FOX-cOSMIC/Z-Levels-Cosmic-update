using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 31 (haul to a floor, idea "haul to a floor" on docs/mod-comparison/ideas.html). The player marks surface steel for the lower floor with
    // the haul-to-floor designator. A colonist on the lower floor then hauls that steel into the lower stockpile. A second surface steel stack is
    // not marked, and it must not move. The designator is reached by name, so this file builds before the designator exists, and the first row
    // fails until it does.
    public partial class RuntimeValidation
    {
        private int hauledStage;
        private int hauledDueTick;
        private bool hauledDone;
        private Thing hauledMarked;
        private Thing hauledUnmarked;
        private IntVec3 hauledUnmarkedStart;
        private Map hauledSurface;
        private Map hauledLower;
        private Pawn hauledColonist;
        private string hauledLastTrace = "";
        private int hauledNextBeat;
        private string hauledClickRow = "";
        private ZLevelsManager hauledTracker;

        // The game ticks this many times before the marked steel must be stored on the lower floor.
        private const int HaulToFloorTicks = 6000;

        // The designator's marking method. It is reached by name, so a missing designator is a failing row, not a build error.
        private static int MarkItemsForFloorByName(IEnumerable<Thing> items, int floorIndex)
        {
            Type designator = typeof(FloorPolicy).Assembly.GetType("ZLevels.Designator_HaulToFloor")
                ?? throw new InvalidOperationException("the mod has no ZLevels.Designator_HaulToFloor class");
            System.Reflection.MethodInfo method = designator.GetMethod("MarkItemsForFloor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("Designator_HaulToFloor has no public static MarkItemsForFloor");
            try
            {
                return (int)method.Invoke(null, new object[] { items.ToList(), floorIndex });
            }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }

        private static Thing SpawnStackOn(Map map, IntVec3 cell, ThingDef def)
        {
            ClearCell(map, cell);
            Thing stack = ThingMaker.MakeThing(def);
            stack.stackCount = 10;
            return GenSpawn.Spawn(stack, cell, map);
        }

        private void SetUpHaulToFloor()
        {
            hauledSurface = Find.CurrentMap;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            hauledTracker = tracker;
            RequireFloors(tracker, hauledSurface, 1);
            Building_StairsUp stairs = SetUpLowerStairs(hauledSurface, tracker, out hauledLower);
            hauledLower.fogGrid.ClearAllFog();
            RequireFloors(tracker, hauledSurface, 2);

            // A clear block on the surface holds the marked steel and the unmarked log. The block is cleared the way the background haul scenario clears its block.
            IntVec3 block = FindStandableBlock(hauledSurface, 9, 9);
            if (!block.IsValid)
            {
                throw new InvalidOperationException("no standable 9 by 9 block on the surface away from the edges");
            }
            for (int i = 0; i < 9; i++)
            {
                for (int j = 0; j < 9; j++)
                {
                    ClearCell(hauledSurface, block + new IntVec3(i, 0, j));
                }
            }

            // The open ground around the stairs, so the stairs stay reachable from the lower floor. The stockpile sits five cells from the stairs. The
            // carve is centred on the stairs, because a carve that reached the stairs cell would clear the stairs up: ClearCell removes what it clears.
            CarveAround(hauledLower, stairs.Position, 6f);
            IntVec3 stockNear = OnMapCell(hauledLower, stairs.Position + new IntVec3(5, 0, 0));
            Zone_Stockpile stock = MakeSupplyZone(hauledLower, stockNear);
            // The lower stockpile takes steel only. The background scan marks every item that the lower floor needs, so a second steel stack would be
            // hauled down as well. The unmarked item is therefore a wood log, which the lower floor does not need.
            stock.settings.filter.SetDisallowAll();
            stock.settings.filter.SetAllow(ThingDefOf.Steel, true);
            if (stock.Cells.Count == 0)
            {
                throw new InvalidOperationException("no stockpile cells on the lower floor");
            }
            if (!hauledLower.listerThings.AllThings.OfType<Building_StairsUp>().Any())
            {
                throw new InvalidOperationException("the lower floor has no stairs up after its stockpile is carved");
            }

            hauledMarked = SpawnStackOn(hauledSurface, block + new IntVec3(7, 0, 7), ThingDefOf.Steel);
            hauledUnmarked = SpawnStackOn(hauledSurface, block + new IntVec3(7, 0, 2), ThingDefOf.WoodLog);
            hauledUnmarkedStart = hauledUnmarked.Position;

            // A lower colonist that hauls. Mining is set to zero, so hauling is the only work it takes.
            hauledColonist = SpawnCapableColonistNear(hauledLower, stockNear, new List<IntVec3>(), WorkTypeDefOf.Hauling);
            SetWork(hauledColonist, WorkTypeDefOf.Hauling, 3, WorkTypeDefOf.Mining, 0);

            // The designator marks the first steel for the lower floor. The second steel is never marked.
            int lowerZ = tracker.GetZIndexFor(hauledLower);
            int marked = MarkItemsForFloorByName(new List<Thing> { hauledMarked }, lowerZ);
            Record("designator result", "INFO: the designator marked " + marked + " item for the lower floor (index " + lowerZ + ")");

            // A single click on an empty cell. The game sends a click through DesignateSingleCell, not the drag method (DesignatorManager.cs, line 101).
            hauledClickRow = ClickEmptyCellRow(block);
            Record("a click on a cell with no loose item does nothing and does not throw", hauledClickRow);

            hauledStage = 1;
            hauledDueTick = Find.TickManager.TicksGame + HaulToFloorTicks;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        }

        // A click on a cell with no loose item must open no menu and must not throw. The base Designator class throws NotImplementedException from
        // DesignateSingleCell, so a designator without its own override fails this row.
        private static string ClickEmptyCellRow(IntVec3 cell)
        {
            try
            {
                Type designator = typeof(FloorPolicy).Assembly.GetType("ZLevels.Designator_HaulToFloor")
                    ?? throw new InvalidOperationException("the mod has no ZLevels.Designator_HaulToFloor class");
                object instance = Activator.CreateInstance(designator);
                System.Reflection.MethodInfo click = designator.GetMethod("DesignateSingleCell", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    ?? throw new InvalidOperationException("Designator_HaulToFloor has no public DesignateSingleCell");
                click.Invoke(instance, new object[] { cell });
                return "PASS";
            }
            catch (System.Reflection.TargetInvocationException exception)
            {
                return "FAIL: " + (exception.InnerException ?? exception).Message;
            }
            catch (Exception exception)
            {
                return "FAIL: " + exception.Message;
            }
        }

        private void RunHaulToFloorTick()
        {
            if (hauledDone)
            {
                return;
            }
            try
            {
                if (hauledStage == 0)
                {
                    SetUpHaulToFloor();
                    return;
                }
                string trace = DescribeJob(hauledColonist.CurJob) + " at " + hauledColonist.Position + " on " + hauledColonist.MapHeld
                    + "; the marked steel is on " + hauledMarked.MapHeld + ", in storage " + hauledMarked.IsInAnyStorage();
                if (trace != hauledLastTrace)
                {
                    hauledLastTrace = trace;
                    Record("job trace", "INFO: " + trace);
                }
                // A heartbeat every 300 ticks: the speed, the colonist's floor, and what the stairs registry holds for the surface.
                if (Find.TickManager.TicksGame >= hauledNextBeat)
                {
                    hauledNextBeat = Find.TickManager.TicksGame + 300;
                    int surfaceDown = hauledTracker.GetStairsDownFor(hauledSurface).Count;
                    int surfaceOnMap = hauledSurface.listerThings.AllThings.OfType<Building_StairsDown>().Count();
                    bool lowerKnown = hauledTracker.GetLowerLevel(hauledSurface.Tile, hauledSurface) != null;
                    string stairsCheck = "no stairs down on the surface";
                    Building_StairsDown surfaceStairs = hauledSurface.listerThings.AllThings.OfType<Building_StairsDown>().FirstOrDefault();
                    if (surfaceStairs != null)
                    {
                        IntVec3 p = surfaceStairs.Position;
                        stairsCheck = "stairs down at " + p + ": Walkable " + p.Walkable(hauledSurface)
                            + ", normal path grid " + hauledSurface.pathing.Normal.pathGrid.Walkable(p)
                            + ", fence path grid " + hauledSurface.pathing.FenceBlocked.pathGrid.Walkable(p)
                            + ", terrain " + p.GetTerrain(hauledSurface).defName
                            + ", things " + string.Join(", ", p.GetThingList(hauledSurface).Select(t => t.def.defName + " " + t.def.passability));
                    }
                    string lowerCheck = "no stairs up on the lower floor";
                    Building_StairsUp lowerStairs = hauledLower.listerThings.AllThings.OfType<Building_StairsUp>().FirstOrDefault();
                    if (lowerStairs != null)
                    {
                        IntVec3 q = lowerStairs.Position;
                        lowerCheck = "stairs up at " + q + ": Walkable " + q.Walkable(hauledLower)
                            + ", normal path grid " + hauledLower.pathing.Normal.pathGrid.Walkable(q)
                            + ", terrain " + q.GetTerrain(hauledLower).defName
                            + ", things " + string.Join(", ", q.GetThingList(hauledLower).Select(t => t.def.defName + " " + t.def.passability))
                            + ", registry up " + hauledTracker.GetStairsUpFor(hauledLower).Count;
                    }
                    Record("heartbeat", "INFO: tick " + Find.TickManager.TicksGame + ", speed " + Find.TickManager.CurTimeSpeed
                        + ", the colonist's floor index " + hauledTracker.GetZIndexFor(hauledColonist.Map)
                        + ", surface stairs down in the registry " + surfaceDown + " and on the map " + surfaceOnMap
                        + ", lower level known from the surface " + lowerKnown + "; " + stairsCheck + "; " + lowerCheck + "; " + trace);
                }
                bool arrived = hauledMarked.MapHeld == hauledLower && hauledMarked.IsInAnyStorage();
                if (arrived || Find.TickManager.TicksGame > hauledDueTick)
                {
                    FinishHaulToFloor(arrived);
                }
            }
            catch (Exception exception)
            {
                hauledDone = true;
                Record("marked item ends on the chosen floor", "FAIL: " + exception.Message);
                Record("an unmarked item stays where it was", "FAIL: not checked, the setup or the designator failed");
                Record("haul to a floor", "FAIL: " + exception.Message);
                Log.Error("Z-Levels haul to a floor validation failed: " + exception);
                finished = true;
                Root.Shutdown();
            }
        }

        private void FinishHaulToFloor(bool arrived)
        {
            hauledDone = true;
            Record("marked item ends on the chosen floor", arrived
                ? "PASS: the marked steel is in storage on the lower floor"
                : "FAIL: after " + HaulToFloorTicks + " ticks the marked steel is on " + hauledMarked.MapHeld + ", in storage " + hauledMarked.IsInAnyStorage()
                    + "; the job is " + DescribeJob(hauledColonist.CurJob));
            bool unmoved = hauledUnmarked.MapHeld == hauledSurface && hauledUnmarked.Position == hauledUnmarkedStart;
            Record("an unmarked item stays where it was", unmoved
                ? "PASS"
                : "FAIL: the unmarked log is on " + hauledUnmarked.MapHeld + " at " + hauledUnmarked.Position + ", it started at " + hauledUnmarkedStart);
            Record("haul to a floor", arrived && unmoved && hauledClickRow == "PASS" ? "PASS" : "FAIL: a row above failed");
            finished = true;
            Root.Shutdown();
        }
    }
}
