using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    // The D15 follow-ups (2026-11-17), spec docs/specs/2026-11-17-d15-followups.md.
    //
    // Filing. ResetJobTrackerFor makes a colonist's tracker the one being filled (LocalTargetInfo_Constructor_Patch.curPawnJobTracker): every
    // cell target built from then on, by any code, is filed into it under the floor field. BuildJobListFor ends the filing after a search that
    // finds a job. The rows check that the filing also ends after a work search that finds nothing, and after a think-tree search
    // (Pawn_JobTracker.DetermineNextJob).
    //
    // The reservation patches. ReservationManager_CanReserve_Patch and ReservationManager_CanReserveStack_Patch move the colonist to the
    // target's floor. A harness prefix throws while the colonist stands on the lower floor, and the rows check where the colonist ends.
    //
    // The floor loops. The bill ingredient loop and the refuel loop move the colonist to each floor. Harness prefixes on the per-floor steps
    // record the colonist's floor and the floor field, so the rows check that the field names the floor the colonist looks into.
    //
    // The haul count. HaulToCellStorageJob counts the space at a store cell on another floor from that floor's stacks.
    public partial class RuntimeValidation
    {
        private const string ReserveThrowMessage = "Z-Levels follow-up test: the reservation check failed on the other floor";

        // The harness prefixes on the per-floor steps of the bill and refuel loops. While armed for the colonist, each step finds nothing and
        // records the colonist's floor and the floor field.
        internal static class FloorFieldProbe
        {
            internal static Pawn ArmedPawn;
            internal static readonly List<KeyValuePair<Map, Map>> Calls = new List<KeyValuePair<Map, Map>>();

            public static bool EmptyStep(Pawn pawn, ref bool __result)
            {
                if (ArmedPawn == null || pawn != ArmedPawn)
                {
                    return true;
                }
                Calls.Add(new KeyValuePair<Map, Map>(pawn.Map, ZU.Field));
                __result = false;
                return false;
            }

            internal static void Arm(Pawn pawn)
            {
                Calls.Clear();
                ArmedPawn = pawn;
            }
        }

        // The harness prefix on the reservation checks. While armed, it throws when the colonist is the claimant and stands on the armed floor.
        internal static class ReserveThrowProbe
        {
            internal static Pawn ArmedPawn;
            internal static Map ArmedMap;
            internal static int Throws;

            public static void Prefix(Pawn claimant)
            {
                if (ArmedPawn != null && claimant == ArmedPawn && claimant.Map == ArmedMap)
                {
                    Throws++;
                    throw new InvalidOperationException(ReserveThrowMessage);
                }
            }
        }

        private void RunD15FollowupsCheck()
        {
            const string label = "d15 follow-ups";
            Harmony harmony = new Harmony("zlevels.validation.d15followups");
            List<MethodInfo> patchedMethods = new List<MethodInfo>();
            try
            {
                MethodInfo canReserve = AccessTools.Method(typeof(ReservationManager), nameof(ReservationManager.CanReserve)) ?? throw new InvalidOperationException("ReservationManager.CanReserve is missing");
                MethodInfo canReserveStack = AccessTools.Method(typeof(ReservationManager), nameof(ReservationManager.CanReserveStack)) ?? throw new InvalidOperationException("ReservationManager.CanReserveStack is missing");
                MethodInfo billStep = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients") ?? throw new InvalidOperationException("WorkGiver_DoBill.TryFindBestBillIngredients is missing");
                MethodInfo refuelStep = AccessTools.Method(typeof(WorkGiver_Refuel), nameof(WorkGiver_Refuel.HasJobOnThing)) ?? throw new InvalidOperationException("WorkGiver_Refuel.HasJobOnThing is missing");
                MethodInfo determineNextJob = AccessTools.Method(typeof(Pawn_JobTracker), "DetermineNextJob") ?? throw new InvalidOperationException("Pawn_JobTracker.DetermineNextJob is missing");

                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                lower.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 2);
                Func<Map, string> describeFloor = map => map == surface ? "the own floor" : map == lower ? "the lower floor" : map?.ToString() ?? "null";

                Pawn colonist = SpawnColonistNear(surface, stairs.Position, new List<IntVec3>());
                colonist.workSettings.DisableAll();
                IntVec3 colonistCell = colonist.Position;
                Func<bool> home = () => colonist.Map == surface && colonist.Position == colonistCell;
                Func<string> where = () => "the colonist is at " + colonist.Position + " on " + describeFloor(colonist.Map);
                Action closeFiling = () => LocalTargetInfo_Constructor_Patch.curPawnJobTracker = null;
                // Each row starts with the colonist on its cell, the floor field on the own floor, and no tracker being filled.
                Action startRow = () =>
                {
                    if (!home())
                    {
                        ZU.Teleport(colonist, surface, colonistCell);
                    }
                    closeFiling();
                    ZU.Field = surface;
                };
                List<bool> rows = new List<bool>();
                Action<string, bool, string> row = (name, pass, failure) =>
                {
                    rows.Add(pass);
                    Record(name, pass ? "PASS" : "FAIL: " + failure);
                };

                // Part 1. Filing ends when a search ends.
                IntVec3 probeCell = GenRadial.RadialCellsAround(colonistCell, 6f, false).First(c => c.InBounds(surface) && c.DistanceTo(colonistCell) >= 3f);
                startRow();
                ThinkResult workResult = new JobGiver_Work().TryIssueJobPackage(colonist, default(JobIssueParams));
                JobTracker filingAfterWork = LocalTargetInfo_Constructor_Patch.curPawnJobTracker;
                JobTracker colonistTracker = tracker.jobTracker[colonist];
                bool filedBefore = colonistTracker.lookedAtLocalCellMap != null && colonistTracker.lookedAtLocalCellMap.ContainsKey(probeCell);
                LocalTargetInfo probeTarget = new LocalTargetInfo(probeCell);
                bool filedAfter = colonistTracker.lookedAtLocalCellMap != null && colonistTracker.lookedAtLocalCellMap.ContainsKey(probeCell);
                Record("the empty work search", "INFO: job " + (workResult.Job == null ? "none" : workResult.Job.def.defName) + ", probe cell " + probeTarget.Cell);
                row("no tracker is being filled after a work search that finds nothing", filingAfterWork == null,
                    "the tracker being filled is " + (filingAfterWork == colonistTracker ? "the colonist's" : "another one"));
                row("a cell built after a work search that finds nothing is not filed in the colonist's tracker", !filedBefore && !filedAfter,
                    filedBefore ? "the cell was filed before it was built" : "the cell was filed under " + describeFloor(colonistTracker.lookedAtLocalCellMap[probeCell]));

                startRow();
                tracker.ResetJobTrackerFor(colonist);
                object[] determineArgs = new object[determineNextJob.GetParameters().Length];
                for (int i = 0; i < determineArgs.Length; i++)
                {
                    ParameterInfo parameter = determineNextJob.GetParameters()[i];
                    determineArgs[i] = parameter.HasDefaultValue ? parameter.DefaultValue : null;
                }
                object thought = determineNextJob.Invoke(colonist.jobs, determineArgs);
                JobTracker filingAfterThink = LocalTargetInfo_Constructor_Patch.curPawnJobTracker;
                Job thoughtJob = thought is ThinkResult thinkResult ? thinkResult.Job : null;
                Record("the think-tree search", "INFO: job " + (thoughtJob == null ? "none" : thoughtJob.def.defName));
                row("no tracker is being filled after a think-tree search", filingAfterThink == null,
                    "the tracker being filled is " + (filingAfterThink == colonistTracker ? "the colonist's" : "another one"));

                // Part 2. A reservation check that throws on the lower floor. The CanReserve probe runs first: the Z-Levels prefix asks CanReserve
                // again after it moves the colonist, so the inner call throws. The CanReserveStack prefix lets the original run after the move, so
                // the probe runs after it.
                harmony.Patch(canReserve, prefix: new HarmonyMethod(typeof(ReserveThrowProbe), nameof(ReserveThrowProbe.Prefix)) { priority = Priority.First });
                patchedMethods.Add(canReserve);
                harmony.Patch(canReserveStack, prefix: new HarmonyMethod(typeof(ReserveThrowProbe), nameof(ReserveThrowProbe.Prefix)) { priority = Priority.Last });
                patchedMethods.Add(canReserveStack);
                IntVec3 lowerCell = GenRadial.RadialCellsAround(stairs.Position, 2.5f, false).First(c => c.InBounds(lower) && c.Standable(lower) && c.GetFirstItem(lower) == null);
                Thing lowerStack = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Steel), lowerCell, lower);
                IntVec3 lowerFreeCell = GenRadial.RadialCellsAround(stairs.Position, 3.5f, false).First(c => c.InBounds(lower) && c.Standable(lower) && c.GetFirstItem(lower) == null && c != lowerCell);

                startRow();
                Exception thrown = ReserveThrowing(colonist, lower, () => colonist.CanReserve(lowerStack));
                row("a reservation check of a thing below that throws reaches the caller", IsReserveThrow(thrown), DescribeReserveThrow(thrown));
                row("a reservation check of a thing below that throws leaves the colonist on its own floor and cell", home(), where());

                startRow();
                tracker.ResetJobTrackerFor(colonist);
                closeFiling();
                tracker.jobTracker[colonist].lookedAtLocalCellMap[lowerFreeCell] = lower;
                thrown = ReserveThrowing(colonist, lower, () => colonist.CanReserve(lowerFreeCell));
                row("a reservation check of a cell filed below that throws reaches the caller", IsReserveThrow(thrown), DescribeReserveThrow(thrown));
                row("a reservation check of a cell filed below that throws leaves the colonist on its own floor and cell", home(), where());

                startRow();
                thrown = ReserveThrowing(colonist, lower, () => surface.reservationManager.CanReserveStack(colonist, lowerStack));
                row("a stack reservation check of a thing below that throws reaches the caller", IsReserveThrow(thrown), DescribeReserveThrow(thrown));
                row("a stack reservation check of a thing below that throws leaves the colonist on its own floor and cell", home(), where());

                startRow();
                bool reservable = colonist.CanReserve(lowerStack);
                Record("a normal reservation check of the thing below", "INFO: " + (reservable ? "allowed" : "refused"));
                row("a normal reservation check of a thing below leaves the colonist on its own floor and cell", home(), where());

                // Part 3. The bill and refuel loops look into each floor with the field on that floor.
                harmony.Patch(billStep, prefix: new HarmonyMethod(typeof(FloorFieldProbe), nameof(FloorFieldProbe.EmptyStep)) { priority = Priority.First });
                patchedMethods.Add(billStep);
                harmony.Patch(refuelStep, prefix: new HarmonyMethod(typeof(FloorFieldProbe), nameof(FloorFieldProbe.EmptyStep)) { priority = Priority.First });
                patchedMethods.Add(refuelStep);
                Action<string, Func<Job>> loopRows = (name, search) =>
                {
                    startRow();
                    tracker.ResetJobTrackerFor(colonist);
                    FloorFieldProbe.Arm(colonist);
                    Job found;
                    try
                    {
                        found = search();
                    }
                    finally
                    {
                        FloorFieldProbe.ArmedPawn = null;
                    }
                    Map fieldAfter = ZU.Field;
                    List<KeyValuePair<Map, Map>> calls = FloorFieldProbe.Calls.ToList();
                    string described = calls.Count == 0 ? "no calls" : string.Join(", ", calls.Select(c => "colonist on " + describeFloor(c.Key) + " with the field on " + describeFloor(c.Value)));
                    Record("the " + name + " loop", "INFO: job " + (found == null ? "none" : found.def.defName) + "; " + described);
                    row("the " + name + " loop looks into the lower floor", calls.Any(c => c.Key == lower), described);
                    row("the floor field names the colonist's floor at each step of the " + name + " loop", calls.Count > 0 && calls.All(c => c.Key == c.Value), described);
                    row("the floor field is the own floor after the " + name + " loop", fieldAfter == surface, "the floor field is " + describeFloor(fieldAfter));
                    row("the colonist is back on its own floor and cell after the " + name + " loop", home(), where());
                };

                ThingDef stoveDef = Def("FueledStove");
                ThingWithComps stove = SpawnPlayerBuilding(surface, stoveDef, FindSupplyCell(surface, stairs.Position, stoveDef.Size));
                stove.TryGetComp<CompRefuelable>().Refuel(30f);
                ((Building_WorkTable)stove).BillStack.AddBill(new Bill_Production(DefDatabase<RecipeDef>.GetNamed("CookMealSimple")));
                WorkGiver_DoBill billScanner = DefDatabase<WorkGiverDef>.AllDefsListForReading
                    .Where(d => d.Worker is WorkGiver_DoBill && d.fixedBillGiverDefs != null && d.fixedBillGiverDefs.Contains(stoveDef))
                    .Select(d => (WorkGiver_DoBill)d.Worker).FirstOrDefault() ?? throw new InvalidOperationException("no bill work giver for the fuelled stove");
                loopRows("bill", () => JobPatches.TryIssueJobPackagePatch.DoBillJobOnThing(billScanner, colonist, stove, tracker.jobTracker[colonist], true));
                bool stoveBack = stove.Map == surface;
                row("the stove is back on its own floor after the bill loop", stoveBack, "the stove is on " + describeFloor(stove.Map));
                stove.Destroy();

                // An empty campfire beside the stairs. SpawnEmptyCampfire reads the supply scenario's stairs, so it is not used here.
                ThingDef campfireDef = Def("Campfire");
                CompRefuelable campfire = SpawnPlayerBuilding(surface, campfireDef, FindSupplyCell(surface, stairs.Position, campfireDef.Size)).GetComp<CompRefuelable>();
                EmptyFuel(campfire);
                WorkGiver_Refuel refuelScanner = DefDatabase<WorkGiverDef>.GetNamed("Refuel").Worker as WorkGiver_Refuel ?? throw new InvalidOperationException("the Refuel work giver is not a WorkGiver_Refuel");
                loopRows("refuel", () => JobPatches.TryIssueJobPackagePatch.RefuelJobOnThing(refuelScanner, colonist, campfire.parent, true));
                campfire.parent.Destroy();

                // Part 4. The haul count at a store cell on the lower floor holding a partial stack.
                startRow();
                Zone_Stockpile storeZone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, lower.zoneManager);
                lower.zoneManager.RegisterZone(storeZone);
                storeZone.AddCell(lowerFreeCell);
                Thing heldBelow = ThingMaker.MakeThing(ThingDefOf.Steel);
                heldBelow.stackCount = 50;
                GenSpawn.Spawn(heldBelow, lowerFreeCell, lower);
                IntVec3 carryCell = GenRadial.RadialCellsAround(colonistCell, 4f, false).First(c => c.InBounds(surface) && c.Standable(surface) && c.GetFirstItem(surface) == null && c != colonistCell);
                Thing carried = ThingMaker.MakeThing(ThingDefOf.Steel);
                carried.stackCount = 60;
                GenSpawn.Spawn(carried, carryCell, surface);
                try
                {
                    tracker.ResetJobTrackerFor(colonist);
                    Job haul = JobPatches.TryIssueJobPackagePatch.HaulToCellStorageJob(colonist, carried, lowerFreeCell, true, lower);
                    int expected = ThingDefOf.Steel.stackLimit - heldBelow.stackCount;
                    row("a haul to a store cell below counts the space on that floor", haul != null && haul.count == expected,
                        "job count " + (haul == null ? "none" : haul.count.ToString()) + ", expected " + expected);
                    row("the colonist is on its own floor and cell after the haul count", home(), where());
                }
                finally
                {
                    closeFiling();
                    carried.Destroy();
                    heldBelow.Destroy();
                    storeZone.Delete();
                }
                lowerStack.Destroy();

                startRow();
                Record(label, rows.All(pass => pass) ? "PASS" : "FAIL: " + rows.Count(pass => !pass) + " rows above failed");
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels D15 follow-up validation failed: " + exception);
            }
            finally
            {
                FloorFieldProbe.ArmedPawn = null;
                ReserveThrowProbe.ArmedPawn = null;
                ReserveThrowProbe.ArmedMap = null;
                foreach (MethodInfo method in patchedMethods)
                {
                    harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
                }
            }
            Root.Shutdown();
        }

        // Runs the reservation check with the throw probe armed for the colonist and floor, and returns what it threw, or null.
        private static Exception ReserveThrowing(Pawn colonist, Map throwOn, Func<object> check)
        {
            ReserveThrowProbe.Throws = 0;
            ReserveThrowProbe.ArmedPawn = colonist;
            ReserveThrowProbe.ArmedMap = throwOn;
            try
            {
                check();
                return null;
            }
            catch (Exception thrown)
            {
                return thrown;
            }
            finally
            {
                ReserveThrowProbe.ArmedPawn = null;
                ReserveThrowProbe.ArmedMap = null;
            }
        }

        private static bool IsReserveThrow(Exception thrown)
        {
            return thrown is InvalidOperationException && thrown.Message == ReserveThrowMessage && ReserveThrowProbe.Throws == 1;
        }

        private static string DescribeReserveThrow(Exception thrown)
        {
            return (thrown == null ? "the check did not throw" : "the check threw " + thrown.GetType().Name + ": " + thrown.Message) + ", " + ReserveThrowProbe.Throws + " throws";
        }
    }
}
