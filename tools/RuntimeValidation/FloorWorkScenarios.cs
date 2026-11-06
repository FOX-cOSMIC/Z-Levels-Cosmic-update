using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 30 (the work filter per floor, idea 6 on docs/mod-comparison/ideas.html). The stove and its raw rice stand on the lower floor, and the
    // colonist on the surface has cooking as its only work. The filter must stop the cooking work at that stove when cooking is off on the lower
    // floor, and must take it again when cooking is allowed there. The setting is saved, so it must still be off after a load.
    public partial class RuntimeValidation
    {
        private bool floorWorkSaved;
        private bool floorWorkChecked;
        private static bool floorWorkBeforeOk;
        private static bool floorWorkPreviousPriorities;
        private static int floorWorkLowerZ = int.MinValue;

        // The cooking work type, by its definition name (WorkTypes.xml). This build has no WorkTypeDefOf.Cooking.
        private static WorkTypeDef CookingWorkType()
        {
            return DefDatabase<WorkTypeDef>.GetNamed("Cooking");
        }

        // One search for the colonist. It returns the first step the search gives back. The tracker keeps the main job (the work) and the
        // destination of the search, and both are cleared first, so an empty search cannot show the search before it.
        private static Job FloorWorkSearch(ZLevelsManager tracker, Pawn colonist, out Job main, out TargetInfo destination)
        {
            // The game's own idle pause (600 ticks after an empty search) would hide the second search, so the clock moves past it first.
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 1000);
            if (tracker.jobTracker != null && tracker.jobTracker.TryGetValue(colonist, out JobTracker before))
            {
                before.targetDest = TargetInfo.Invalid;
                before.mainJob = null;
            }
            Job first = SearchWorkJob(colonist);
            main = first;
            destination = TargetInfo.Invalid;
            if (tracker.jobTracker != null && tracker.jobTracker.TryGetValue(colonist, out JobTracker after))
            {
                destination = after.targetDest;
                if (after.mainJob != null)
                {
                    main = after.mainJob;
                }
            }
            return first;
        }

        // A job that targets the stove as its target or its second target.
        private static bool JobTargetsStove(Job job, Thing stove)
        {
            return job != null && (job.targetA.Thing == stove || job.targetB.Thing == stove);
        }

        // Cooking work at the stove. The first step, the main job, or the destination of the search reaches the stove: a bill on it, a haul into
        // it, or a haul to a cell beside it (the ingredients are brought there first, and that haul is part of the cook job).
        private static bool IsCookAtStove(Job first, Job main, TargetInfo destination, Thing stove)
        {
            if (JobTargetsStove(first, stove) || JobTargetsStove(main, stove))
            {
                return true;
            }
            if (destination.Thing == stove)
            {
                return true;
            }
            return destination.IsValid && destination.Map == stove.Map && stove.OccupiedRect().ExpandedBy(2).Contains(destination.Cell);
        }

        private static string FloorWorkOffRow(ZLevelsManager tracker, Pawn colonist, Thing stove)
        {
            Job first = FloorWorkSearch(tracker, colonist, out Job main, out TargetInfo destination);
            return IsCookAtStove(first, main, destination, stove)
                ? "FAIL: the search takes cooking work at the stove on the lower floor: " + DescribeOwnFloorJob(main)
                : "PASS: the search found " + DescribeOwnFloorJob(main) + ", no cooking work at the stove";
        }

        private static string FloorWorkOnRow(ZLevelsManager tracker, Pawn colonist, Thing stove)
        {
            Job first = FloorWorkSearch(tracker, colonist, out Job main, out TargetInfo destination);
            return IsCookAtStove(first, main, destination, stove)
                ? "PASS: the search takes cooking work at the stove on the lower floor: " + DescribeOwnFloorJob(main)
                : "FAIL: the search found " + DescribeOwnFloorJob(main) + ", not cooking work at the stove";
        }

        private void RunFloorWorkBeforeSave()
        {
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                RequireFloors(tracker, surface, 2);
                CarveAround(lower, stairs.Position, 6f);
                // The lower level has never been seen, and its fog hides things from the search (the need cool-down scenario clears it too).
                lower.fogGrid.ClearAllFog();
                int lowerZ = tracker.GetZIndexFor(lower);
                List<IntVec3> lowerUsed = new List<IntVec3>();
                floorWorkPreviousPriorities = Find.PlaySettings.useWorkPriorities;
                // The priorities are read by the search only while this setting is on, as the own-floor scenario notes.
                Find.PlaySettings.useWorkPriorities = true;

                // A fuelled stove with one cook-a-meal bill, and raw rice on the floor beside it. All of it is on the lower floor.
                ThingDef stoveDef = Def("FueledStove");
                ThingWithComps stove = SpawnPlayerBuilding(lower, stoveDef, FindSupplyCell(lower, stairs.Position, stoveDef.Size));
                stove.TryGetComp<CompRefuelable>().Refuel(30f);
                ((Building_WorkTable)stove).BillStack.AddBill(new Bill_Production(DefDatabase<RecipeDef>.GetNamed("CookMealSimple")));
                Thing rawRice = ThingMaker.MakeThing(Def("RawRice"));
                rawRice.stackCount = 40;
                GenSpawn.Spawn(rawRice, TakeCellNear(lower, OnMapCell(lower, stairs.Position + new IntVec3(3, 0, 3)), lowerUsed), lower);

                // The colonist stands on the surface, and cooking is its only work.
                Pawn cook = SpawnCapableColonistNear(surface, stairs.Position, new List<IntVec3>(), CookingWorkType());
                cook.workSettings.DisableAll();
                cook.workSettings.SetPriority(CookingWorkType(), 1);

                // Row 1: cooking is off on the lower floor, so the search must not take the cooking work at the stove.
                tracker.floorPolicy.SetWorkAllowed(CookingWorkType(), lowerZ, false);
                string offRow = CheckFloorsTabRow(() => FloorWorkOffRow(tracker, cook, stove));
                Record("cooking off on the lower floor: no cook job there", offRow);

                // Row 2: the same colonist and stove, with cooking allowed on the lower floor. The cooking work must be taken.
                tracker.floorPolicy.SetWorkAllowed(CookingWorkType(), lowerZ, true);
                string onRow = CheckFloorsTabRow(() => FloorWorkOnRow(tracker, cook, stove));
                Record("cooking on: the cook job is taken", onRow);

                // Row 3: the setting is saved as off on the lower floor, and it must still be off after the load.
                tracker.floorPolicy.SetWorkAllowed(CookingWorkType(), lowerZ, false);
                floorWorkBeforeOk = offRow.StartsWith("PASS") && onRow.StartsWith("PASS");
                floorWorkLowerZ = lowerZ;
                GameDataSaveLoader.SaveGame("ZLevelsValidation");
                LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("ZLevelsValidation"));
            }
            catch (Exception exception)
            {
                Record("floor work filter", "FAIL: " + exception);
                Log.Error("Z-Levels floor work filter validation failed: " + exception);
                Root.Shutdown();
            }
        }

        private void RunFloorWorkAfterReload()
        {
            try
            {
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                bool stillOff = !tracker.floorPolicy.WorkTypeAllowed(CookingWorkType(), floorWorkLowerZ);
                string survives = stillOff
                    ? "PASS"
                    : "FAIL: after the load cooking is allowed on floor " + floorWorkLowerZ + " again";
                Record("filter survives save and reload", survives);
                Record("floor work filter", floorWorkBeforeOk && survives == "PASS" ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record("floor work filter", "FAIL: " + exception);
                Log.Error("Z-Levels floor work filter validation failed after the load: " + exception);
            }
            Find.PlaySettings.useWorkPriorities = floorWorkPreviousPriorities;
            finished = true;
            Root.Shutdown();
        }
    }
}
