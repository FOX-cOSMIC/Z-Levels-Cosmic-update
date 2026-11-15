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
    // D15 (2026-11-15), spec docs/specs/2026-11-15-floor-state-left-behind.md. No search may leave floor state behind.
    //
    // The floor field. ZUtils.currentLookedIntoMap names the floor a search looks into. The two floor iterators in ZUtils.cs set it before each
    // floor they yield. While a colonist's tracker is the one being filled (ResetJobTrackerFor sets it, BuildJobListFor clears it), the
    // constructor patch on LocalTargetInfo files each cell built under the field, and CanReach_Patch answers the colonist's reach to that cell
    // on the filed floor. A field left on the lower floor makes the colonist's reach to an open surface cell, solid below, refused. The rows run
    // each iterator to its end, stop it early, and throw inside its loop, and run the food, joy, rest and attack searches through their Harmony
    // prefixes. A harness prefix on each search's per-floor step makes every floor come up empty and records the floors it ran on.
    //
    // The reach patch. CanReach_Patch moves the colonist to the target's floor and answers there, and its postfix moves the colonist back.
    // Harmony skips a postfix when the call throws. A harness prefix on Reachability.CanReach throws while the colonist stands on the lower
    // floor, and the rows check where the colonist is afterwards.
    public partial class RuntimeValidation
    {
        private const string ReachThrowMessage = "Z-Levels floor state test: the reach check failed on the other floor";

        // ZUtils is internal to the mod, so the scenario reaches it by reflection. Reachability.map is not public in the reference assembly.
        internal static class ZU
        {
            private static readonly Type Utils = AccessTools.TypeByName("ZLevels.ZUtils") ?? throw new InvalidOperationException("ZLevels.ZUtils is missing");
            private static readonly FieldInfo LookedInto = AccessTools.Field(Utils, "currentLookedIntoMap") ?? throw new InvalidOperationException("ZUtils.currentLookedIntoMap is missing");
            private static readonly MethodInfo TeleportMethod = AccessTools.Method(Utils, "TeleportThing", new[] { typeof(Thing), typeof(Map), typeof(IntVec3) }) ?? throw new InvalidOperationException("ZUtils.TeleportThing is missing");
            private static readonly MethodInfo MapsMethod = AccessTools.Method(Utils, "GetAllMapsInClosestOrder", new[] { typeof(Thing), typeof(Map), typeof(IntVec3), typeof(bool), typeof(bool) }) ?? throw new InvalidOperationException("ZUtils.GetAllMapsInClosestOrder is missing");
            private static readonly MethodInfo MapsForTwoMethod = AccessTools.Method(Utils, "GetAllMapsInClosestOrderForTwoThings") ?? throw new InvalidOperationException("ZUtils.GetAllMapsInClosestOrderForTwoThings is missing");
            private static readonly AccessTools.FieldRef<Reachability, Map> ReachMap = AccessTools.FieldRefAccess<Reachability, Map>("map");

            internal static Map Field
            {
                get => (Map)LookedInto.GetValue(null);
                set => LookedInto.SetValue(null, value);
            }

            internal static void Teleport(Thing thing, Map map, IntVec3 cell) => TeleportMethod.Invoke(null, new object[] { thing, map, cell });

            // The iterators are called lazily, as a caller's foreach calls them, so an early stop or a throw in the loop body disposes them.
            internal static IEnumerable<Map> Maps(Thing thing, Map oldMap, IntVec3 oldPosition) => (IEnumerable<Map>)MapsMethod.Invoke(null, new object[] { thing, oldMap, oldPosition, false, false });

            internal static IEnumerable<Map> MapsForTwo(Thing thing, Map oldMap, IntVec3 oldPosition, Thing thing2, Map oldMap2, IntVec3 oldPosition2)
                => (IEnumerable<Map>)MapsForTwoMethod.Invoke(null, new object[] { thing, oldMap, oldPosition, thing2, oldMap2, oldPosition2 });

            internal static Map ReachabilityMap(Reachability reachability) => ReachMap(reachability);
        }

        // The harness prefixes on the per-floor steps of the need searches and on the attack search. While armed for the colonist, the need
        // steps find nothing. Each records the floor the colonist stood on when it ran.
        internal static class FloorStateProbe
        {
            internal static Pawn ArmedPawn;
            internal static readonly List<Map> Floors = new List<Map>();

            public static bool EmptyStep(Pawn pawn, ref Job __result)
            {
                if (ArmedPawn == null || pawn != ArmedPawn)
                {
                    return true;
                }
                Floors.Add(pawn.Map);
                __result = null;
                return false;
            }

            public static void AttackStep(IAttackTargetSearcher searcher)
            {
                if (ArmedPawn != null && searcher?.Thing == ArmedPawn)
                {
                    Floors.Add(ArmedPawn.Map);
                }
            }

            internal static void Arm(Pawn pawn)
            {
                Floors.Clear();
                ArmedPawn = pawn;
            }
        }

        // The harness prefix on the reach check. While armed, it throws when the colonist's reach is answered on the armed floor.
        internal static class ReachThrowProbe
        {
            internal static Pawn ArmedPawn;
            internal static Map ArmedMap;
            internal static int Throws;

            public static void Prefix(Reachability __instance, TraverseParms traverseParams)
            {
                if (ArmedPawn != null && traverseParams.pawn == ArmedPawn && ZU.ReachabilityMap(__instance) == ArmedMap)
                {
                    Throws++;
                    throw new InvalidOperationException(ReachThrowMessage);
                }
            }
        }

        private void RunFloorStateAfterSearchCheck()
        {
            const string label = "floor state after search";
            Harmony harmony = new Harmony("zlevels.validation.floorstate");
            List<MethodInfo> patchedMethods = new List<MethodInfo>();
            try
            {
                MethodInfo foodStep = AccessTools.Method(typeof(JobPatches.JobGiver_GetFoodPatch), "TryGiveJob") ?? throw new InvalidOperationException("JobGiver_GetFoodPatch.TryGiveJob is missing");
                MethodInfo joyStep = AccessTools.Method(typeof(JobPatches.JobGiver_GetJoyPatch), "TryGiveJob") ?? throw new InvalidOperationException("JobGiver_GetJoyPatch.TryGiveJob is missing");
                MethodInfo restStep = AccessTools.Method(typeof(JobPatches.JobGiver_GetRestPatch), "TryGiveJob") ?? throw new InvalidOperationException("JobGiver_GetRestPatch.TryGiveJob is missing");
                MethodInfo attackSearch = AccessTools.Method(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget)) ?? throw new InvalidOperationException("AttackTargetFinder.BestAttackTarget is missing");
                MethodInfo reachCheck = AccessTools.Method(typeof(Reachability), nameof(Reachability.CanReach), new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })
                    ?? throw new InvalidOperationException("Reachability.CanReach(IntVec3, LocalTargetInfo, PathEndMode, TraverseParms) is missing");

                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                lower.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 2);
                Func<Map, string> describeFloor = map => map == surface ? "the own floor" : map == lower ? "the lower floor" : map?.ToString() ?? "null";
                Func<IEnumerable<Map>, string> describeFloors = maps => maps.Any() ? string.Join(", ", maps.Select(describeFloor)) : "none";

                Pawn colonist = SpawnColonistNear(surface, stairs.Position, new List<IntVec3>());
                colonist.workSettings.DisableAll();
                IntVec3 colonistCell = colonist.Position;

                // A surface cell the colonist reaches on the surface, which is not walkable on the lower floor. A reach to it that is answered
                // on the lower floor is refused. The plain check passes no pawn, so the Z-Levels reachability patch leaves it alone.
                IntVec3 cell = GenRadial.RadialCellsAround(colonistCell, 15f, false)
                    .Where(c => c.InBounds(surface) && c.DistanceTo(colonistCell) >= 3f && c.Standable(surface) && !c.Walkable(lower)
                        && surface.reachability.CanReach(colonistCell, c, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!cell.IsValid)
                {
                    throw new InvalidOperationException("no surface cell within 15 of the colonist that it reaches and that is solid on the lower floor");
                }
                Record("floor state cell", "INFO: colonist at " + colonistCell + ", cell " + cell + ", stairs at " + stairs.Position);

                // Each row starts from the state a finished search on the own floor leaves: the colonist on its cell, its tracker the one being
                // filled, and the floor field on the own floor.
                Action startRow = () =>
                {
                    if (colonist.Map != surface || colonist.Position != colonistCell)
                    {
                        ZU.Teleport(colonist, surface, colonistCell);
                    }
                    tracker.ResetJobTrackerFor(colonist);
                    ZU.Field = surface;
                };
                Func<bool> reaches = () => colonist.CanReach(cell, PathEndMode.OnCell, Danger.Deadly);
                List<bool> rows = new List<bool>();
                Action<string, bool, string> row = (name, pass, failure) =>
                {
                    rows.Add(pass);
                    Record(name, pass ? "PASS" : "FAIL: " + failure);
                };

                startRow();
                row("the colonist reaches the cell before any search", reaches(), "the colonist's reach to " + cell + " is refused before any search");

                // Part 1. The colonist's iterator, run by the scenario as a caller would. After each run the caller moves the colonist back.
                startRow();
                List<Map> seen = ZU.Maps(colonist, surface, colonistCell).ToList();
                Map fieldAfter = ZU.Field;
                row("the colonist's floor iterator yields the lower floor", seen.Contains(lower), "it yielded " + describeFloors(seen));
                row("the floor field is the own floor after the iterator runs to its end", fieldAfter == surface, "the floor field is " + describeFloor(fieldAfter));
                ZU.Teleport(colonist, surface, colonistCell);
                row("the colonist still reaches the cell after the iterator runs to its end", reaches(), "the colonist's reach to " + cell + " is refused");

                startRow();
                foreach (Map map in ZU.Maps(colonist, surface, colonistCell))
                {
                    if (map == lower)
                    {
                        break;
                    }
                }
                fieldAfter = ZU.Field;
                row("the floor field is the own floor after the caller stops the iterator on the lower floor", fieldAfter == surface, "the floor field is " + describeFloor(fieldAfter));

                startRow();
                Exception loopThrow = null;
                try
                {
                    foreach (Map map in ZU.Maps(colonist, surface, colonistCell))
                    {
                        if (map == lower)
                        {
                            throw new InvalidOperationException("Z-Levels floor state test: the caller's loop failed on the lower floor");
                        }
                    }
                }
                catch (InvalidOperationException thrown)
                {
                    loopThrow = thrown;
                }
                fieldAfter = ZU.Field;
                row("the floor field is the own floor after the caller's loop throws on the lower floor", loopThrow != null && fieldAfter == surface,
                    (loopThrow == null ? "the loop never reached the lower floor; " : "") + "the floor field is " + describeFloor(fieldAfter));

                // A search nested in a loop body on the lower floor runs with the colonist there, so its own floor is the lower floor. When it ends,
                // the field must stay on the lower floor, where the outer search still looks.
                startRow();
                Map fieldAfterNested = null;
                bool nestedRan = false;
                foreach (Map map in ZU.Maps(colonist, surface, colonistCell))
                {
                    if (map == lower)
                    {
                        foreach (Map inner in ZU.Maps(colonist, lower, colonist.Position))
                        {
                            nestedRan = true;
                        }
                        fieldAfterNested = ZU.Field;
                        ZU.Teleport(colonist, lower, stairs.Position);
                        break;
                    }
                }
                row("a search nested on the lower floor leaves the floor field on the lower floor", nestedRan && fieldAfterNested == lower,
                    (nestedRan ? "" : "the nested search yielded no floor; ") + "the floor field is " + describeFloor(fieldAfterNested));

                // Part 2. The iterator for two things, with a steel stack beside the colonist as the second thing.
                IntVec3 stackCell = GenRadial.RadialCellsAround(colonistCell, 3f, false).First(c => c.InBounds(surface) && c.Standable(surface) && c.GetFirstItem(surface) == null);
                Thing stack = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Steel), stackCell, surface);
                Action twoBack = () =>
                {
                    ZU.Teleport(colonist, surface, colonistCell);
                    ZU.Teleport(stack, surface, stackCell);
                };
                startRow();
                List<Map> seenTwo = ZU.MapsForTwo(colonist, surface, colonistCell, stack, surface, stackCell).ToList();
                fieldAfter = ZU.Field;
                twoBack();
                row("the two-thing iterator yields the lower floor", seenTwo.Contains(lower), "it yielded " + describeFloors(seenTwo));
                row("the floor field is the own floor after the two-thing iterator runs to its end", fieldAfter == surface, "the floor field is " + describeFloor(fieldAfter));

                startRow();
                foreach (Map map in ZU.MapsForTwo(colonist, surface, colonistCell, stack, surface, stackCell))
                {
                    if (map == lower)
                    {
                        break;
                    }
                }
                fieldAfter = ZU.Field;
                twoBack();
                row("the floor field is the own floor after the caller stops the two-thing iterator on the lower floor", fieldAfter == surface, "the floor field is " + describeFloor(fieldAfter));

                startRow();
                loopThrow = null;
                try
                {
                    foreach (Map map in ZU.MapsForTwo(colonist, surface, colonistCell, stack, surface, stackCell))
                    {
                        if (map == lower)
                        {
                            throw new InvalidOperationException("Z-Levels floor state test: the caller's loop failed on the lower floor");
                        }
                    }
                }
                catch (InvalidOperationException thrown)
                {
                    loopThrow = thrown;
                }
                fieldAfter = ZU.Field;
                twoBack();
                row("the floor field is the own floor after the caller's two-thing loop throws on the lower floor", loopThrow != null && fieldAfter == surface,
                    (loopThrow == null ? "the loop never reached the lower floor; " : "") + "the floor field is " + describeFloor(fieldAfter));
                stack.Destroy();

                // Part 3. The need searches through their Z-Levels prefixes, with every floor empty.
                HarmonyMethod emptyStep = new HarmonyMethod(typeof(FloorStateProbe), nameof(FloorStateProbe.EmptyStep)) { priority = Priority.First };
                foreach (MethodInfo step in new[] { foodStep, joyStep, restStep })
                {
                    harmony.Patch(step, prefix: emptyStep);
                    patchedMethods.Add(step);
                }
                harmony.Patch(attackSearch, prefix: new HarmonyMethod(typeof(FloorStateProbe), nameof(FloorStateProbe.AttackStep)) { priority = Priority.First });
                patchedMethods.Add(attackSearch);

                foreach (var family in new (string name, ThinkNode_JobGiver giver)[]
                {
                    ("food", new JobGiver_GetFood()),
                    ("joy", new JobGiver_GetJoy()),
                    ("rest", new JobGiver_GetRest()),
                })
                {
                    startRow();
                    FloorStateProbe.Arm(colonist);
                    ThinkResult found;
                    try
                    {
                        found = family.giver.TryIssueJobPackage(colonist, default(JobIssueParams));
                    }
                    finally
                    {
                        FloorStateProbe.ArmedPawn = null;
                    }
                    List<Map> floors = FloorStateProbe.Floors.ToList();
                    fieldAfter = ZU.Field;
                    bool back = colonist.Map == surface && colonist.Position == colonistCell;
                    Record("the " + family.name + " search", "INFO: job " + (found.Job == null ? "none" : found.Job.def.defName) + ", floors searched " + describeFloors(floors));
                    row("the " + family.name + " search looks into the lower floor", floors.Contains(lower), "it searched " + describeFloors(floors));
                    row("the colonist is back on its own floor and cell after the " + family.name + " search", back, "the colonist is at " + colonist.Position + " on " + describeFloor(colonist.Map));
                    row("the floor field is the own floor after the " + family.name + " search", fieldAfter == surface, "the floor field is " + describeFloor(fieldAfter));
                    row("the colonist still reaches the cell after the " + family.name + " search", reaches(), "the colonist's reach to " + cell + " is refused");
                }

                // The attack search, with the cross-floor flag on, as the Z-Levels callers set it. No hostile is on any floor.
                startRow();
                bool previousMulti = CombatPatches_BestAttackTarget_Patch.multiMapSearch;
                CombatPatches_BestAttackTarget_Patch.multiMapSearch = true;
                FloorStateProbe.Arm(colonist);
                IAttackTarget target;
                try
                {
                    target = AttackTargetFinder.BestAttackTarget(colonist, TargetScanFlags.NeedThreat | TargetScanFlags.NeedReachable);
                }
                finally
                {
                    FloorStateProbe.ArmedPawn = null;
                    CombatPatches_BestAttackTarget_Patch.multiMapSearch = previousMulti;
                }
                List<Map> attackFloors = FloorStateProbe.Floors.ToList();
                fieldAfter = ZU.Field;
                Record("the attack search", "INFO: target " + (target?.Thing?.ToString() ?? "none") + ", floors searched " + describeFloors(attackFloors));
                row("the attack search looks into the lower floor", target == null && attackFloors.Contains(lower), "target " + (target?.Thing?.ToString() ?? "none") + ", it searched " + describeFloors(attackFloors));
                row("the floor field is the own floor after the attack search", fieldAfter == surface, "the floor field is " + describeFloor(fieldAfter));
                if (colonist.Map != surface || colonist.Position != colonistCell)
                {
                    Record("the colonist after the attack search", "INFO: the attack search left the colonist at " + colonist.Position + " on " + describeFloor(colonist.Map));
                    ZU.Teleport(colonist, surface, colonistCell);
                }
                row("the colonist still reaches the cell after the attack search", reaches(), "the colonist's reach to " + cell + " is refused");

                // Part 4. The reach check throws while the colonist stands on the lower floor.
                harmony.Patch(reachCheck, prefix: new HarmonyMethod(typeof(ReachThrowProbe), nameof(ReachThrowProbe.Prefix)) { priority = Priority.First });
                patchedMethods.Add(reachCheck);
                IntVec3 lowerCell = GenRadial.RadialCellsAround(stairs.Position, 2.5f, false).First(c => c.InBounds(lower) && c.Standable(lower) && c.GetFirstItem(lower) == null);
                Thing lowerStack = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Steel), lowerCell, lower);

                startRow();
                Exception reachThrown = ReachThrowing(colonist, lower, () => colonist.CanReach(lowerStack, PathEndMode.Touch, Danger.Deadly));
                bool thingBack = colonist.Map == surface && colonist.Position == colonistCell;
                row("a reach check to a thing below that throws reaches the caller", IsReachThrow(reachThrown), DescribeReachThrow(reachThrown));
                row("a reach check to a thing below that throws leaves the colonist on its own floor and cell", thingBack, "the colonist is at " + colonist.Position + " on " + describeFloor(colonist.Map));

                // The cell branch: a search filed the lower cell under the lower floor. The reach check builds a target from the cell, which files
                // it again under the floor field, so the field names the lower floor during the call, as inside a search there.
                startRow();
                ZU.Field = lower;
                try
                {
                    reachThrown = ReachThrowing(colonist, lower, () => colonist.CanReach(lowerCell, PathEndMode.OnCell, Danger.Deadly));
                }
                finally
                {
                    ZU.Field = surface;
                }
                bool cellBack = colonist.Map == surface && colonist.Position == colonistCell;
                row("a reach check to a cell filed below that throws reaches the caller", IsReachThrow(reachThrown), DescribeReachThrow(reachThrown));
                row("a reach check to a cell filed below that throws leaves the colonist on its own floor and cell", cellBack, "the colonist is at " + colonist.Position + " on " + describeFloor(colonist.Map));
                if (!cellBack || !thingBack)
                {
                    ZU.Teleport(colonist, surface, colonistCell);
                }

                // A normal reach check still moves the colonist back, through the postfix.
                startRow();
                bool reachesStack = colonist.CanReach(lowerStack, PathEndMode.Touch, Danger.Deadly);
                bool normalBack = colonist.Map == surface && colonist.Position == colonistCell;
                Record("reach to the thing below", "INFO: " + (reachesStack ? "allowed" : "refused"));
                row("a normal reach check to a thing below leaves the colonist on its own floor and cell", normalBack, "the colonist is at " + colonist.Position + " on " + describeFloor(colonist.Map));
                lowerStack.Destroy();

                startRow();
                row("the colonist still reaches the cell after every row", reaches(), "the colonist's reach to " + cell + " is refused");
                Record(label, rows.All(pass => pass) ? "PASS" : "FAIL: " + rows.Count(pass => !pass) + " rows above failed");
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels floor state validation failed: " + exception);
            }
            finally
            {
                FloorStateProbe.ArmedPawn = null;
                ReachThrowProbe.ArmedPawn = null;
                ReachThrowProbe.ArmedMap = null;
                foreach (MethodInfo method in patchedMethods)
                {
                    harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
                }
            }
            Root.Shutdown();
        }

        // Runs the reach check with the throw probe armed for the colonist and floor, and returns what it threw, or null.
        private static Exception ReachThrowing(Pawn colonist, Map throwOn, Func<bool> check)
        {
            ReachThrowProbe.Throws = 0;
            ReachThrowProbe.ArmedPawn = colonist;
            ReachThrowProbe.ArmedMap = throwOn;
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
                ReachThrowProbe.ArmedPawn = null;
                ReachThrowProbe.ArmedMap = null;
            }
        }

        private static bool IsReachThrow(Exception thrown)
        {
            return thrown is InvalidOperationException && thrown.Message == ReachThrowMessage && ReachThrowProbe.Throws == 1;
        }

        private static string DescribeReachThrow(Exception thrown)
        {
            return (thrown == null ? "the reach check did not throw" : "the reach check threw " + thrown.GetType().Name + ": " + thrown.Message) + ", " + ReachThrowProbe.Throws + " throws";
        }
    }
}
