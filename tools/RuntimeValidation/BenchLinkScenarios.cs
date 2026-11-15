using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 32 (workbench floor link, idea "workbench floor link" on docs/mod-comparison/ideas.html). A fuelled stove stands on the surface with one
    // cook-a-meal bill. Raw rice lies beside it on the surface, and more rice lies on the lower floor. A linked stove must fetch the lower rice
    // first. An unlinked stove keeps the search order, which takes the surface rice. The link is set by name on FloorPolicy, so this file builds
    // before the link exists, and the first row fails until it does. The link is also saved, and it must still be there after a load.
    public partial class RuntimeValidation
    {
        private bool benchLinkSaved;
        private bool benchLinkChecked;
        // Static, because the game recreates the component on load, and the rows before the save are read after it.
        private static bool benchLinkBeforeOk;
        private static int benchLinkStoveId = -1;
        private static int benchLinkLowerZ = int.MinValue;
        private static bool benchLinkPreviousPriorities;

        // The rice pile that the chain's first ingredient job fetches, or null when no job in the chain fetches either pile.
        private static Thing FetchedRice(List<Job> chain, Thing surfaceRice, Thing lowerRice)
        {
            foreach (Job job in chain)
            {
                Thing target = job.targetA.Thing;
                if (target != null && (target == surfaceRice || target == lowerRice))
                {
                    return target;
                }
            }
            return null;
        }

        // Links a stove to a floor, or removes the link when floorIndex is null. FloorPolicy.SetBenchFloor is reached by name, so a missing method
        // fails the row instead of the build.
        private static void SetBenchFloorByName(FloorPolicy policy, Thing bench, int? floorIndex)
        {
            System.Reflection.MethodInfo method = typeof(FloorPolicy).GetMethod("SetBenchFloor")
                ?? throw new InvalidOperationException("FloorPolicy has no SetBenchFloor");
            method.Invoke(policy, new object[] { bench, floorIndex });
        }

        private static string DescribeFetch(Thing fetched, Thing surfaceRice, Thing lowerRice)
        {
            if (fetched == null)
            {
                return "nothing";
            }
            return fetched == surfaceRice ? "the surface rice" : fetched == lowerRice ? "the lower rice" : "something else";
        }

        // The rows before the save. The link is set and read here, then the game saves and reloads.
        private void RunBenchLinkBeforeSave()
        {
            const string label = "bench link";
            benchLinkPreviousPriorities = Find.PlaySettings.useWorkPriorities;
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                lower.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 2);
                // The open ground around the stairs, which keeps the lower rice reachable from the stairs. The carve is centred on the stairs, so it
                // cannot clear the stairs up (see the stockpile note in the haul to a floor scenario).
                CarveAround(lower, stairs.Position, 6f);
                int lowerZ = tracker.GetZIndexFor(lower);
                List<IntVec3> surfaceUsed = new List<IntVec3>();
                List<IntVec3> lowerUsed = new List<IntVec3>();
                // The search reads the work priorities only while this setting is on, as the floor work scenario notes.
                Find.PlaySettings.useWorkPriorities = true;

                // A fuelled stove with one cook-a-meal bill. Rice lies beside it on the surface, and more rice lies on the lower floor.
                ThingDef stoveDef = Def("FueledStove");
                ThingWithComps stove = SpawnPlayerBuilding(surface, stoveDef, FindSupplyCell(surface, stairs.Position, stoveDef.Size));
                stove.TryGetComp<CompRefuelable>().Refuel(30f);
                ((Building_WorkTable)stove).BillStack.AddBill(new Bill_Production(DefDatabase<RecipeDef>.GetNamed("CookMealSimple")));
                Thing surfaceRice = ThingMaker.MakeThing(Def("RawRice"));
                surfaceRice.stackCount = 40;
                GenSpawn.Spawn(surfaceRice, TakeCellNear(surface, OnMapCell(surface, stove.Position + new IntVec3(0, 0, -3)), surfaceUsed), surface);
                Thing lowerRice = ThingMaker.MakeThing(Def("RawRice"));
                lowerRice.stackCount = 40;
                GenSpawn.Spawn(lowerRice, TakeCellNear(lower, OnMapCell(lower, stairs.Position + new IntVec3(3, 0, 3)), lowerUsed), lower);

                // Two colonists with cooking as their only work. Each search uses its own colonist, since a job queued by one search would show up in
                // the next search's chain.
                List<IntVec3> avoid = new List<IntVec3>();
                Pawn unlinkedCook = SpawnCapableColonistNear(surface, stove.Position, avoid, CookingWorkType());
                unlinkedCook.workSettings.DisableAll();
                unlinkedCook.workSettings.SetPriority(CookingWorkType(), 1);
                Pawn linkedCook = SpawnCapableColonistNear(surface, stove.Position, avoid, CookingWorkType());
                linkedCook.workSettings.DisableAll();
                linkedCook.workSettings.SetPriority(CookingWorkType(), 1);

                // The unlinked stove comes first. The stove starts with no link, so this row does not touch the link at all.
                Job firstUnlinked = FloorWorkSearch(tracker, unlinkedCook, out Job unlinkedMain, out TargetInfo unlinkedDest);
                List<Job> unlinkedChain = FloorsTabChain(tracker, unlinkedCook, firstUnlinked);
                Thing unlinkedFetch = FetchedRice(unlinkedChain, surfaceRice, lowerRice);
                string unlinkedRow = unlinkedFetch == surfaceRice
                    ? "PASS: the unlinked stove fetches the surface rice, as before; the jobs are " + DescribeFloorsTabChain(unlinkedChain)
                    : "FAIL: the unlinked stove fetches " + DescribeFetch(unlinkedFetch, surfaceRice, lowerRice) + "; the jobs are " + DescribeFloorsTabChain(unlinkedChain);
                Record("an unlinked bench is unchanged", unlinkedRow);

                // The linked stove. Its search must try the lower floor first, and so fetch the lower rice.
                string linkedRow = CheckFloorsTabRow(() =>
                {
                    SetBenchFloorByName(tracker.floorPolicy, stove, lowerZ);
                    Job firstLinked = FloorWorkSearch(tracker, linkedCook, out Job linkedMain, out TargetInfo linkedDest);
                    List<Job> linkedChain = FloorsTabChain(tracker, linkedCook, firstLinked);
                    Thing linkedFetch = FetchedRice(linkedChain, surfaceRice, lowerRice);
                    return linkedFetch == lowerRice
                        ? "PASS: the linked stove fetches the lower rice first; the jobs are " + DescribeFloorsTabChain(linkedChain)
                        : "FAIL: the linked stove fetches " + DescribeFetch(linkedFetch, surfaceRice, lowerRice) + "; the jobs are " + DescribeFloorsTabChain(linkedChain);
                });
                Record("bench linked to the lower floor fetches lower ingredients first", linkedRow);

                // The link is saved with the stove, and the rows after the load read it back.
                benchLinkBeforeOk = unlinkedRow.StartsWith("PASS") && linkedRow.StartsWith("PASS");
                benchLinkStoveId = stove.thingIDNumber;
                benchLinkLowerZ = lowerZ;
                GameDataSaveLoader.SaveGame("ZLevelsValidation");
                LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("ZLevelsValidation"));
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels bench link validation failed: " + exception);
                Find.PlaySettings.useWorkPriorities = benchLinkPreviousPriorities;
                finished = true;
                Root.Shutdown();
            }
        }

        // The row after the load. The stove is found by its id, and its link must still point at the lower floor.
        private void RunBenchLinkAfterReload()
        {
            const string label = "bench link";
            try
            {
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                Thing stove = Find.Maps.SelectMany(map => map.listerThings.AllThings).FirstOrDefault(thing => thing.thingIDNumber == benchLinkStoveId);
                string survives = CheckFloorsTabRow(() =>
                {
                    if (stove == null)
                    {
                        return "FAIL: the stove is not on any map after the load";
                    }
                    int? floorIndex = tracker.floorPolicy.BenchFloor(stove);
                    return floorIndex == benchLinkLowerZ
                        ? "PASS"
                        : "FAIL: after the load the stove is linked to " + (floorIndex == null ? "no floor" : floorIndex.Value.ToString()) + ", expected " + benchLinkLowerZ;
                });
                Record("the link survives save and reload", survives);
                Record(label, benchLinkBeforeOk && survives == "PASS" ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels bench link validation failed after the load: " + exception);
            }
            Find.PlaySettings.useWorkPriorities = benchLinkPreviousPriorities;
            finished = true;
            Root.Shutdown();
        }
    }
}
