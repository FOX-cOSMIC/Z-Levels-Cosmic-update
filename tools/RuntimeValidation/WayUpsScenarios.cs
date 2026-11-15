using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 36 (idea "more ways up" on docs/mod-comparison/ideas.html; decision D3, answered 2026-11-06: ladders and stair sizes). The new kinds are
    // reached by def name, so this file builds before they exist, and each row FAILs until its kind does. Both floors share one block of cells,
    // because a partner is found by its cell (ZLevelsManager.GetOtherEnd), so the partner of a way sits on the same cells above.
    //   Rows 1 to 3 spawn a way on the lower floor and check each of its cells on both floors: a small ladder, a large ladder, and large stairs.
    //   Row 4 calls a colonist from the lower floor to the surface. The small ladder and the large stairs are removed first, so the large ladder is
    //   the only way up and the colonist has to use it.
    //   Rows 5 and 6 ask the placement check about a large ladder whose four cells above are open, and then about one with a wall among them.
    public partial class RuntimeValidation
    {
        // The colonist must stand on the surface cell before this many ticks pass.
        private const int WayUpsColonistTicks = 6000;

        // The block both floors give the rows: 16 cells across and 8 deep.
        private const int WayUpsWidth = 16;
        private const int WayUpsDepth = 8;

        private static readonly string[] WayUpsRowLabels =
        {
            "small ladder places a pair of ways",
            "large ladder covers four cells on both floors",
            "large stairs place a pair",
            "colonist climbs a large ladder",
            "large way allowed with four open cells on the other floor",
            "blocked cell on the other floor refuses a large way",
        };

        private readonly List<string> wayUpsRows = new List<string>();
        private readonly HashSet<string> wayUpsRecorded = new HashSet<string>();
        private int wayUpsStage;
        private bool wayUpsDone;
        private int wayUpsStartTick;
        private int wayUpsDueTick;
        private bool wayUpsCallSent;
        private Map wayUpsSurface;
        private Map wayUpsLower;
        private IntVec3 wayUpsBlock;
        private Pawn wayUpsWalker;
        private IntVec3 wayUpsCallCell;

        // The def with this name. A missing def throws, so the row reads FAIL with the name.
        private static ThingDef WayUpDef(string defName)
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(defName)
                ?? throw new InvalidOperationException("the mod has no " + defName + " def");
        }

        // The cell at an offset from the block's origin. Both floors use the same cells.
        private static IntVec3 WayUpCell(IntVec3 block, int x, int z)
        {
            return block + new IntVec3(x, 0, z);
        }

        // The way with this def standing on the cell, or null. A 2 by 2 way stands on all four of its cells.
        private static Thing WayUpAt(Map map, IntVec3 cell, string defName)
        {
            return map.thingGrid.ThingsListAt(cell).FirstOrDefault(thing => thing.def.defName == defName);
        }

        // Spawns a way with the game's own spawn, which runs its SpawnSetup and so makes the partner. A 2 by 2 kind takes the origin as its
        // bottom-left cell.
        private static void SpawnWayUp(string defName, IntVec3 origin, Map map)
        {
            Thing thing = ThingMaker.MakeThing(WayUpDef(defName), ThingDefOf.Steel);
            thing.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(thing, origin, map, Rot4.North);
            if (!thing.Spawned)
            {
                throw new InvalidOperationException(defName + " did not spawn at " + origin);
            }
        }

        // Removes the way with this def from the cell, if one stands there.
        private static void RemoveWayUpAt(Map map, IntVec3 cell, string defName)
        {
            Thing thing = WayUpAt(map, cell, defName);
            if (thing != null)
            {
                thing.Destroy(DestroyMode.Vanish);
            }
        }

        // Clears a block of ground on one floor and gives it concrete, so the rows stand on bare ground.
        private static void ClearWayUpsBlock(Map map, IntVec3 block)
        {
            for (int x = 0; x < WayUpsWidth; x++)
            {
                for (int z = 0; z < WayUpsDepth; z++)
                {
                    IntVec3 cell = WayUpCell(block, x, z);
                    ClearCell(map, cell);
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                }
            }
        }

        // A walled box 7 cells square, with a roof over its inside. The inside is 25 cells, the least a room may have for stairs, and no inside
        // cell is open to the sky.
        private static void BuildWayUpsRoom(Map map, IntVec3 corner)
        {
            for (int x = 0; x < 7; x++)
            {
                for (int z = 0; z < 7; z++)
                {
                    IntVec3 cell = WayUpCell(corner, x, z);
                    ClearCell(map, cell);
                    if (x == 0 || z == 0 || x == 6 || z == 6)
                    {
                        GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), cell, map);
                    }
                    else
                    {
                        map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                    }
                }
            }
        }

        // Spawns a pair of ways on the lower floor and checks each cell of the pair on both floors. A way of size 1 has one cell.
        private static string CheckWayUpPair(Map lower, Map surface, string lowerDef, string upperDef, IntVec3 origin, int size)
        {
            SpawnWayUp(lowerDef, origin, lower);
            List<string> missing = new List<string>();
            for (int x = 0; x < size; x++)
            {
                for (int z = 0; z < size; z++)
                {
                    IntVec3 cell = WayUpCell(origin, x, z);
                    if (WayUpAt(lower, cell, lowerDef) == null)
                    {
                        missing.Add("no " + lowerDef + " on the lower floor at " + cell);
                    }
                    if (WayUpAt(surface, cell, upperDef) == null)
                    {
                        missing.Add("no " + upperDef + " on the floor above at " + cell);
                    }
                }
            }
            return missing.Count == 0
                ? "PASS: " + size + " by " + size + " cells hold the pair, " + lowerDef + " below and " + upperDef + " above"
                : "FAIL: " + string.Join("; ", missing);
        }

        // The placement check's answer for a way at the origin on the lower floor. Null means the placement is allowed.
        private static string LargeWayRefusal(string defName, IntVec3 origin, Map lower)
        {
            return PlacementRefusal(WayUpDef(defName), origin, lower);
        }

        // Runs one row's check and records its result. A check that throws records its reason as the FAIL.
        private void RecordWayUpRow(string label, Func<string> check)
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
            wayUpsRows.Add(result);
            wayUpsRecorded.Add(label);
            Record(label, result);
        }

        // Builds the shared block and runs the rows that need no colonist. Then starts the climb, whose rows are checked over the next ticks.
        private void SetUpWayUps()
        {
            wayUpsSurface = Find.CurrentMap;
            ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
            wayUpsLower = tracker.GetLowerLevel(wayUpsSurface.Tile, wayUpsSurface) ?? CreateLevelBelow(tracker, wayUpsSurface);
            RequireFloors(tracker, wayUpsSurface, 2);
            wayUpsSurface.fogGrid.ClearAllFog();
            wayUpsLower.fogGrid.ClearAllFog();
            wayUpsBlock = FindStandableBlock(wayUpsSurface, WayUpsWidth, WayUpsDepth);
            if (!wayUpsBlock.IsValid)
            {
                throw new InvalidOperationException("no standable 16 by 8 block on the surface away from the edges");
            }
            ClearWayUpsBlock(wayUpsSurface, wayUpsBlock);
            ClearWayUpsBlock(wayUpsLower, wayUpsBlock);
            BuildWayUpsRoom(wayUpsLower, WayUpCell(wayUpsBlock, 9, 0));

            RecordWayUpRow(WayUpsRowLabels[0], () => CheckWayUpPair(wayUpsLower, wayUpsSurface, "ZL_SmallLadderUp", "ZL_SmallLadderDown",
                WayUpCell(wayUpsBlock, 1, 1), 1));
            RecordWayUpRow(WayUpsRowLabels[1], () => CheckWayUpPair(wayUpsLower, wayUpsSurface, "ZL_LargeLadderUp", "ZL_LargeLadderDown",
                WayUpCell(wayUpsBlock, 3, 1), 2));
            RecordWayUpRow(WayUpsRowLabels[2], () => CheckWayUpPair(wayUpsLower, wayUpsSurface, "ZL_LargeStairsUp", "ZL_LargeStairsDown",
                WayUpCell(wayUpsBlock, 6, 1), 2));
            RecordWayUpRow(WayUpsRowLabels[4], () =>
            {
                string refusal = LargeWayRefusal("ZL_LargeLadderUp", WayUpCell(wayUpsBlock, 10, 2), wayUpsLower);
                return refusal == null
                    ? "PASS: the placement check allows a large way with its four cells above open"
                    : "FAIL: refused with \"" + refusal + "\"";
            });
            RecordWayUpRow(WayUpsRowLabels[5], () =>
            {
                // A wall on one of the four cells above. The row is run after the open case, so the open case does not see it.
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), WayUpCell(wayUpsBlock, 11, 3), wayUpsSurface);
                string refusal = LargeWayRefusal("ZL_LargeLadderUp", WayUpCell(wayUpsBlock, 10, 2), wayUpsLower);
                return refusal != null && refusal.Contains("other floor")
                    ? "PASS: refused with \"" + refusal + "\""
                    : "FAIL: " + (refusal == null ? "the placement check allowed it" : "refused with \"" + refusal + "\"");
            });

            string climbError = StartWayUpClimb();
            if (climbError != null)
            {
                RecordWayUpRow(WayUpsRowLabels[3], () => "FAIL: " + climbError);
                FinishWayUps();
            }
        }

        // Removes the other ways, so the large ladder is the only way between the floors, then calls a colonist to the surface. Returns the reason
        // when the set-up fails, or null when the call is under way.
        private string StartWayUpClimb()
        {
            try
            {
                // A missing kind throws here, before anything else changes.
                WayUpDef("ZL_LargeLadderUp");
                RemoveWayUpAt(wayUpsLower, WayUpCell(wayUpsBlock, 1, 1), "ZL_SmallLadderUp");
                RemoveWayUpAt(wayUpsSurface, WayUpCell(wayUpsBlock, 1, 1), "ZL_SmallLadderDown");
                RemoveWayUpAt(wayUpsLower, WayUpCell(wayUpsBlock, 6, 1), "ZL_LargeStairsUp");
                RemoveWayUpAt(wayUpsSurface, WayUpCell(wayUpsBlock, 6, 1), "ZL_LargeStairsDown");
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                tracker.GameComponentTick();

                wayUpsWalker = SpawnCapableColonistNear(wayUpsLower, WayUpCell(wayUpsBlock, 4, 6), new List<IntVec3>());
                wayUpsWalker.workSettings.DisableAll();
                wayUpsCallCell = WayUpCell(wayUpsBlock, 3, 3);
                if (!wayUpsCallCell.Standable(wayUpsSurface))
                {
                    return "the call cell " + wayUpsCallCell + " is not standable on the surface";
                }
                List<Pawn> sent = CallColonistsTo(new List<Pawn> { wayUpsWalker }, wayUpsCallCell, wayUpsSurface);
                wayUpsCallSent = sent.Contains(wayUpsWalker);
                wayUpsStartTick = Find.TickManager.TicksGame;
                wayUpsDueTick = wayUpsStartTick + WayUpsColonistTicks;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        // Records the climb row and the aggregate, then ends the scenario.
        private void FinishWayUpClimb(bool arrived)
        {
            int ticks = Find.TickManager.TicksGame - wayUpsStartTick;
            string result = arrived
                ? "PASS: the colonist stood on the surface cell after " + ticks + " ticks, through the large ladder"
                : "FAIL: after " + ticks + " ticks the colonist is on " + DescribeCallFloor(wayUpsWalker.Map, wayUpsSurface, wayUpsLower)
                    + " at " + wayUpsWalker.Position + ", the job is " + DescribeJob(wayUpsWalker.CurJob)
                    + (wayUpsCallSent ? "" : "; the call did not send the colonist");
            RecordWayUpRow(WayUpsRowLabels[3], () => result);
            FinishWayUps();
        }

        // Records the aggregate row, then ends the scenario. The aggregate reads PASS only when all six rows read PASS.
        private void FinishWayUps()
        {
            wayUpsDone = true;
            bool all = wayUpsRows.Count == WayUpsRowLabels.Length && wayUpsRows.All(row => row.StartsWith("PASS"));
            Record("way ups", all ? "PASS" : "FAIL: a row above failed");
            finished = true;
            Root.Shutdown();
        }

        // Fails every row that has not been recorded, with the reason, and ends the scenario.
        private void FailWayUps(string reason)
        {
            foreach (string label in WayUpsRowLabels)
            {
                if (!wayUpsRecorded.Contains(label))
                {
                    RecordWayUpRow(label, () => "FAIL: " + reason);
                }
            }
            FinishWayUps();
        }

        // Called each frame while the player's colony is ready. The first call sets up and runs the rows that need no colonist. Later calls wait
        // for the colonist to reach the surface, or for the time limit.
        private void RunWayUpsTick()
        {
            if (wayUpsDone)
            {
                return;
            }
            if (wayUpsStage == 0)
            {
                wayUpsStage = 1;
                try
                {
                    SetUpWayUps();
                }
                catch (Exception exception)
                {
                    Log.Error("Z-Levels way ups validation failed: " + exception);
                    FailWayUps(exception.Message);
                }
                return;
            }
            try
            {
                bool arrived = wayUpsWalker.Map == wayUpsSurface && wayUpsWalker.Position == wayUpsCallCell;
                bool gone = wayUpsWalker.Destroyed || wayUpsWalker.Dead;
                if (arrived || gone || Find.TickManager.TicksGame > wayUpsDueTick)
                {
                    FinishWayUpClimb(arrived);
                }
            }
            catch (Exception exception)
            {
                Log.Error("Z-Levels way ups validation failed: " + exception);
                FailWayUps(exception.Message);
            }
        }
    }
}
