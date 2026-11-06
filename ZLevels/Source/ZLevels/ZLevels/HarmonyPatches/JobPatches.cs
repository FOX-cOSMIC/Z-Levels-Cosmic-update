using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZLevels
{
    [StaticConstructorOnStartup]
    public static class JobPatches
    {
        [HarmonyPatch(typeof(ThinkNode_ConditionalShouldFollowMaster), "ShouldFollowMaster")]
        internal static class ShouldFollowMasterPatch
        {
            private static bool Prefix(Pawn pawn, ref bool __result)
            {
                if (!pawn.Spawned || pawn.playerSettings == null)
                {
                    __result = false;
                    return false;
                }
                Pawn respectedMaster = pawn.playerSettings.RespectedMaster;
                if (respectedMaster == null)
                {
                    __result = false;
                    return false;
                }
                if (respectedMaster.Spawned)
                {
                    if (pawn.playerSettings.followDrafted
                        && respectedMaster.Drafted)
                    //&& pawn.CanReach(respectedMaster, PathEndMode.OnCell, Danger.Deadly))
                    {
                        __result = true;
                        return false;
                    }
                    if (pawn.playerSettings.followFieldwork
                        && respectedMaster.mindState.lastJobTag == JobTag.Fieldwork)
                    //&& pawn.CanReach(respectedMaster, PathEndMode.OnCell, Danger.Deadly))
                    {
                        __result = true;
                        return false;
                    }
                }
                else
                {
                    Pawn carriedBy = respectedMaster.CarriedBy;
                    if (carriedBy != null && carriedBy.HostileTo(respectedMaster) && pawn.CanReach(carriedBy, PathEndMode.OnCell, Danger.Deadly))
                    {
                        __result = true;
                        return false;

                    }
                }
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(JobGiver_AIFollowPawn), "TryGiveJob")]
        public class JobGiver_AIFollowPawnPatch
        {
            private static bool Prefix(JobGiver_AIFollowPawn __instance, ref Job __result, Pawn pawn)
            {
                if (!ZUtils.HasExtraFloors(pawn)) return true;
                try
                {
                    var ZTracker = ZUtils.ZTracker;
                    if (ZTracker.jobTracker == null)
                    {
                        ZTracker.jobTracker = new Dictionary<Pawn, JobTracker>();
                    }
                    if (!ZTracker.jobTracker.TryGetValue(pawn, out JobTracker jobTracker))
                    {
                        jobTracker = new JobTracker();
                        ZTracker.jobTracker[pawn] = jobTracker;
                    }
                    if (jobTracker?.activeJobs?.Any() ?? false)
                    {
                        if (!pawn.jobs.jobQueue.Contains(jobTracker.activeJobs[0]))
                        {
                            if (jobTracker.activeJobs[0].def.defName != "UnloadYourHauledInventory"
                                && jobTracker.activeJobs[0].TryMakePreToilReservations(pawn, false))
                            {
                                __result = jobTracker.activeJobs[0];
                                jobTracker.activeJobs.RemoveAt(0);
                                return false;
                            }
                        }
                        else if (pawn.jobs.curJob == null)
                        {
                            __result = jobTracker.activeJobs[0];
                            jobTracker.activeJobs.RemoveAt(0);
                            return false;
                        }
                        else if (pawn.jobs.curJob != jobTracker.activeJobs[0])
                        {
                            __result = jobTracker.activeJobs[0];
                            jobTracker.activeJobs.RemoveAt(0);
                            return false;
                        }
                    }
                    Job result;
                    var oldMap = pawn.Map;
                    var oldPosition = pawn.Position;
                    bool select = false;
                    if (Find.Selector.SelectedObjects.Contains(pawn)) select = true;
                    ZTracker.ResetJobTrackerFor(pawn);

                    using (new FloorRestore(pawn, oldMap, oldPosition))
                    foreach (var otherMap in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrder(pawn, oldMap, oldPosition)))
                    {
                        ZLogger.Message("Searching follow job for " + pawn + " in " + ZTracker.GetMapInfo(otherMap)
                            + " for " + ZTracker.GetMapInfo(oldMap));
                        result = JobGiver_AIFollowPawnPatch.TryGiveJob(pawn, __instance);
                        if (result != null)
                        {
                            ZLogger.Message(pawn + " got follow job " + result + " - map: "
                                + ZTracker.GetMapInfo(pawn.Map) + " - " + pawn.Position);
                            ZUtils.TeleportThing(pawn, oldMap, oldPosition);
                            ZTracker.BuildJobListFor(pawn, otherMap, result);
                            __result = jobTracker.activeJobs[0];
                            jobTracker.activeJobs.RemoveAt(0);
                            break;
                        }
                    }
                    if (select) Find.Selector.Select(pawn);
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Error("Some kind of error occurred in Z-Levels JobManager: " + ex);
                }
                return true;
            }

            public static Job TryGiveJob(Pawn pawn, JobGiver_AIFollowPawn __instance)
            {
                Pawn followee = __instance.GetFollowee(pawn);
                if (followee == null)
                {
                    return null;
                }
                if (!followee.Spawned || !pawn.CanReach(followee, PathEndMode.OnCell, Danger.Deadly, false))
                {
                    return null;
                }
                ZLogger.Message(pawn + " starting following " + followee);
                float radius = __instance.GetRadius(pawn);
                if (!JobDriver_FollowClose.FarEnoughAndPossibleToStartJob(pawn, followee, radius))
                {
                    return null;
                }
                Job job = JobMaker.MakeJob(JobDefOf.FollowClose, followee);
                job.expiryInterval = __instance.FollowJobExpireInterval;
                job.checkOverrideOnExpire = true;
                job.followRadius = radius;
                return job;
            }
        }

        [HarmonyPatch(typeof(JobDriver_Ingest))]
        [HarmonyPatch("PrepareToIngestToils_ToolUser")]
        public static class Patch_PrepareToIngestToils_ToolUser_Postfix
        {
            private static void Postfix(ref IEnumerable<Toil> __result, JobDriver_Ingest __instance, Toil chewToil)
            {
                if (!ZUtils.HasExtraFloors(__instance.pawn)) return;
                var list = __result.ToList<Toil>();
                Thing IngestibleSource = __instance.job.GetTarget(TargetIndex.A).Thing;
                Pawn actor = __instance.pawn;
                Thing thing = null;
                IntVec3 intVec = IntVec3.Invalid;
                var ZTracker = ZUtils.ZTracker;
                var oldMap = actor.Map;
                var oldPosition = actor.Position;
                bool select = false;
                if (Find.Selector.SelectedObjects.Contains(actor)) select = true;
                Predicate<Thing> baseChairValidator = delegate (Thing t)
                {
                    if (t.def.building == null || !t.def.building.isSittable)
                    {
                        return false;
                    }
                    if (t.IsForbidden(actor))
                    {
                        return false;
                    }
                    if (!actor.CanReserve(t))
                    {
                        return false;
                    }
                    if (!t.IsSociallyProper(actor))
                    {
                        return false;
                    }
                    if (t.IsBurning())
                    {
                        return false;
                    }
                    if (t.HostileTo(actor))
                    {
                        return false;
                    }
                    bool flag = false;
                    for (int i = 0; i < 4; i++)
                    {
                        Building edifice = (t.Position + GenAdj.CardinalDirections[i]).GetEdifice(t.Map);
                        if (edifice != null && edifice.def.surfaceType == SurfaceType.Eat)
                        {
                            flag = true;
                            break;
                        }
                    }
                    return flag ? true : false;
                };
                if (oldMap != null)
                {
                    using (new FloorRestore(actor, oldMap, oldPosition))
                    foreach (var otherMap in ZUtils.ZTracker.floorPolicy.Allowed(actor, ZUtils.GetAllMapsInClosestOrder(actor, oldMap, oldPosition)))
                    {
                        if (IngestibleSource.def.ingestible.chairSearchRadius > 0f)
                        {
                            thing = GenClosest.ClosestThingReachable(actor.Position, actor.Map,
                                ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial), PathEndMode.OnCell,
                                TraverseParms.For(actor), IngestibleSource.def.ingestible.chairSearchRadius,
                                (Thing t) => baseChairValidator(t) && t.Position.GetDangerFor(actor, t.Map) == Danger.None);
                        }
                        if (thing == null)
                        {
                            try
                            {
                                intVec = RCellFinder.SpotToChewStandingNear(actor, actor.CurJob.GetTarget(TargetIndex.A).Thing);
                            }
                            catch
                            {
                                intVec = actor.Position;
                            }
                            Danger chewSpotDanger = intVec.GetDangerFor(actor, actor.Map);
                            if (chewSpotDanger != Danger.None)
                            {
                                thing = GenClosest.ClosestThingReachable(actor.Position, actor.Map,
                                    ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial), PathEndMode.OnCell,
                                    TraverseParms.For(actor), IngestibleSource.def.ingestible.chairSearchRadius, (Thing t) =>
                                    baseChairValidator(t) && (int)t.Position.GetDangerFor(actor, t.Map) <= (int)chewSpotDanger);
                            }
                        }

                        if (thing != null)
                        {
                            intVec = thing.Position;
                            actor.Reserve(thing, actor.CurJob);
                            ZLogger.Message(actor + " - Found: " + thing);
                            break;
                        }
                    }

                    if (select) Find.Selector.Select(actor);

                    if (thing != null && thing.Map != null && thing.Map != actor.Map)
                    {
                        if (!IngestibleSource.def.IsDrug)
                        {
                            list.InsertRange(list.Count - 2, Toils_ZLevels.GoToMap(__instance.GetActor()
                                , thing.Map, __instance));
                        }
                        else
                        {
                            list.InsertRange(list.Count - 1, Toils_ZLevels.GoToMap(__instance.GetActor()
                                , thing.Map, __instance));
                        }
                    }
                }


                __result = list;
            }
        }

        [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
        public class JobGiver_GetFoodPatch
        {
            [HarmonyPrefix]
            private static bool JobGiver_GetFoodPrefix(JobGiver_GetFood __instance, ref Job __result, float ___maxLevelPercentage, HungerCategory ___minCategory, Pawn pawn)
            {
                if (!ZUtils.HasExtraFloors(pawn)) return true;
                if (pawn.def.race.Humanlike)
                {
                    ZLogger.Message(pawn + " starting food search");
                    try
                    {
                        var ZTracker = ZUtils.ZTracker;

                        if (ZTracker.jobTracker == null)
                        {
                            ZTracker.jobTracker = new Dictionary<Pawn, JobTracker>();
                        }

                        if (!ZTracker.jobTracker.TryGetValue(pawn, out JobTracker jobTracker))
                        {
                            jobTracker = new JobTracker();
                            ZTracker.jobTracker[pawn] = jobTracker;
                        }
                        if (jobTracker?.activeJobs?.Any() ?? false)
                        {
                            if (pawn.needs.food.CurCategory < HungerCategory.Starving
                                && !pawn.jobs.jobQueue.Contains(jobTracker.activeJobs[0]))
                            {
                                if (jobTracker.activeJobs[0].def.defName != "UnloadYourHauledInventory"
                                    && jobTracker.activeJobs[0].TryMakePreToilReservations(pawn, false))
                                {
                                    ZLogger.Message("Queue: " + jobTracker.activeJobs[0]);
                                    pawn.jobs.jobQueue.EnqueueLast(jobTracker.activeJobs[0]);
                                    return false;
                                }
                            }
                            else if (pawn.needs.food.CurCategory < HungerCategory.Starving
                                && pawn.jobs.curJob == null
                                && jobTracker.activeJobs[0].TryMakePreToilReservations(pawn, false))
                            {
                                ZLogger.Message("1 START JOB "
                                    + jobTracker.activeJobs[0] + " FOR " + pawn);
                                __result = jobTracker.activeJobs[0];
                                jobTracker.activeJobs.RemoveAt(0);

                                return false;
                            }
                            else if (pawn.jobs.curJob == null && jobTracker.activeJobs[0] != null)
                            {
                                ZLogger.Message("2 START JOB " + jobTracker.activeJobs[0]
                                    + " FOR " + pawn);
                                __result = jobTracker.activeJobs[0];
                                jobTracker.activeJobs.RemoveAt(0);
                                return false;
                            }
                        }
                        Job result;
                        var oldMap = pawn.Map;
                        var oldPosition = pawn.Position;
                        bool select = false;
                        if (Find.Selector.SelectedObjects.Contains(pawn)) select = true;
                        ZTracker.ResetJobTrackerFor(pawn);
                        try
                        {
                            if (pawn.MentalStateDef != null)
                            {
                                result = JobGiver_GetFoodPatch.TryGiveJob(pawn, __instance.forceScanWholeMap, ___maxLevelPercentage, ___minCategory);
                                if (result != null && result.targetA.Thing != null && result.targetA.Thing.Map == pawn.Map)
                                {
                                    __result = result;
                                    return false;
                                }
                                else
                                {
                                    return true;
                                }
                            }

                            // Needs cool-down (DM5): a redirect to another floor waits 2000 ticks after the last one, unless the colonist is very hungry.
                            int redirectNow = Find.TickManager.TicksGame;
                            bool redirectOpen = ZTracker.floorPolicy.RedirectAllowed(pawn, redirectNow, pawn.needs.food.CurCategory >= HungerCategory.UrgentlyHungry);
                            using (new FloorRestore(pawn, oldMap, oldPosition))
                            foreach (var otherMap in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrder(pawn, oldMap, oldPosition)).Where(candidate => ZUtils.ZTracker.floorPolicy.NeedAllowsMap(pawn, candidate, NeedKind.Food)))
                            {
                                if (otherMap != oldMap && !redirectOpen) continue;
                                ZLogger.Message("Searching food job for " + pawn + " in " + ZTracker.GetMapInfo(otherMap)
                                    + " for " + ZTracker.GetMapInfo(oldMap));
                                result = JobGiver_GetFoodPatch.TryGiveJob(pawn, __instance.forceScanWholeMap,
                                    ___maxLevelPercentage, ___minCategory);
                                if (result != null)
                                {
                                    if (otherMap != oldMap) ZTracker.floorPolicy.NoteRedirect(pawn, redirectNow);
                                    ZLogger.Message(pawn + " got food job " + result + " - map: "
                                        + ZTracker.GetMapInfo(pawn.Map) + " - " + pawn.Position);
                                    ZUtils.TeleportThing(pawn, oldMap, oldPosition);

                                    ZTracker.BuildJobListFor(pawn, otherMap, result);
                                    __result = jobTracker.activeJobs[0];
                                    jobTracker.activeJobs.RemoveAt(0);
                                    break;
                                }
                            }

                            if (select) Find.Selector.Select(pawn);
                            return false;
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Some kind of error occurred in Z-Levels JobManager (reverting cloning bug): " + ex);
                            ZUtils.TeleportThing(pawn, oldMap, oldPosition);
                            if (select) Find.Selector.Select(pawn);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Some kind of error occurred in Z-Levels JobManager: " + ex);
                    }
                }
                return true;
            }

            private static Job TryGiveJob(Pawn pawn, bool forceScanWholeMap, float maxLevelPercentage, HungerCategory minCategory)
            {
                Need_Food food = pawn.needs.food;
                if (food == null || (int)food.CurCategory < (int)minCategory || food.CurLevelPercentage > maxLevelPercentage)
                {
                    return null;
                }
                bool allowCorpse;
                if (pawn.AnimalOrWildMan())
                {
                    allowCorpse = true;
                }
                else
                {
                    Hediff firstHediffOfDef = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Malnutrition);
                    allowCorpse = firstHediffOfDef != null && firstHediffOfDef.Severity > 0.4f;
                }
                bool desperate = pawn.needs.food.CurCategory == HungerCategory.Starving;
                if (!FoodUtility.TryFindBestFoodSourceFor(pawn, pawn, desperate, out var foodSource, out var foodDef, canRefillDispenser: true, canUseInventory: true, canUsePackAnimalInventory: true, allowForbidden: false, allowCorpse, allowSociallyImproper: false, pawn.IsWildMan(), forceScanWholeMap))
                {
                    return null;
                }
                Pawn pawn2 = foodSource as Pawn;
                if (pawn2 != null)
                {
                    Job job = JobMaker.MakeJob(JobDefOf.PredatorHunt, pawn2);
                    job.killIncappedTarget = true;
                    return job;
                }
                if (foodSource is Plant && foodSource.def.plant.harvestedThingDef == foodDef)
                {
                    return JobMaker.MakeJob(JobDefOf.Harvest, foodSource);
                }
                Building_NutrientPasteDispenser building_NutrientPasteDispenser = foodSource as Building_NutrientPasteDispenser;
                if (building_NutrientPasteDispenser != null && !building_NutrientPasteDispenser.HasEnoughFeedstockInHoppers())
                {
                    Building building = building_NutrientPasteDispenser.AdjacentReachableHopper(pawn);
                    if (building != null)
                    {
                        ISlotGroupParent hopperSgp = building as ISlotGroupParent;
                        Job job2 = WorkGiver_CookFillHopper.HopperFillFoodJob(pawn, hopperSgp, forced: false);
                        if (job2 != null)
                        {
                            return job2;
                        }
                    }
                    foodSource = FoodUtility.BestFoodSourceOnMap(pawn, pawn, desperate, out foodDef, FoodPreferability.MealLavish, allowPlant: false, !pawn.IsTeetotaler(), allowCorpse: false, allowDispenserFull: false, allowDispenserEmpty: false, allowForbidden: false, allowSociallyImproper: false, allowHarvest: false, forceScanWholeMap);
                    if (foodSource == null)
                    {
                        return null;
                    }
                }
                float nutrition = FoodUtility.GetNutrition(pawn, foodSource, foodDef);
                Pawn pawn3 = (foodSource.ParentHolder as Pawn_InventoryTracker)?.pawn;
                if (pawn3 != null && pawn3 != pawn)
                {
                    Job job3 = JobMaker.MakeJob(JobDefOf.TakeFromOtherInventory, foodSource, pawn3);
                    job3.count = FoodUtility.WillIngestStackCountOf(pawn, foodDef, nutrition);
                    return job3;
                }
                Job job4 = JobMaker.MakeJob(JobDefOf.Ingest, foodSource);
                job4.count = FoodUtility.WillIngestStackCountOf(pawn, foodDef, nutrition);
                return job4;
            }
        }

        [HarmonyPatch(typeof(JobGiver_GetJoy), "TryGiveJob")]
        public class JobGiver_GetJoyPatch
        {
            [HarmonyPrefix]
            private static bool JobGiver_GetJoyPrefix(JobGiver_GetJoy __instance, ref Job __result,
                 ref DefMap<JoyGiverDef, float> ___joyGiverChances, Pawn pawn)
            {
                if (!ZUtils.HasExtraFloors(pawn)) return true;
                try
                {
                    ZLogger.Message(pawn + " starting joy search");
                    var ZTracker = ZUtils.ZTracker;
                    if (ZTracker.jobTracker == null)
                    {
                        ZTracker.jobTracker = new Dictionary<Pawn, JobTracker>();
                    }
                    if (!ZTracker.jobTracker.TryGetValue(pawn, out JobTracker jobTracker))
                    {
                        jobTracker = new JobTracker();
                        ZTracker.jobTracker[pawn] = jobTracker;
                    }
                    if (jobTracker?.activeJobs?.Any() ?? false)
                    {
                        if (pawn?.needs?.joy?.CurCategory > JoyCategory.Low
                            && !pawn.jobs.jobQueue.Contains(jobTracker.activeJobs[0]))
                        {
                            if (jobTracker.activeJobs[0].def.defName != "UnloadYourHauledInventory"
                                && jobTracker.activeJobs[0].TryMakePreToilReservations(pawn, false))
                            {
                                ZLogger.Message("Queue: " + jobTracker.activeJobs[0]);
                                pawn.jobs.jobQueue.EnqueueLast(jobTracker.activeJobs[0]);
                                return false;
                            }

                        }
                        else if (pawn?.needs?.joy?.CurCategory > JoyCategory.Low && pawn.jobs.curJob == null
                            && jobTracker.activeJobs[0].TryMakePreToilReservations(pawn, false))
                        {

                            //ZLogger.Message("2 START JOB "
                            //    + jobTracker.activeJobs[0] + " FOR " + pawn);
                            //pawn.jobs.StartJob(jobTracker.activeJobs[0]);
                            //jobTracker.activeJobs.RemoveAt(0);
                            ZLogger.Message("Return 4");
                            return false;
                        }
                        else if (pawn?.needs?.joy?.CurCategory <= JoyCategory.Low && pawn.jobs.curJob != jobTracker.activeJobs[0])
                        {
                            ZLogger.Message("2 RESETTING JOB TRACKER FOR " + pawn);
                            ZLogger.Message(pawn + " - pawn.jobs.curJob: " + pawn.jobs.curJob);
                            ZLogger.Message(pawn + " - jobTracker.activeJobs[0]: " + jobTracker.activeJobs[0]);
                            foreach (var job in pawn.jobs.jobQueue)
                            {
                                ZLogger.Message(pawn + " - job in pawn queue: " + job.job);
                            }
                            foreach (var job in jobTracker.activeJobs)
                            {
                                ZLogger.Message(pawn + " - job in ZTracker queue: " + job);
                            }
                            ZTracker.ResetJobTrackerFor(pawn);
                        }
                    }
                    Job result = null;
                    var oldMap = pawn.Map;
                    var oldPosition = pawn.Position;
                    bool select = false;
                    if (Find.Selector.SelectedObjects.Contains(pawn)) select = true;
                    ZTracker.ResetJobTrackerFor(pawn);

                    try
                    {
                        var jobList = new Dictionary<Job, Map>();
                        bool CanDoDuringMedicalRest = __instance.CanDoDuringMedicalRest;
                        bool InBed = pawn.InBed();

                        if (pawn.MentalStateDef != null)
                        {
                            __result = TryGiveJob(pawn, CanDoDuringMedicalRest, InBed, ___joyGiverChances, __instance);
                            return false;
                        }
                        // Needs cool-down (DM5): fun is a redirect too, and it shares the clock with food.
                        int redirectNow = Find.TickManager.TicksGame;
                        bool redirectOpen = ZTracker.floorPolicy.RedirectAllowed(pawn, redirectNow, false);
                        using (new FloorRestore(pawn, oldMap, oldPosition))
                        foreach (var otherMap in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrder(pawn, oldMap, oldPosition)).Where(candidate => ZUtils.ZTracker.floorPolicy.NeedAllowsMap(pawn, candidate, NeedKind.Fun)))
                        {
                            if (otherMap != oldMap && !redirectOpen) continue;
                            ZLogger.Message(pawn + " - other map: " + otherMap);
                            ZLogger.Message("Searching joy job for " + pawn + " in " + ZTracker.GetMapInfo(otherMap)
                                + " for " + ZTracker.GetMapInfo(oldMap));

                            result = TryGiveJob(pawn, CanDoDuringMedicalRest, InBed, ___joyGiverChances, __instance);
                            if (result != null)
                            {
                                ZLogger.Message(pawn + " got joy job " + result + " - map: "
                                    + ZTracker.GetMapInfo(pawn.Map) + " - " + pawn.Position);
                                jobList[result] = otherMap;
                            }
                        }

                        if (jobList.Count > 0 && jobList.Keys.TryRandomElementByWeight(j => j.def.joyGainRate, out Job job))
                        {
                            //var job = jobList.RandomElement();
                            ZLogger.Message(pawn + " found joy " + job);
                            if (jobList[job] != oldMap) ZTracker.floorPolicy.NoteRedirect(pawn, redirectNow);
                            ZTracker.BuildJobListFor(pawn, jobList[job], job);
                            __result = jobTracker.activeJobs[0];
                            jobTracker.activeJobs.RemoveAt(0);
                        }
                        else
                        {
                            ZLogger.Message(pawn + " cant find joy job");
                        }

                        if (select) Find.Selector.Select(pawn);

                        return false;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Some kind of error occurred in Z-Levels JobManager (reverting cloning bug): " + ex);
                        ZUtils.TeleportThing(pawn, oldMap, oldPosition);
                        if (select) Find.Selector.Select(pawn);
                    }

                }
                catch (Exception ex)
                {
                    Log.Error("Some kind of error occurred in Z-Levels JobManager: " + ex);
                }
                return true;
            }
            public static Job TryGiveJob(Pawn pawn, bool CanDoDuringMedicalRest, bool InBed, DefMap<JoyGiverDef, float> joyGiverChances, JobGiver_GetJoy __instance)
            {
                if (!CanDoDuringMedicalRest && InBed && HealthAIUtility.ShouldSeekMedicalRest(pawn))
                {
                    return null;
                }
                List<JoyGiverDef> allDefsListForReading = DefDatabase<JoyGiverDef>.AllDefsListForReading;
                JoyToleranceSet tolerances = pawn.needs.joy.tolerances;
                for (int i = 0; i < allDefsListForReading.Count; i++)
                {
                    JoyGiverDef joyGiverDef = allDefsListForReading[i];
                    joyGiverChances[joyGiverDef] = 0f;
                    if (pawn.needs.joy.tolerances.BoredOf(joyGiverDef.joyKind) || !joyGiverDef.Worker.CanBeGivenTo(pawn))
                    {
                        continue;
                    }
                    if (joyGiverDef.pctPawnsEverDo < 1f)
                    {
                        Rand.PushState(pawn.thingIDNumber ^ 0x3C49C49);
                        if (Rand.Value >= joyGiverDef.pctPawnsEverDo)
                        {
                            Rand.PopState();
                            continue;
                        }
                        Rand.PopState();
                    }
                    float num = tolerances[joyGiverDef.joyKind];
                    float b = Mathf.Pow(1f - num, 5f);
                    b = Mathf.Max(0.001f, b);
                    joyGiverChances[joyGiverDef] = joyGiverDef.Worker.GetChance(pawn) * b;
                }
                for (int j = 0; j < joyGiverChances.Count; j++)
                {
                    if (!allDefsListForReading.TryRandomElementByWeight((JoyGiverDef d) => joyGiverChances[d], out JoyGiverDef result))
                    {
                        break;
                    }
                    try
                    {
                        Job job = __instance.TryGiveJobFromJoyGiverDefDirect(result, pawn);
                        if (job != null)
                        {
                            return job;
                        }
                        joyGiverChances[result] = 0f;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Error in JobGiver_GetJoy: " + ex + " - " + result);
                    }
                }
                return null;
            }
        }

        [HarmonyPatch(typeof(JobGiver_GetRest), "TryGiveJob")]
        public class JobGiver_GetRestPatch
        {
            [HarmonyPrefix]
            private static bool JobGiver_GetRestPrefix(JobGiver_GetRest __instance, ref Job __result,
                RestCategory ___minCategory, float ___maxLevelPercentage, Pawn pawn)
            {
                if (!ZUtils.HasExtraFloors(pawn)) return true;
                try
                {
                    var ZTracker = ZUtils.ZTracker;
                    if (pawn.Faction == Faction.OfPlayer)
                    {
                        ZLogger.Message(pawn + " search for rest job");
                        if (ZTracker.jobTracker == null)
                        {
                            ZTracker.jobTracker = new Dictionary<Pawn, JobTracker>();
                        }
                        if (!ZTracker.jobTracker.TryGetValue(pawn, out JobTracker jobTracker))
                        {
                            jobTracker = new JobTracker();
                            ZTracker.jobTracker[pawn] = jobTracker;
                        }
                        if (jobTracker?.activeJobs?.Any() ?? false)
                        {
                            if (pawn.needs.rest.CurCategory < RestCategory.Exhausted &&
                                !pawn.jobs.jobQueue.Contains(jobTracker.activeJobs[0]))
                            {
                                if (jobTracker.activeJobs[0].def.defName != "UnloadYourHauledInventory"
                                    && jobTracker.activeJobs[0].TryMakePreToilReservations(pawn, false))
                                {
                                    ZLogger.Message("Queue: " + jobTracker.activeJobs[0]);

                                    try
                                    {
                                        ZLogger.Message("--------------------------");
                                        for (int i = jobTracker.mainJob.targetQueueB.Count - 1; i >= 0; i--)
                                        {
                                            var target = jobTracker.mainJob.targetQueueB[i];

                                            ZLogger.Message("31 job.targetQueueB: " + target.Thing);
                                            ZLogger.Message("31 job.targetQueueB.Map: " + target.Thing.Map);
                                            ZLogger.Message("31 job.targetQueueB.stackCount: " + target.Thing.stackCount);
                                            ZLogger.Message("31 job.targetQueueB.countQueue: " + jobTracker.mainJob.countQueue[i]);
                                        }
                                    }
                                    catch { }

                                    pawn.jobs.jobQueue.EnqueueLast(jobTracker.activeJobs[0]);

                                    return false;
                                }
                            }
                            else if (pawn.needs.rest.CurCategory < RestCategory.Exhausted &&
                                pawn.jobs.curJob == null)
                            {
                                ZLogger.Message("3 START JOB "
                                    + jobTracker.activeJobs[0] + " FOR " + pawn);
                                __result = jobTracker.activeJobs[0];
                                jobTracker.activeJobs.RemoveAt(0);

                                return false;
                            }
                            else if (pawn.needs.rest.CurCategory >= RestCategory.Exhausted &&
                                pawn.jobs.curJob != jobTracker.activeJobs[0])
                            {
                                ZLogger.Message("3 RESETTING JOB TRACKER FOR " + pawn);
                                ZLogger.Message(pawn + " - pawn.jobs.curJob: " + pawn.jobs.curJob);
                                ZLogger.Message(pawn + " - jobTracker.activeJobs[0]: " + jobTracker.activeJobs[0]);
                                foreach (var job in pawn.jobs.jobQueue)
                                {
                                    ZLogger.Message(pawn + " - job in pawn queue: " + job.job);
                                }
                                foreach (var job in jobTracker.activeJobs)
                                {
                                    ZLogger.Message(pawn + " - job in ZTracker queue: " + job);
                                }
                                ZTracker.ResetJobTrackerFor(pawn);

                            }
                        }

                        Job result = null;
                        var oldMap = pawn.Map;
                        var oldPosition = pawn.Position;
                        bool select = false;
                        if (Find.Selector.SelectedObjects.Contains(pawn)) select = true;
                        ZTracker.ResetJobTrackerFor(pawn);

                        try
                        {
                            if (pawn.MentalStateDef != null)
                            {
                                result = JobGiver_GetRestPatch.TryGiveJob(pawn, ___minCategory, ___maxLevelPercentage);
                                if (result != null && result.targetA.Thing != null && result.targetA.Thing.Map == pawn.Map)
                                {
                                    __result = result;
                                    return false;
                                }
                                else
                                {
                                    return true;
                                }
                            }
                            // A set sleep floor restricts the ground spot as well as the beds. A ground spot found on the set floor is routed there.
                            bool sleepFloorSet = ZUtils.ZTracker.floorPolicy.NeedFloorFor(pawn, NeedKind.Sleep) != null;
                            bool groundRouted = false;
                            using (new FloorRestore(pawn, oldMap, oldPosition))
                            foreach (var otherMap in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrder(pawn, oldMap, oldPosition)).Where(candidate => ZUtils.ZTracker.floorPolicy.NeedAllowsMap(pawn, candidate, NeedKind.Sleep)))
                            {
                                ZLogger.Message("Searching rest job for " + pawn + " in " + ZTracker.GetMapInfo(otherMap)
                                    + " for " + ZTracker.GetMapInfo(oldMap));
                                result = JobGiver_GetRestPatch.TryGiveJob(pawn, ___minCategory, ___maxLevelPercentage);
                                if (result != null && result.targetA.Thing != null)
                                {
                                    ZLogger.Message(pawn + " got rest job " + result + " - map: "
                                        + ZTracker.GetMapInfo(pawn.Map) + " - " + pawn.Position);
                                    ZUtils.TeleportThing(pawn, oldMap, oldPosition);

                                    ZTracker.BuildJobListFor(pawn, result.targetA.Thing.Map, result);
                                    __result = jobTracker.activeJobs[0];
                                    jobTracker.activeJobs.RemoveAt(0);
                                    break;
                                }
                                if (result != null && sleepFloorSet && otherMap != oldMap)
                                {
                                    ZLogger.Message(pawn + " taking rest on the ground on " + ZTracker.GetMapInfo(otherMap));
                                    // The route is built while the colonist stands on this floor, so it leads to the spot here.
                                    ZTracker.BuildJobListFor(pawn, otherMap, result);
                                    ZUtils.TeleportThing(pawn, oldMap, oldPosition);
                                    __result = jobTracker.activeJobs[0];
                                    jobTracker.activeJobs.RemoveAt(0);
                                    groundRouted = true;
                                    break;
                                }
                            }
                            if (result == null)
                            {
                                // No floor gave a job. Vanilla decides, as it does when a colonist is not tired enough.
                                if (select) Find.Selector.Select(pawn);
                                return true;
                            }
                            if (!groundRouted && result.targetA.Thing == null)
                            {
                                ZLogger.Message(pawn + " taking rest on the ground");
                                __result = JobMaker.MakeJob(JobDefOf.LayDown, FindGroundSleepSpotFor(pawn));
                            }
                            if (select) Find.Selector.Select(pawn);

                            return false;
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Some kind of error occurred in Z-Levels JobManager (reverting cloning bug): " + ex);
                            ZUtils.TeleportThing(pawn, oldMap, oldPosition);
                            if (select) Find.Selector.Select(pawn);
                        }

                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Some kind of error occurred in Z-Levels JobManager: " + ex);
                }
                return true;
            }

            private static Job TryGiveJob(Pawn pawn, RestCategory minCategory, float maxLevelPercentage = 1f)
            {
                Need_Rest rest = pawn.needs.rest;
                if (rest == null || rest.CurCategory < minCategory || rest.CurLevelPercentage > maxLevelPercentage)
                {
                    return null;
                }
                if (RestUtility.DisturbancePreventsLyingDown(pawn))
                {
                    return null;
                }
                Lord lord = pawn.GetLord();
                Building_Bed building_Bed;
                var ZTracker = ZUtils.ZTracker;
                if ((lord != null && lord.CurLordToil != null && !lord.CurLordToil.AllowRestingInBed) || pawn.IsWildMan())
                {
                    building_Bed = null;
                }
                else
                {
                    if (pawn.ownership?.OwnedBed != null && ZTracker.floorPolicy.Allowed(pawn, ZTracker.GetAllMaps(pawn.Map.Tile))
                        .Contains(pawn.ownership?.OwnedBed?.Map) && ZUtils.ZTracker.floorPolicy.NeedAllowsMap(pawn, pawn.ownership.OwnedBed.Map, NeedKind.Sleep))
                    {
                        building_Bed = pawn.ownership.OwnedBed;
                    }
                    else
                    {
                        building_Bed = RestUtility.FindBedFor(pawn);
                    }
                }
                if (building_Bed != null)
                {
                    ZLogger.Message("Found " + building_Bed + " for " + pawn + " in " + ZTracker.GetMapInfo(building_Bed.Map));
                    return JobMaker.MakeJob(JobDefOf.LayDown, building_Bed);
                }
                return JobMaker.MakeJob(JobDefOf.LayDown, FindGroundSleepSpotFor(pawn));
            }

            private static IntVec3 FindGroundSleepSpotFor(Pawn pawn)
            {
                Map map = pawn.Map;
                for (int i = 0; i < 2; i++)
                {
                    int radius = (i == 0) ? 4 : 12;
                    if (CellFinder.TryRandomClosewalkCellNear(pawn.Position, map, radius, out IntVec3 result, (IntVec3 x) => !x.IsForbidden(pawn) && !x.GetTerrain(map).avoidWander))
                    {
                        return result;
                    }
                }
                return CellFinder.RandomClosewalkCellNearNotForbidden(pawn, 4);
            }
        }


        // The psyfocus meditation search (Royalty). The vanilla search runs on the colonist's own map only, so a meditation floor set to
        // another floor searches that floor's map instead, and the route is built to the spot there, as the rest and food searches do.
        [HarmonyPatch(typeof(JobGiver_Meditate), "TryGiveJob")]
        public class JobGiver_MeditatePatch
        {
            [HarmonyPrefix]
            private static bool JobGiver_MeditatePrefix(ref Job __result, Pawn pawn)
            {
                if (!ZUtils.HasExtraFloors(pawn)) return true;
                if (ZUtils.ZTracker.floorPolicy.NeedFloorFor(pawn, NeedKind.Meditation) == null) return true;
                try
                {
                    var ZTracker = ZUtils.ZTracker;
                    if (ZTracker.jobTracker == null)
                    {
                        ZTracker.jobTracker = new Dictionary<Pawn, JobTracker>();
                    }
                    if (!ZTracker.jobTracker.TryGetValue(pawn, out JobTracker jobTracker))
                    {
                        jobTracker = new JobTracker();
                        ZTracker.jobTracker[pawn] = jobTracker;
                    }
                    var oldMap = pawn.Map;
                    var oldPosition = pawn.Position;
                    bool select = Find.Selector.SelectedObjects.Contains(pawn);
                    ZTracker.ResetJobTrackerFor(pawn);
                    __result = null;
                    using (new FloorRestore(pawn, oldMap, oldPosition))
                    foreach (var otherMap in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrder(pawn, oldMap, oldPosition)).Where(candidate => ZUtils.ZTracker.floorPolicy.NeedAllowsMap(pawn, candidate, NeedKind.Meditation)))
                    {
                        Job job = VanillaMeditationJob(pawn);
                        if (job != null)
                        {
                            ZLogger.Message(pawn + " got meditation job " + job + " - map: " + ZTracker.GetMapInfo(otherMap));
                            ZTracker.BuildJobListFor(pawn, otherMap, job);
                            ZUtils.TeleportThing(pawn, oldMap, oldPosition);
                            __result = jobTracker.activeJobs[0];
                            jobTracker.activeJobs.RemoveAt(0);
                            break;
                        }
                    }
                    if (select) Find.Selector.Select(pawn);
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Error("Some kind of error occurred in Z-Levels JobManager (meditation search): " + ex);
                }
                return true;
            }

            // The steps of the vanilla TryGiveJob, for the colonist on the floor being searched.
            private static Job VanillaMeditationJob(Pawn pawn)
            {
                if (!ModsConfig.RoyaltyActive || !MeditationUtility.CanMeditateNow(pawn))
                {
                    return null;
                }
                return MeditationUtility.GetMeditationJob(pawn);
            }
        }

        [HarmonyPatch(typeof(WorkGiver_InteractAnimal), "TakeFoodForAnimalInteractJob")]
        public class TakeFoodForAnimalInteractJob_Patch
        {
            public static bool recursiveTrap;
            private static void Postfix(WorkGiver_InteractAnimal __instance, ref Job __result, Pawn pawn, Pawn tamee)
            {
                if (!ZUtils.HasExtraFloors(pawn)) return;
                if (!recursiveTrap && __result is null)
                {
                    recursiveTrap = true;
                    Map oldMap = pawn.Map;
                    IntVec3 oldPosition = pawn.Position;
                    try
                    {
                        using (new FloorRestore(pawn, oldMap, oldPosition))
                        foreach (var map in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrder(pawn, oldMap, oldPosition)))
                        {
                            if (map != oldMap)
                            {
                                var newPosition = ZUtils.GetCellToTeleportFrom(pawn.Map, pawn.Position, map);
                                ZUtils.TeleportThing(pawn, map, newPosition);
                                var job = __instance.TakeFoodForAnimalInteractJob(pawn, tamee);
                                if (job != null)
                                {
                                    __result = job;
                                }
                            }
                        }
                    }
                    finally
                    {
                        recursiveTrap = false;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "FindNearbyNeeders")]
        public class FindNearbyNeedersPatch
        {
            // The finalizer puts the colonist back even when the original method throws (idea "always move the colonist back").
            private static void Prefix(Pawn pawn, ThingDef stuff, IConstructible c, int resNeeded, int resTotalAvailable,
                bool canRemoveExistingFloorUnderNearbyNeeders, ref int neededTotal, ref Job jobToMakeNeederAvailable, out FloorRestore __state)
            {
                __state = null;
                if (c is Thing thing && thing.Map != pawn.Map)
                {
                    __state = new FloorRestore(pawn, pawn.Map, pawn.Position);
                    ZUtils.TeleportThing(pawn, thing.Map, thing.Position);
                }
            }
            private static void Finalizer(FloorRestore __state)
            {
                __state?.Dispose();
            }
        }

        [HarmonyPatch(typeof(GenConstruct), "CanConstruct", new Type[] { typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool), typeof(JobDef)})]
        public static class CanConstructPatch
        {
            // The finalizer puts the colonist back even when the original method throws (idea "always move the colonist back").
            private static void Prefix(Thing t, Pawn p, out FloorRestore __state, bool checkSkills = true, bool forced = false)
            {
                __state = null;
                if (t.Map != p.Map)
                {
                    __state = new FloorRestore(p, p.Map, p.Position);
                    ZUtils.TeleportThing(p, t.Map, t.Position);
                }
            }
            private static void Finalizer(FloorRestore __state)
            {
                __state?.Dispose();
            }
        }
        [HarmonyPatch(typeof(WorkGiver_Haul), "PotentialWorkThingsGlobal")]
        public static class PotentialWorkThingsGlobalPatch
        {
            private static void Postfix(ref IEnumerable<Thing> __result, Pawn pawn)
            {
                if (!ZUtils.HasExtraFloors(pawn)) return;
                var list = __result.ToList();
                // The other floors offer only the items that the background scan marked for this colonist's floor (ItemFloorMarks).
                int floor = ZUtils.ZTracker.GetZIndexFor(pawn.Map);
                foreach (var map in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.ZTracker.GetAllMaps(pawn.Tile)))
                {
                    if (map != pawn.Map)
                    {
                        list.AddRange(map.listerHaulables.ThingsPotentiallyNeedingHauling().Where(item => ItemFloorMarks.IsMarkedFor(item, floor)));
                    }
                }
                __result = list;
            }
        }

        [HarmonyPatch(typeof(WorkGiver_DoBill), "ShouldSkip")]
        public static class ShouldSkipPatch
        {
            private static void Postfix(ref bool __result, WorkGiver_DoBill __instance, Pawn pawn, bool forced = false)
            {
                if (!ZUtils.HasExtraFloors(pawn)) return;
                if (__result)
                {
                    foreach (var map in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.ZTracker.GetAllMaps(pawn.Tile)))
                    {
                        if (map != pawn.Map)
                        {
                            List<Thing> list = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
                            for (int i = 0; i < list.Count; i++)
                            {
                                IBillGiver billGiver;
                                if ((billGiver = (list[i] as IBillGiver)) != null && __instance.ThingIsUsableBillGiver(list[i]) && billGiver.BillStack.AnyShouldDoNow)
                                {
                                    __result = false;
                                    return;
                                }
                            }
                        }
                    }
                }

            }
        }
        [HarmonyPatch(typeof(StoreUtility), "IsInValidBestStorage")]
        public static class IsInValidBestStoragePatch
        {
            public static bool Prefix(Thing t, ref bool __result)
            {
                if (t.Map != null && !ZUtils.HasExtraFloors(t.Map)) return true;
                IHaulDestination haulDestination = StoreUtility.CurrentHaulDestinationOf(t);
                if (haulDestination == null || !haulDestination.Accepts(t))
                {
                    __result = false;
                    return false;
                }
                FloorPolicy.CrossFloorSearches++;
                var ZTracker = ZUtils.ZTracker;
                foreach (var map in ZTracker.GetAllMapsInClosestOrder(t.Map))
                {
                    if (StoreUtility.TryFindBestBetterStorageFor(t, null, map,
                        haulDestination.GetStoreSettings().Priority,
                        Faction.OfPlayer, out IntVec3 _, out IHaulDestination _,
                        needAccurateResult: false))
                    {
                        __result = false;
                        return false;
                    }
                }
                __result = true;
                return false;
            }
        }


        [HarmonyPatch(typeof(Pawn_CarryTracker), "TryDropCarriedThing")]
        [HarmonyPatch(new Type[]
        {
            typeof(IntVec3),
            typeof(ThingPlaceMode),
            typeof(Thing),
            typeof(Action<Thing, int>)
        }, new ArgumentType[]
        {
             ArgumentType.Normal,
             ArgumentType.Normal,
             ArgumentType.Out,
             ArgumentType.Normal
        })]
        public static class TryDropCarriedThingPatch
        {
            public static bool blockTryDrop = false;
            private static bool Prefix(Pawn_CarryTracker __instance, IntVec3 dropLoc, ThingPlaceMode mode, Thing resultingThing, Action<Thing, int> placedAction = null)
            {
                if (__instance.pawn.RaceProps.Humanlike)
                {
                    var ZTracker = ZUtils.ZTracker;
                    if (ZTracker.jobTracker != null && ZTracker.jobTracker.TryGetValue(__instance.pawn, out var jobTracker) && jobTracker.activeJobs?.Count > 0 && blockTryDrop)
                    {
                        return false;
                    }
                }
                return true;
            }
        }


        [HarmonyPatch(typeof(Pawn_JobTracker), "DetermineNextJob")]
        public class DetermineNextJobPatch
        {
            private static void Postfix(Pawn_JobTracker __instance, Pawn ___pawn, ThinkResult __result, ref ThinkTreeDef thinkTree)
            {
                if (___pawn.RaceProps.Humanlike)
                {
                    try
                    {
                        ZLogger.Message(___pawn + " got next job " + __result.Job);
                    }
                    catch
                    {

                    }
                }
            }
        }

        [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
        public class StartJobPatch
        {
            private static void Postfix(Pawn_JobTracker __instance, Pawn ___pawn, Job newJob, JobTag? tag)
            {
                if (___pawn.RaceProps.Humanlike)
                {
                    try
                    {
                        var jobString = ___pawn.CurJob.ToString();
                        if (jobString.Length > 0)
                        {
                            ZLogger.Message(___pawn + " starts job " + ___pawn.CurJob);
                        }
                        else
                        {
                            ZLogger.Message(___pawn + " starts job " + ___pawn.CurJob?.def);
                        }
                    }
                    catch
                    {
                        ZLogger.Message(___pawn + " starts job " + ___pawn.CurJob?.def);
                    }
                }
            }
        }


        [HarmonyPatch(typeof(Pawn_JobTracker), "EndCurrentJob")]
        public class EndCurrentJobPatch
        {
            private static void Prefix(Pawn_JobTracker __instance, Pawn ___pawn, JobCondition condition, ref bool startNewJob, bool canReturnToPool = true)
            {
                if (___pawn.RaceProps.Humanlike)
                {
                    var ZTracker = ZUtils.ZTracker;
                    // The ended job's claims end with it (idea "someone is already going"). Targets that a queued job still needs stay claimed.
                    if (__instance.curJob != null) ZTracker.ReleaseClaims(___pawn, __instance.curJob);
                    if (ZTracker.jobTracker != null && ZTracker.jobTracker.ContainsKey(___pawn) && !ZTracker.jobTracker[___pawn].startingQueuedJob && ZTracker.jobTracker[___pawn].activeJobs?.Count > 0)
                    {
                        TryDropCarriedThingPatch.blockTryDrop = true;
                        startNewJob = false;
                    }
                    try
                    {
                        ZLogger.Message(___pawn + " ends " + __instance.curJob + " - " + startNewJob + " - condition " + condition);
                    }
                    catch
                    {

                    }

                }
            }

            private static void Postfix(Pawn_JobTracker __instance, Pawn ___pawn, JobCondition condition, ref bool startNewJob, bool canReturnToPool = true)
            {

                if (___pawn.RaceProps.Humanlike)
                {
                    var ZTracker = ZUtils.ZTracker;
                    if (___pawn.CurJob == null && ZTracker.jobTracker != null && ZTracker.jobTracker.ContainsKey(___pawn) && !ZTracker.jobTracker[___pawn].startingQueuedJob
                        && ZTracker.jobTracker[___pawn].activeJobs?.Count > 0)
                    {
                        TryDropCarriedThingPatch.blockTryDrop = false;
                        ZTracker.TryTakeFirstJob(___pawn);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(JobGiver_Work), "TryIssueJobPackage")]
        public class TryIssueJobPackagePatch
        {
            private static bool Prefix(JobGiver_Work __instance, bool ___emergency, ref ThinkResult __result, Pawn pawn, JobIssueParams jobParams)
            {
                // The own-floor search calls the vanilla search, and that call comes back here. It runs unchanged.
                if (OwnFloorFirstSearch.VanillaCallRunning)
                {
                    return true;
                }
                // A pocket map is not a level, so the cross-level search below does not reach it. The vanilla search runs on the pocket instead.
                if (pawn.Map != null && pawn.Map.IsPocketMap)
                {
                    return true;
                }
                if (!ZUtils.HasExtraFloors(pawn)) return true;
                if (pawn.RaceProps.Humanlike)
                {
                    var ZTracker = ZUtils.ZTracker;
                    try
                    {
                        if (!ZTracker.jobTracker.TryGetValue(pawn, out JobTracker jobTracker))
                        {
                            jobTracker = new JobTracker();
                            ZTracker.jobTracker[pawn] = jobTracker;
                        }
                        if (jobTracker.activeJobs?.Any() ?? false)
                        {
                            if (!pawn.jobs.jobQueue.Contains(jobTracker.activeJobs[0]))
                            {
                                if (jobTracker.activeJobs[0].def.defName != "UnloadYourHauledInventory" && jobTracker.activeJobs[0].TryMakePreToilReservations(pawn, false))
                                {
                                    __result = new ThinkResult(jobTracker.activeJobs[0], jobTracker.activeJobs[0].jobGiver);
                                    jobTracker.activeJobs.RemoveAt(0);
                                    return false;
                                }
                            }
                            else if (pawn.jobs.curJob == null)
                            {

                            }
                            else if (pawn.jobs.curJob != jobTracker.activeJobs[0])
                            {
                                ZLogger.Message("4 RESETTING JOB TRACKER FOR " + pawn);
                                ZTracker.ResetJobTrackerFor(pawn);
                            }
                        }
                        else
                        {
                            ZLogger.Message(pawn + " has no active jobs");
                        }
                        // Idle pause (DM6): after a normal search that found nothing, the next normal search waits 600 ticks. The emergency search
                        // is not paused, and the vanilla search on the colonist's own map still runs, so local work is not delayed.
                        if (!___emergency && !ZTracker.floorPolicy.IdleSearchAllowed(pawn, Find.TickManager.TicksGame))
                        {
                            return true;
                        }
                        ThinkResult result;
                        var oldMap = pawn.Map;
                        var oldPosition = pawn.Position;
                        bool select = false;
                        if (Find.Selector.SelectedObjects.Contains(pawn)) select = true;
                        ZTracker.ResetJobTrackerFor(pawn);

                        using (new FloorRestore(pawn, oldMap, oldPosition))
                        try
                        {
                            if (!___emergency) ZTracker.floorPolicy.CountSearch(pawn);
                            jobTracker.searchingJobsNow = true;
                            jobTracker.oldMap = pawn.Map;
                            jobTracker.pawn = pawn;
                            result = OwnFloorFirstSearch.Search(__instance, ___emergency, pawn, jobParams, jobTracker, oldMap, oldPosition);
                            jobTracker.searchingJobsNow = false;
                            if (result.Job != null)
                            {
                                ZUtils.TeleportThing(pawn, oldMap, oldPosition);
                                ZLogger.Message(pawn + " got job " + result + " - map: "
                                    + ZTracker.GetMapInfo(pawn.Map) + " - " + pawn.Position, debugLevel: DebugLevel.Jobs);
                                if (jobTracker.mapDest != null)
                                {
                                    ZTracker.BuildJobListFor(pawn, jobTracker.mapDest, result.Job);
                                }
                                else
                                {
                                    ZTracker.BuildJobListFor(pawn, oldMap, result.Job);
                                }
                                ZLogger.Message($"Assigned local data: jobTracker.lookedAtMap: {ZTracker.GetMapInfo(jobTracker.mapDest)}, jobTracker.lookedAtLocalCell: {jobTracker.lookedAtLocalCellMap}");
                                __result = new ThinkResult(jobTracker.activeJobs[0], jobTracker.activeJobs[0].jobGiver);
                                jobTracker.activeJobs.RemoveAt(0);
                            }
                            else
                            {
                                __result = ThinkResult.NoJob;
                                ZTracker.ResetJobTrackerFor(pawn);
                                if (!___emergency) ZTracker.floorPolicy.NoteIdleSearch(pawn, Find.TickManager.TicksGame);
                                ZLogger.Message(pawn + " failed to find job", debugLevel: DebugLevel.Jobs);
                            }
                        }
                        catch (Exception ex)
                        {
                            jobTracker.searchingJobsNow = false;
                            ZLogger.Error("Exception in TryIssueJobPackagePatch: " + ex);
                        }
                        if (select) Find.Selector.Select(pawn);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Some kind of error occurred in Z-Levels JobManager: " + ex);
                    }
                }
                return true;
            }
            // A table linked to a floor tries that floor first (idea "workbench floor link"). The other floors keep their order, and a table with no link
            // keeps the whole order. The floors are read into a list first, so the order does not change while the search moves the table between floors.
            private static IEnumerable<Map> LinkedFloorFirst(IEnumerable<Map> maps, Thing bench)
            {
                List<Map> list = maps.ToList();
                int? linked = ZUtils.ZTracker.floorPolicy.BenchFloor(bench);
                if (linked == null)
                {
                    return list;
                }
                int index = list.FindIndex(map => ZUtils.ZTracker.GetZIndexFor(map) == linked.Value);
                if (index > 0)
                {
                    Map first = list[index];
                    list.RemoveAt(index);
                    list.Insert(0, first);
                }
                return list;
            }

            private static Job StartOrResumeBillJob(WorkGiver_DoBill scanner, Pawn pawn, IBillGiver giver, JobTracker jobTracker)
            {
                List<ThingCount> chosenIngThings = scanner.chosenIngThings;
                for (int i = 0; i < giver.BillStack.Count; i++)
                {
                    Bill bill = giver.BillStack[i];
                    if ((bill.recipe.requiredGiverWorkType != null && bill.recipe.requiredGiverWorkType
                        != scanner.def.workType) || (Find.TickManager.TicksGame <= bill.nextTickToSearchForIngredients
                        && FloatMenuMakerMap.makingFor != pawn))
                    {
                        continue;
                    }
                    bill.nextTickToSearchForIngredients = 0;
                    if (!bill.ShouldDoNow() || !bill.PawnAllowedToStartAnew(pawn))
                    {
                        continue;
                    }
                    SkillRequirement skillRequirement = bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn);
                    if (skillRequirement != null)
                    {
                        JobFailReason.Is("UnderRequiredSkill".Translate(skillRequirement.minLevel), bill.Label);
                        continue;
                    }
                    Bill_ProductionWithUft bill_ProductionWithUft = bill as Bill_ProductionWithUft;

                    if (bill_ProductionWithUft != null)
                    {
                        if (bill_ProductionWithUft.BoundUft != null)
                        {
                            if (bill_ProductionWithUft.BoundWorker == pawn && pawn.CanReserveAndReach(bill_ProductionWithUft.BoundUft, PathEndMode.Touch, Danger.Deadly) && !bill_ProductionWithUft.BoundUft.IsForbidden(pawn))
                            {
                                return WorkGiver_DoBill.FinishUftJob(pawn, bill_ProductionWithUft.BoundUft, bill_ProductionWithUft);
                            }
                            continue;
                        }
                        UnfinishedThing unfinishedThing = WorkGiver_DoBill.ClosestUnfinishedThingForBill(pawn, bill_ProductionWithUft);

                        if (unfinishedThing != null)
                        {
                            return WorkGiver_DoBill.FinishUftJob(pawn, unfinishedThing, bill_ProductionWithUft);
                        }
                    }

                    bool flag = false;
                    var ZTracker = ZUtils.ZTracker;
                    var workBench = ((Thing)giver);
                    var origBillGiverMap = giver.Map;
                    var origPawnMap = pawn.Map;
                    var origBillGiverPosition = workBench.Position;
                    var origPawnPosition = pawn.Position;
                    ZLogger.Message(giver + " - billGiver.Map: " + ZTracker.GetMapInfo(giver.Map));
                    ZLogger.Message(giver + " - billGiver.Position: " + workBench.Position);
                    using (new FloorRestore(workBench, origBillGiverMap, origBillGiverPosition, pawn, origPawnMap, origPawnPosition))
                    foreach (var map in LinkedFloorFirst(ZTracker.floorPolicy.Allowed(pawn, ZTracker.GetAllMapsInClosestOrder(origBillGiverMap)), workBench))
                    {
                        try
                        {
                            if (origBillGiverMap != map)
                            {
                                workBench.positionInt = ZUtils.GetCellToTeleportFrom(workBench.Map, workBench.Position, map);
                                pawn.positionInt = workBench.positionInt;
                            }
                            else if (workBench.Position != origBillGiverPosition)
                            {
                                workBench.positionInt = origBillGiverPosition;
                                pawn.positionInt = origPawnPosition;
                            }
                            workBench.mapIndexOrState = (sbyte)Find.Maps.IndexOf(map);
                            pawn.mapIndexOrState = (sbyte)Find.Maps.IndexOf(map);
                            flag = WorkGiver_DoBill.TryFindBestBillIngredients(bill, pawn, (Thing)giver, chosenIngThings, null);
                            ZLogger.Message("Found ingredients: " + flag + " in " + ZTracker.GetMapInfo(map) + " for " + bill);
                            if (flag) break;
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Z-Levels failed to process HasJobOnThing of DoBill workgiver. Report about it to devs and provide Hugslib log. Error: " + ex);
                        }
                    }

                    ZLogger.Message("Final position " + workBench.Position);

                    if (!flag)
                    {
                        if (FloatMenuMakerMap.makingFor != pawn)
                        {
                            bill.nextTickToSearchForIngredients = Find.TickManager.TicksGame + WorkGiver_DoBill.ReCheckFailedBillTicksRange.RandomInRange;
                        }
                        else
                        {
                            JobFailReason.Is("MissingMaterials".Translate(), bill.Label);
                        }
                        chosenIngThings.Clear();
                        continue;
                    }
                    Job haulOffJob;
                    Job result = WorkGiver_DoBill.TryStartNewDoBillJob(pawn, bill, giver, chosenIngThings, out haulOffJob);
                    chosenIngThings.Clear();
                    jobTracker.mapDest = giver.Map;
                    return result;
                }
                chosenIngThings.Clear();
                return null;
            }
            public static Job DoBillJobOnThing(WorkGiver_DoBill scanner, Pawn pawn, Thing thing, JobTracker jobTracker, bool forced = false)
            {
                IBillGiver billGiver = thing as IBillGiver;
                if (billGiver == null || !scanner.ThingIsUsableBillGiver(thing)
                    || !billGiver.BillStack.AnyShouldDoNow || !billGiver.UsableForBillsAfterFueling()
                    || !pawn.CanReserve(thing, 1, -1, null, forced) || thing.IsBurning()
                    || thing.IsForbidden(pawn))
                {
                    return null;
                }
                CompRefuelable compRefuelable = thing.TryGetComp<CompRefuelable>();

                if (compRefuelable == null || compRefuelable.HasFuel)
                {
                    billGiver.BillStack.RemoveIncompletableBills();
                    Job job = StartOrResumeBillJob(scanner, pawn, billGiver, jobTracker);
                    return job;
                }
                if (!RefuelWorkGiverUtility.CanRefuel(pawn, thing, forced))
                {
                    return null;
                }
                return RefuelWorkGiverUtility.RefuelJob(pawn, thing, forced, null, null);
            }

            // Construction job
            public static Job ConstructDeliverJobOnThing(WorkGiver_ConstructDeliverResourcesToBlueprints scanner, Pawn pawn, Thing t, bool forced = false)
            {
                if (t.Faction != pawn.Faction)
                {
                    return null;
                }
                Blueprint blueprint = t as Blueprint;
                if (blueprint == null)
                {
                    return null;
                }
                if (GenConstruct.FirstBlockingThing(blueprint, pawn) != null)
                {
                    return GenConstruct.HandleBlockingThingJob(blueprint, pawn, forced);
                }
                bool flag = scanner.def.workType == WorkTypeDefOf.Construction;
                if (!GenConstruct.CanConstruct(blueprint, pawn, flag, forced))
                {
                    return null;
                }
                if (!flag && blueprint.def.entityDefToBuild is TerrainDef
                    && pawn.Map.terrainGrid.CanRemoveTopLayerAt(blueprint.Position))
                {
                    return null;
                }
                Job job = scanner.RemoveExistingFloorJob(pawn, blueprint);
                if (job != null)
                {
                    return job;
                }
                var oldMap = pawn.Map;
                var oldPosition = pawn.Position;
                using (new FloorRestore(pawn, oldMap, oldPosition))
                foreach (var map in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrder(pawn, oldMap, oldPosition)))
                {
                    Job job2 = scanner.ResourceDeliverJobFor(pawn, blueprint, true);
                    if (job2 != null)
                    {
                        return job2;
                    }
                }
                if (scanner.def.workType != WorkTypeDefOf.Hauling)
                {
                    Job job3 = scanner.NoCostFrameMakeJobFor(blueprint);
                    if (job3 != null)
                    {
                        return job3;
                    }
                }
                return null;
            }
            public static Job ConstructDeliverJobOnThing(WorkGiver_ConstructDeliverResourcesToFrames scanner, Pawn pawn, Thing t, bool forced = false)
            {
                if (t.Faction != pawn.Faction)
                {
                    return null;
                }
                Frame frame = t as Frame;
                if (frame == null)
                {
                    return null;
                }

                if (GenConstruct.FirstBlockingThing(frame, pawn) != null)
                {
                    return GenConstruct.HandleBlockingThingJob(frame, pawn, forced);
                }
                bool checkSkills = scanner.def.workType == WorkTypeDefOf.Construction;
                if (!GenConstruct.CanConstruct(frame, pawn, checkSkills, forced))
                {
                    return null;
                }

                Job job2 = scanner.ResourceDeliverJobFor(pawn, frame);
                if (job2 == null)
                {
                    var oldMap = pawn.Map;
                    var oldPosition = pawn.Position;
                    using (new FloorRestore(pawn, oldMap, oldPosition))
                    foreach (var otherMap in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrder(pawn, oldMap, oldPosition)))
                    {
                        if (otherMap != oldMap)
                        {
                            job2 = scanner.ResourceDeliverJobFor(pawn, frame);
                            if (job2 != null)
                            {
                                break;
                            }
                        }
                    }
                }
                return job2;
            }

            // Haul job
            public static bool TryFindBestBetterStoreCellFor(Thing t, Pawn carrier, Map mapToSearch, StoragePriority currentPriority, Faction faction, out IntVec3 foundCell, JobTracker jobTracker, bool needAccurateResult = true)
            {
                bool result = true;
                List<SlotGroup> allGroupsListInPriorityOrder = new List<SlotGroup>();
                // The floor iterator moves the carrier to the stairs of each floor it yields. This loop only collects the storage groups, so the
                // carrier goes back to its own cell when the loop ends. Without the guard, a storage check left the carrier on another floor in
                // the middle of the search that asked for the storage.
                using (new FloorRestore(carrier, carrier.Map, carrier.Position))
                foreach (var map in ZUtils.ZTracker.floorPolicy.Allowed(carrier, ZUtils.GetAllMapsInClosestOrder(carrier, carrier.Map, carrier.Position)))
                {
                    allGroupsListInPriorityOrder.AddRange(map.haulDestinationManager.AllGroupsListInPriorityOrder);
                }

                if (allGroupsListInPriorityOrder.Count == 0)
                {
                    foundCell = IntVec3.Invalid;
                    result = false;
                }
                StoragePriority storagePriority = currentPriority;
                float num = 2.14748365E+09f;
                IntVec3 invalid = IntVec3.Invalid;
                int count = allGroupsListInPriorityOrder.Count;

                foreach (var slotGroup in allGroupsListInPriorityOrder.OrderByDescending(x => x.Settings.Priority))
                {
                    //ZLogger.Message("Checking on : " + t + " - " + ZTracker.GetMapInfo(t.Map) + " for " + carrier + " - " + carrier?.Map + " - " + slotGroup.parent
                    //    + " - priority: " + slotGroup.Settings.Priority + " - " + slotGroup.parent.Map);
                    StoragePriority priority = slotGroup.Settings.Priority;
                    if (priority < storagePriority || priority <= currentPriority)
                    {
                        //ZLogger.Message("Break on - " + slotGroup.parent + " - priority: " + slotGroup.Settings.Priority + " - " + slotGroup.parent.Map);
                        break;
                    }
                    TryFindBestBetterStoreCellForWorker(t, carrier, mapToSearch, faction, slotGroup, needAccurateResult, ref invalid, ref num, ref storagePriority, jobTracker);
                }
                if (!invalid.IsValid)
                {
                    foundCell = IntVec3.Invalid;
                    result = false;
                }
                foundCell = invalid;
                return result;
            }

            private static void TryFindBestBetterStoreCellForWorker(Thing t, Pawn carrier, Map map, Faction faction, SlotGroup slotGroup, bool needAccurateResult, ref IntVec3 closestSlot, ref float closestDistSquared, ref StoragePriority foundPriority, JobTracker jobTracker)
            {
                if (slotGroup == null)
                {
                    return;
                }
                if (!slotGroup.parent.Accepts(t))
                {
                    return;
                }

                if (slotGroup.HeldThings.Contains(t))
                {
                    //ZLogger.Message(t + " already in stockpile " + slotGroup.parent);
                    return;
                }
                //ZLogger.Message(slotGroup.parent + " - priority: " + slotGroup.Settings.Priority
                //    + " - " + slotGroup.parent.Map + " accepts " + t + " - " + t.Map);

                IntVec3 a = t.SpawnedOrAnyParentSpawned ? t.PositionHeld : carrier.PositionHeld;
                List<IntVec3> cellsList = slotGroup.CellsList;
                int count = cellsList.Count;
                int num;
                if (needAccurateResult)
                {
                    num = Mathf.FloorToInt((float)count * Rand.Range(0.005f, 0.018f));
                }
                else
                {
                    num = 0;
                }
                for (int i = 0; i < count; i++)
                {
                    IntVec3 intVec = cellsList[i];
                    //ZLogger.Message("Checking " + intVec + " in " + slotGroup + " in " + slotGroup.parent.Map);
                    float num2 = (float)(a - intVec).LengthHorizontalSquared;
                    if (num2 <= closestDistSquared && IsGoodStoreCell(intVec, slotGroup.parent.Map, t, carrier, faction))
                    {
                        closestSlot = intVec;
                        jobTracker.mapDest = slotGroup.parent.Map;
                        closestDistSquared = num2;
                        foundPriority = slotGroup.Settings.Priority;
                        //var ZTracker = Current.Game.GetComponent<ZLevelsManager>();
                        //ZLogger.Message("RESULT: " + carrier + " - " + slotGroup.parent + " - priority: "
                        //    + slotGroup.Settings.Priority + " - " + ZTracker.GetMapInfo(slotGroup.parent.Map)
                        //    + " accepts " + t + " in " + ZTracker.GetMapInfo(dest));
                        if (i >= num)
                        {
                            break;
                        }
                    }
                }
            }

            // The cell is checked as a cell of map. The reservation and reachability checks below build a LocalTargetInfo from the cell, and the
            // Z-Levels patches route each check to the map recorded when that LocalTargetInfo was built. The floor iterator leaves the last floor it
            // yielded as the recorded map, so without this the cell is checked on the wrong floor. The map is set for the check and put back after it.
            public static bool IsGoodStoreCell(IntVec3 c, Map map, Thing t, Pawn carrier, Faction faction)
            {
                Map previousLookedIntoMap = ZUtils.currentLookedIntoMap;
                ZUtils.currentLookedIntoMap = map;
                try
                {
                    return IsGoodStoreCellOnMap(c, map, t, carrier, faction);
                }
                finally
                {
                    ZUtils.currentLookedIntoMap = previousLookedIntoMap;
                }
            }

            private static bool IsGoodStoreCellOnMap(IntVec3 c, Map map, Thing t, Pawn carrier, Faction faction)
            {
                if (carrier != null && c.IsForbidden(carrier))
                {
                    return false;
                }
                if (!NoStorageBlockersIn(c, map, t))
                {
                    return false;
                }
                if (carrier != null)
                {
                    if (!carrier.CanReserveNew(c))
                    {
                        return false;
                    }
                }
                else if (faction != null && map.reservationManager.IsReservedByAnyoneOf(c, faction))
                {
                    return false;
                }
                if (c.ContainsStaticFire(map))
                {
                    return false;
                }
                List<Thing> thingList = c.GetThingList(map);
                for (int i = 0; i < thingList.Count; i++)
                {
                    if (thingList[i] is IConstructible && GenConstruct.BlocksConstruction(thingList[i], t))
                    {
                        return false;
                    }
                }

                // A cell on the carrier's own level must be reachable from the carrier. Cells on other levels are not checked here,
                // because the route to them goes through the stairs, which this query does not know.
                if (carrier != null && map == carrier.Map && !carrier.Map.reachability.CanReach(carrier.Position, c, PathEndMode.OnCell, TraverseParms.For(carrier, Danger.Deadly, TraverseMode.ByPawn, false)))
                {
                    return false;
                }

                return true;
            }

            private static bool NoStorageBlockersIn(IntVec3 c, Map map, Thing thing)
            {
                List<Thing> list = map.thingGrid.ThingsListAt(c);
                for (int i = 0; i < list.Count; i++)
                {
                    Thing thing2 = list[i];
                    if (thing2.def.EverStorable(false))
                    {
                        if (!thing2.CanStackWith(thing))
                        {
                            return false;
                        }
                        if (thing2.stackCount >= thing.def.stackLimit)
                        {
                            return false;
                        }
                    }
                    if (thing2.def.entityDefToBuild != null && thing2.def.entityDefToBuild.passability != Traversability.Standable)
                    {
                        return false;
                    }
                    if (thing2.def.surfaceType == SurfaceType.None && thing2.def.passability != Traversability.Standable)
                    {
                        return false;
                    }
                }
                return true;
            }
            public static bool PawnCanAutomaticallyHaulFast(Pawn p, Thing t, bool forced)
            {
                UnfinishedThing unfinishedThing = t as UnfinishedThing;
                Building building;
                if (unfinishedThing != null && unfinishedThing.BoundBill != null
                    && ((building = (unfinishedThing.BoundBill.billStack.billGiver as Building)) == null
                    || (building.Spawned && building.OccupiedRect().ExpandedBy(1).Contains(unfinishedThing.Position))))
                {
                    return false;
                }
                if (!p.CanReserve(t, 1, -1, null, forced))
                {
                    return false;
                }
                if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                {
                    return false;
                }
                if (t.def.IsNutritionGivingIngestible && t.def.ingestible.HumanEdible && !t.IsSociallyProper(p, forPrisoner: false, animalsCare: true))
                {
                    JobFailReason.Is("ReservedForPrisoners".Translate());
                    return false;
                }
                if (t.IsBurning())
                {
                    JobFailReason.Is("BurningLower".Translate());
                    return false;
                }
                return true;
            }

            public static bool HasHaulJobOnThing(WorkGiver_Scanner scanner, Pawn pawn, Thing t, JobTracker jobTracker, bool forced = false)
            {
                Map dest = null;
                if (scanner is WorkGiver_HaulGeneral)
                {
                    if (t is Corpse)
                    {
                        return false;
                    }
                }
                else if (scanner is WorkGiver_HaulCorpses)
                {
                    if (!(t is Corpse))
                    {
                        return false;
                    }
                }
                return HaulJobOnThing(pawn, t, jobTracker, forced) != null;
            }
            public static Job HaulJobOnThing(Pawn pawn, Thing t, JobTracker jobTracker, bool forced = false)
            {
                if (!PawnCanAutomaticallyHaulFast(pawn, t, forced))
                {
                    return null;
                }
                return HaulToStorageJob(pawn, t, jobTracker);
            }

            public static Job HaulToStorageJob(Pawn p, Thing t, JobTracker jobTracker)
            {
                StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(t);
                if (!TryFindBestBetterStorageFor(t, p, p.Map, currentPriority, p.Faction, out IntVec3 foundCell, out IHaulDestination haulDestination, jobTracker))
                {
                    JobFailReason.Is("NoEmptyPlaceLower".Translate());
                    return null;
                }
                if (haulDestination is ISlotGroupParent)
                {
                    return HaulToCellStorageJob(p, t, foundCell, fitInStoreCell: false);
                }
                Thing thing = haulDestination as Thing;
                if (thing != null && thing.TryGetInnerInteractableThingOwner() != null)
                {
                    return HaulToContainerJob(p, t, thing);
                }
                Log.Error("Don't know how to handle HaulToStorageJob for storage " + haulDestination.ToStringSafe() + ". thing=" + t.ToStringSafe());
                return null;
            }
            public static bool TryFindBestBetterStorageFor(Thing t, Pawn carrier, Map map, StoragePriority currentPriority, Faction faction, out IntVec3 foundCell, out IHaulDestination haulDestination, JobTracker jobTracker, bool needAccurateResult = true)
            {
                IntVec3 foundCell2 = IntVec3.Invalid;

                StoragePriority storagePriority = StoragePriority.Unstored;
                if (TryFindBestBetterStoreCellFor(t, carrier, map, currentPriority, faction, out foundCell2, jobTracker, needAccurateResult))
                {
                    storagePriority = foundCell2.GetSlotGroup(jobTracker.mapDest).Settings.Priority;
                }

                if (!TryFindBestBetterNonSlotGroupStorageFor(t, carrier, map, currentPriority, faction, out IHaulDestination haulDestination2, jobTracker))
                {
                    haulDestination2 = null;
                }
                if (storagePriority == StoragePriority.Unstored && haulDestination2 == null)
                {
                    foundCell = IntVec3.Invalid;
                    haulDestination = null;
                    return false;
                }
                if (haulDestination2 != null && (storagePriority == StoragePriority.Unstored || (int)haulDestination2.GetStoreSettings().Priority > (int)storagePriority))
                {
                    foundCell = IntVec3.Invalid;
                    haulDestination = haulDestination2;
                    return true;
                }
                foundCell = foundCell2;
                haulDestination = foundCell2.GetSlotGroup(jobTracker.mapDest).parent;
                return true;
            }

            public static bool TryFindBestBetterNonSlotGroupStorageFor(Thing t, Pawn carrier, Map map, StoragePriority currentPriority, Faction faction, out IHaulDestination haulDestination, JobTracker jobTracker, bool acceptSamePriority = false)
            {
                var ZTracker = ZUtils.ZTracker;
                List<IHaulDestination> allHaulDestinationsListInPriorityOrder = map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder;
                // The saved list holds the destinations of every floor of the tile, in the order this code used to add them. Keeping only the
                // carrier's floors gives the same list, without building it again on each lookup (idea 7 on docs/mod-comparison/ideas.html).
                List<Map> allowedMaps = ZTracker.floorPolicy.Allowed(carrier, ZTracker.GetAllMaps(map.Tile)).ToList();
                List<IHaulDestination> allHaulDestinations = StorageIndex.Destinations(map.Tile).Where(destination => allowedMaps.Contains(destination.Map)).ToList();
                IntVec3 intVec = t.SpawnedOrAnyParentSpawned ? t.PositionHeld : carrier.PositionHeld;
                float num = float.MaxValue;
                StoragePriority storagePriority = StoragePriority.Unstored;
                haulDestination = null;
                foreach (var container in allHaulDestinations.OrderByDescending(x => x.GetStoreSettings().Priority))
                {
                    if (container is ISlotGroupParent)
                    {
                        continue;
                    }
                    StoragePriority priority = container.GetStoreSettings().Priority;
                    if ((int)priority < (int)storagePriority || (acceptSamePriority && (int)priority < (int)currentPriority) || (!acceptSamePriority && (int)priority <= (int)currentPriority))
                    {
                        break;
                    }
                    float num2 = intVec.DistanceToSquared(container.Position);
                    if (num2 > num || !container.Accepts(t))
                    {
                        continue;
                    }
                    //ZLogger.Message(container + " accepts " + t + " - " + (container.Accepts(t)).ToString());
                    Thing thing = container as Thing;
                    if (thing != null && thing.Faction != faction)
                    {
                        continue;
                    }
                    if (thing != null)
                    {
                        if (carrier != null)
                        {
                            if (thing.IsForbidden(carrier))
                            {
                                continue;
                            }
                        }
                        else if (faction != null && thing.IsForbidden(faction))
                        {
                            continue;
                        }
                    }
                    if (thing != null)
                    {
                        if (carrier != null)
                        {
                            if (!carrier.CanReserveNew(thing))
                            {
                                continue;
                            }
                        }
                        else if (faction != null && map.reservationManager.IsReservedByAnyoneOf(thing, faction))
                        {
                            continue;
                        }
                    }
                    if (carrier != null)
                    {
                        if (thing != null)
                        {
                            if (!carrier.Map.reachability.CanReach(intVec, thing, PathEndMode.ClosestTouch, TraverseParms.For(carrier)))
                            {
                                continue;
                            }
                        }
                        //else if (!carrier.Map.reachability.CanReach(intVec, container.Position, PathEndMode.ClosestTouch, TraverseParms.For(carrier)))
                        //{
                        //    continue;
                        //}
                    }
                    num = num2;
                    storagePriority = priority;
                    haulDestination = container;
                    jobTracker.mapDest = map;
                }
                return haulDestination != null;
            }

            public static Job HaulToCellStorageJob(Pawn p, Thing t, IntVec3 storeCell, bool fitInStoreCell)
            {
                Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, t, storeCell);
                SlotGroup slotGroup = p.Map.haulDestinationManager.SlotGroupAt(storeCell);
                if (slotGroup != null)
                {
                    Thing thing = p.Map.thingGrid.ThingAt(storeCell, t.def);
                    if (thing != null)
                    {
                        job.count = t.def.stackLimit;
                        if (fitInStoreCell)
                        {
                            job.count -= thing.stackCount;
                        }
                    }
                    else
                    {
                        job.count = 99999;
                    }
                    int num = 0;
                    float statValue = p.GetStatValue(StatDefOf.CarryingCapacity);
                    List<IntVec3> cellsList = slotGroup.CellsList;
                    for (int i = 0; i < cellsList.Count; i++)
                    {
                        if (IsGoodStoreCell(cellsList[i], p.Map, t, p, p.Faction))
                        {
                            Thing thing2 = p.Map.thingGrid.ThingAt(cellsList[i], t.def);
                            num = ((thing2 == null || thing2 == t) ? (num + t.def.stackLimit) :
                                (num + Mathf.Max(t.def.stackLimit - thing2.stackCount, 0)));
                            if (num >= job.count || (float)num >= statValue)
                            {
                                break;
                            }
                        }
                    }
                    job.count = Mathf.Min(job.count, num);
                }
                else
                {
                    job.count = 99999;
                }
                job.haulOpportunisticDuplicates = true;
                job.haulMode = HaulMode.ToCellStorage;
                return job;
            }

            public static Job HaulToContainerJob(Pawn p, Thing t, Thing container)
            {
                ThingOwner thingOwner = container.TryGetInnerInteractableThingOwner();
                if (thingOwner == null)
                {
                    Log.Error(container.ToStringSafe() + " gave null ThingOwner.");
                    return null;
                }
                Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer, t, container);
                job.count = Mathf.Min(t.stackCount, thingOwner.GetCountCanAccept(t));
                job.haulMode = HaulMode.ToContainer;
                return job;
            }

            public static Job RefuelJobOnThing(WorkGiver_Refuel scanner, Pawn pawn, Thing t, bool forced = false)
            {
                var ZTracker = ZUtils.ZTracker;
                Job job = null;
                var JobStandard = scanner.JobStandard;
                var JobAtomic = scanner.JobAtomic;
                Map oldPawnMap = pawn.Map;
                IntVec3 oldPawnPosition = pawn.Position;
                Map oldThingMap = t.Map;
                IntVec3 oldThingPosition = t.Position;

                using (new FloorRestore(pawn, oldPawnMap, oldPawnPosition, t, oldThingMap, oldThingPosition))
                foreach (var otherMap in ZTracker.floorPolicy.Allowed(pawn, ZTracker.GetAllMapsInClosestOrder(oldThingMap)))
                {
                    if (oldThingMap != otherMap)
                    {
                        t.positionInt = ZUtils.GetCellToTeleportFrom(t.Map, t.Position, otherMap);
                        pawn.positionInt = t.positionInt;
                    }
                    else if (t.Position != oldThingPosition)
                    {
                        t.positionInt = oldThingPosition;
                        pawn.positionInt = oldPawnPosition;
                    }

                    t.mapIndexOrState = (sbyte)Find.Maps.IndexOf(otherMap);
                    pawn.mapIndexOrState = (sbyte)Find.Maps.IndexOf(otherMap);

                    if (scanner.HasJobOnThing(pawn, t, forced))
                    {
                        job = RefuelWorkGiverUtility.RefuelJob(pawn, t, forced, JobStandard, JobAtomic);
                    }
                    if (job != null)
                    {
                        job.count = job.GetTarget(TargetIndex.A).Thing.TryGetComp<CompRefuelable>().GetFuelCountToFullyRefuel();
                    }
                    if (job != null) break;
                }
                return job;
            }

            public static bool HasJobOnThingRescue(WorkGiver_RescueDowned scanner, Pawn pawn, Thing t, bool forced = false)
            {
                Pawn pawn2 = t as Pawn;
                if (pawn2 == null || !pawn2.Downed || pawn2.Faction != pawn.Faction || pawn2.InBed() || !pawn.CanReserve(pawn2, 1, -1, null, forced) || GenAI.EnemyIsNear(pawn2, 40f))
                {
                    return false;
                }

                var oldMap1 = pawn.Map;
                var oldMap2 = pawn2.Map;
                var oldPosition1 = pawn.Position;
                var oldPosition2 = pawn2.Position;
                bool select = false;
                if (Find.Selector.SelectedObjects.Contains(pawn)) select = true;
                Thing thing = null;

                using (new FloorRestore(pawn, oldMap1, oldPosition1, pawn2, oldMap2, oldPosition2))
                foreach (var otherMap in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrderForTwoThings(pawn, oldMap1, oldPosition1,
                    pawn2, oldMap2, oldPosition2)))
                {
                    thing = scanner.FindBed(pawn, pawn2);
                    if (thing != null) break;
                }

                if (select) Find.Selector.Select(pawn);

                Log.Message("HasJobOnThingRescue: " + thing + "pawn2.CanReserve(thing): " + pawn2.CanReserve(thing));
                if (thing != null && pawn2.CanReserve(thing))
                {
                    return true;
                }

                return false;
            }

            public static Job TendJobOnThing(Pawn pawn, Thing t, bool forced = false)
            {
                Pawn pawn2 = t as Pawn;
                var oldMap1 = pawn.Map;
                var oldMap2 = pawn2.Map;
                var oldPosition1 = pawn.Position;
                var oldPosition2 = pawn2.Position;
                bool select = false;
                if (Find.Selector.SelectedObjects.Contains(pawn)) select = true;
                Thing thing = null;

                using (new FloorRestore(pawn, oldMap1, oldPosition1, pawn2, oldMap2, oldPosition2))
                foreach (var otherMap in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrderForTwoThings(pawn, oldMap1, oldPosition1,
                    pawn2, oldMap2, oldPosition2)))
                {
                    thing = HealthAIUtility.FindBestMedicine(pawn, pawn2);
                    if (thing != null) break;
                }

                if (select) Find.Selector.Select(pawn);

                if (thing != null)
                {
                    return JobMaker.MakeJob(JobDefOf.TendPatient, pawn2, thing);
                }
                return JobMaker.MakeJob(JobDefOf.TendPatient, pawn2);
            }
        }
    }
}
