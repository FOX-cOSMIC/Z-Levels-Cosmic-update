using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 37 (idea "pipes between floors" on docs/mod-comparison/ideas.html; decision D4, answered 2026-11-06: one chosen resource per pipe, at a fixed
    // rate). The pipe, its chosen resource and its flow are reached by name and reflection, so this file builds before they exist, and each row FAILs
    // until its part does. The scenario is synchronous. A game day runs as 240 steps of 250 ticks through the pipe's Step method, as the game's rare
    // tick would call it.
    //   A pipe is placed on the lower floor, and its other end must stand on the surface (row 1).
    //   Steel stored on the lower floor flows up at 60 a day (row 2). Wood stored there does not move through a steel pipe (row 3).
    //   A flipped pipe sends steel stored on the surface down (row 4).
    //   Removing one end removes the other (row 5). The resource gizmo sets both ends (row 6).
    //   The placement check refuses a pipe on the top floor and a pipe under a wall, and allows one with a clear cell above (rows 7 to 9).
    public partial class RuntimeValidation
    {
        private const string FloorPipeDefName = "ZL_FloorPipe";

        // The six rows, in the order they run. Each is recorded under its label.
        private static readonly string[] FloorPipeRowLabels =
        {
            "floor pipe places its other end above",
            "steel moves up at 60 a day",
            "only the chosen resource moves",
            "flip direction sends the other way",
            "removing one end removes the other",
            "resource gizmo sets both ends",
            "pipe refused on the top floor",
            "pipe refused where the cell above is blocked",
            "pipe allowed where the cell above is clear",
            "pipe on the lowest of three floors has one other end",
        };

        // The pipe class, by name. A missing class throws, so the row reads FAIL with the reason.
        private static Type FloorPipeType()
        {
            return typeof(FloorPolicy).Assembly.GetType("ZLevels.Building_FloorPipe")
                ?? throw new InvalidOperationException("the mod has no ZLevels.Building_FloorPipe class");
        }

        // The pipe def, by name. A missing def throws.
        private static ThingDef FloorPipeDef()
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(FloorPipeDefName)
                ?? throw new InvalidOperationException("the mod has no " + FloorPipeDefName + " def");
        }

        // The pipe standing on the cell, or null.
        private static Thing FloorPipeAt(Map map, IntVec3 cell)
        {
            return map.thingGrid.ThingsListAt(cell).FirstOrDefault(thing => thing.def.defName == FloorPipeDefName);
        }

        // Spawns a pipe with the game's own spawn, which runs its SpawnSetup and so makes the other end.
        private static Thing SpawnFloorPipe(Map map, IntVec3 cell)
        {
            Thing pipe = ThingMaker.MakeThing(FloorPipeDef());
            GenSpawn.Spawn(pipe, cell, map, Rot4.North);
            if (!pipe.Spawned)
            {
                throw new InvalidOperationException("the pipe did not spawn at " + cell);
            }
            return pipe;
        }

        // Calls a public instance method of the pipe class by name. A missing method throws.
        private static object CallFloorPipe(Thing pipe, string name, params object[] args)
        {
            MethodInfo method = FloorPipeType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Building_FloorPipe has no public " + name);
            try
            {
                return method.Invoke(pipe, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }

        // A field or property of the pipe by name, public or not. A missing member throws.
        private static object FloorPipeValue(Thing pipe, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            System.Reflection.FieldInfo field = FloorPipeType().GetField(name, flags);
            if (field != null)
            {
                return field.GetValue(pipe);
            }
            System.Reflection.PropertyInfo property = FloorPipeType().GetProperty(name, flags);
            if (property != null)
            {
                return property.GetValue(pipe, null);
            }
            throw new InvalidOperationException("Building_FloorPipe has no " + name);
        }

        // One game day for a pipe: 240 steps of the rare-tick interval, through its Step method.
        private static void RunFloorPipeDay(Thing pipe)
        {
            for (int ticks = 0; ticks < GenDate.TicksPerDay; ticks += GenTicks.TickRareInterval)
            {
                CallFloorPipe(pipe, "Step", GenTicks.TickRareInterval);
            }
        }

        // The number of items of the def stored in the zone.
        private static int StoredInZone(Map map, Zone_Stockpile zone, ThingDef def)
        {
            return zone.Cells.Sum(cell => map.thingGrid.ThingsListAt(cell).Where(thing => thing.def == def).Sum(thing => thing.stackCount));
        }

        // The number of items of the def on the cells of a rectangle that starts at the origin.
        private static int ItemsIn(Map map, ThingDef def, IntVec3 origin, int width, int depth)
        {
            int count = 0;
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < depth; z++)
                {
                    foreach (Thing thing in map.thingGrid.ThingsListAt(WayUpCell(origin, x, z)))
                    {
                        if (thing.def == def)
                        {
                            count += thing.stackCount;
                        }
                    }
                }
            }
            return count;
        }

        // Runs one row's check and records its result. A check that throws records its reason as the FAIL.
        private void RecordFloorPipeRow(List<string> rows, string label, Func<string> check)
        {
            string result;
            try
            {
                result = check();
            }
            catch (Exception exception)
            {
                result = "FAIL: " + exception.Message;
            }
            rows.Add(result);
            Record(label, result);
        }

        private void RunFloorPipeCheck()
        {
            const string aggregate = "floor pipe";
            List<string> rows = new List<string>();
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                Map lower = tracker.GetLowerLevel(surface.Tile, surface) ?? CreateLevelBelow(tracker, surface);
                RequireFloors(tracker, surface, 2);
                surface.fogGrid.ClearAllFog();
                lower.fogGrid.ClearAllFog();
                IntVec3 block = FindStandableBlock(surface, WayUpsWidth, WayUpsDepth);
                if (!block.IsValid)
                {
                    throw new InvalidOperationException("no standable 16 by 8 block on the surface away from the edges");
                }
                ClearWayUpsBlock(surface, block);
                ClearWayUpsBlock(lower, block);
                ThingDef steel = ThingDefOf.Steel;
                ThingDef wood = ThingDefOf.WoodLog;
                Zone_Stockpile lowerZone = MakeSupplyZone(lower, WayUpCell(block, 3, 6));
                IntVec3 firstCell = WayUpCell(block, 1, 1);
                Thing lowerPipe = null;

                RecordFloorPipeRow(rows, "floor pipe places its other end above", () =>
                {
                    lowerPipe = SpawnFloorPipe(lower, firstCell);
                    return FloorPipeAt(surface, firstCell) != null
                        ? "PASS: a pipe stands on the floor above at " + firstCell
                        : "FAIL: no pipe on the floor above at " + firstCell;
                });
                RecordFloorPipeRow(rows, "steel moves up at 60 a day", () =>
                {
                    if (lowerPipe == null)
                    {
                        throw new InvalidOperationException("the first row placed no pipe");
                    }
                    PlaceSupplyStack(lower, lowerZone, steel, 60);
                    RunFloorPipeDay(lowerPipe);
                    int left = StoredInZone(lower, lowerZone, steel);
                    int landed = ItemsIn(surface, steel, block, WayUpsWidth, WayUpsDepth);
                    return left == 0 && landed == 60
                        ? "PASS: 60 steel left the lower zone and 60 landed on the floor above"
                        : "FAIL: " + left + " steel is still in the lower zone and " + landed + " landed above";
                });
                IntVec3 thirdCell = WayUpCell(block, 7, 1);
                RecordFloorPipeRow(rows, "only the chosen resource moves", () =>
                {
                    Thing pipe = SpawnFloorPipe(lower, thirdCell);
                    PlaceSupplyStack(lower, lowerZone, wood, 60);
                    RunFloorPipeDay(pipe);
                    int woodStored = StoredInZone(lower, lowerZone, wood);
                    int woodAbove = ItemsIn(surface, wood, block, WayUpsWidth, WayUpsDepth);
                    return woodStored == 60 && woodAbove == 0
                        ? "PASS: the 60 wood stayed in the lower zone under a steel pipe"
                        : "FAIL: " + woodStored + " wood is left in the lower zone and " + woodAbove + " landed above";
                });
                IntVec3 flipCell = WayUpCell(block, 10, 1);
                RecordFloorPipeRow(rows, "flip direction sends the other way", () =>
                {
                    Thing lowerEnd = SpawnFloorPipe(lower, flipCell);
                    Thing upperEnd = FloorPipeAt(surface, flipCell)
                        ?? throw new InvalidOperationException("no pipe on the floor above at " + flipCell);
                    CallFloorPipe(lowerEnd, "FlipDirection");
                    Zone_Stockpile surfaceZone = MakeSupplyZone(surface, WayUpCell(block, 11, 6));
                    PlaceSupplyStack(surface, surfaceZone, steel, 30);
                    RunFloorPipeDay(upperEnd);
                    int left = StoredInZone(surface, surfaceZone, steel);
                    int landed = ItemsIn(lower, steel, WayUpCell(block, 9, 0), 3, 3);
                    return left == 0 && landed == 30
                        ? "PASS: 30 steel left the surface zone and 30 landed below the flipped pipe"
                        : "FAIL: " + left + " steel is still in the surface zone and " + landed + " landed below";
                });
                RecordFloorPipeRow(rows, "removing one end removes the other", () =>
                {
                    if (lowerPipe == null)
                    {
                        throw new InvalidOperationException("the first row placed no pipe");
                    }
                    lowerPipe.Destroy(DestroyMode.Vanish);
                    return FloorPipeAt(surface, firstCell) == null
                        ? "PASS: the pipe above went with its end"
                        : "FAIL: the pipe above stays after its end was removed";
                });
                RecordFloorPipeRow(rows, "resource gizmo sets both ends", () =>
                {
                    Thing pipe = FloorPipeAt(lower, thirdCell)
                        ?? throw new InvalidOperationException("no pipe on the lower floor at " + thirdCell);
                    CallFloorPipe(pipe, "SetResource", wood);
                    Thing other = FloorPipeAt(surface, thirdCell)
                        ?? throw new InvalidOperationException("no pipe on the floor above at " + thirdCell);
                    object resource = FloorPipeValue(other, "Resource");
                    return Equals(resource, wood)
                        ? "PASS: the end above holds WoodLog too"
                        : "FAIL: the end above holds " + (resource == null ? "no resource" : resource.ToString());
                });
                // The placement check (PlaceWorker_FloorPipe, decision DM17) is asked through the game's own blueprint check.
                RecordFloorPipeRow(rows, "pipe refused on the top floor", () =>
                {
                    string refusal = PlacementRefusal(FloorPipeDef(), WayUpCell(block, 14, 1), surface);
                    return refusal == PlaceWorker_FloorPipe.NoFloorAboveMessage
                        ? "PASS: the top floor refuses a pipe with its message"
                        : "FAIL: the top floor " + DescribeRefusal(refusal, PlaceWorker_FloorPipe.NoFloorAboveMessage);
                });
                RecordFloorPipeRow(rows, "pipe refused where the cell above is blocked", () =>
                {
                    Thing wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), WayUpCell(block, 14, 4), surface);
                    try
                    {
                        string refusal = PlacementRefusal(FloorPipeDef(), WayUpCell(block, 14, 4), lower);
                        return refusal == PlaceWorker_FloorPipe.BlockedAboveMessage
                            ? "PASS: a wall above refuses a pipe with its message"
                            : "FAIL: the lower floor " + DescribeRefusal(refusal, PlaceWorker_FloorPipe.BlockedAboveMessage);
                    }
                    finally
                    {
                        wall.Destroy(DestroyMode.Vanish);
                    }
                });
                RecordFloorPipeRow(rows, "pipe allowed where the cell above is clear", () =>
                {
                    string refusal = PlacementRefusal(FloorPipeDef(), WayUpCell(block, 14, 1), lower);
                    return refusal == null
                        ? "PASS: a clear cell above takes a pipe"
                        : "FAIL: the pipe was refused: " + refusal;
                });
                // Three floors: a pipe on the lowest one pairs with the middle one only. The top floor is made here, after the rows above, so their
                // two-floor layout does not change. A pipe that also stands on the top floor means the other end made an end of its own.
                RecordFloorPipeRow(rows, "pipe on the lowest of three floors has one other end", () =>
                {
                    Map top = tracker.CreateUpperLevel(surface, surface.Center);
                    top.fogGrid.ClearAllFog();
                    ClearWayUpsBlock(top, block);
                    IntVec3 cell = WayUpCell(block, 14, 7);
                    SpawnFloorPipe(lower, cell);
                    bool middle = FloorPipeAt(surface, cell) != null;
                    bool topFloor = FloorPipeAt(top, cell) != null;
                    return middle && !topFloor
                        ? "PASS: one other end on the middle floor at " + cell + ", none on the top floor"
                        : "FAIL: the middle floor has a pipe " + middle + " and the top floor has a pipe " + topFloor + ", at " + cell;
                });
        // Four floors: a pair from the lowest floor to the middle one, and a pair from the floor above to the top one, at one cell. The middle
        // end must pair with the pipe below it, not with the pipe above.
        RecordFloorPipeRow(rows, "two pairs stacked on four floors keep their own partners", () =>
        {
            Map topFloor = tracker.GetUpperLevel(surface.Tile, surface) ?? throw new InvalidOperationException("no top floor for the pipe check");
            Map topmost = tracker.CreateUpperLevel(topFloor, topFloor.Center);
            topmost.fogGrid.ClearAllFog();
            ClearWayUpsBlock(topmost, block);
            IntVec3 stackCell = WayUpCell(block, 14, 7);
            Thing lowestEnd = FloorPipeAt(lower, stackCell) ?? throw new InvalidOperationException("no pipe on the lowest floor at " + stackCell);
            Thing middleEnd = FloorPipeAt(surface, stackCell) ?? throw new InvalidOperationException("no pipe on the middle floor at " + stackCell);
            Thing upperSource = SpawnFloorPipe(topFloor, stackCell);
            Thing topEnd = FloorPipeAt(topmost, stackCell) ?? throw new InvalidOperationException("no pipe on the top floor at " + stackCell);
            Building_FloorPipe partner = ((Building_FloorPipe)middleEnd).Partner();
            if (partner != lowestEnd)
            {
                return "FAIL: the middle end pairs with " + (partner == null ? "no pipe" : partner.Map == topFloor ? "the pipe above" : "a pipe below") + ", not the pipe below";
            }
            middleEnd.Destroy(DestroyMode.Vanish);
            return upperSource.Spawned && topEnd.Spawned
                ? "PASS: the middle end pairs with the pipe below, and removing it leaves the pair above"
                : "FAIL: removing the middle end took the pair above with it";
        });
            }
            catch (Exception exception)
            {
                // A failure in the shared set-up fails every row that has not been recorded yet, with the reason.
                Log.Error("Z-Levels floor pipe validation failed: " + exception);
                for (int i = rows.Count; i < FloorPipeRowLabels.Length; i++)
                {
                    rows.Add("FAIL: " + exception.Message);
                    Record(FloorPipeRowLabels[i], "FAIL: " + exception.Message);
                }
            }
            Record(aggregate, rows.Count == FloorPipeRowLabels.Length && rows.All(row => row.StartsWith("PASS")) ? "PASS" : "FAIL: a row above failed");
            Root.Shutdown();
        }
    }
}
