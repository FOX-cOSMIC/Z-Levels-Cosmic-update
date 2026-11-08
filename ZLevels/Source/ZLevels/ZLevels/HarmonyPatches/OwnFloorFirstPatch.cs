using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZLevels
{
    // Own floor first (idea 8 on docs/mod-comparison/ideas.html). The work search of a colonist checks its own floor before the other floors.
    // Work at or above the threshold priority (ZLevelsSettings.WorkGiverHighPriorityThreshold, 2 by default) is checked on every floor before the
    // colonist does lower-priority work on its own floor. Lower-priority work on another floor is checked only when the own floor has none.
    // Other floors are checked nearest first, through the stair-aware iterator the Z-Levels search already uses. The iterator moves the colonist
    // to the stairs of each floor it yields, so each search starts there. Every pass runs the vanilla JobGiver_Work search once per floor, and the
    // pass decides which work givers that search may check.
    internal static class OwnFloorFirstSearch
    {
        // True while this class calls the vanilla search. The Z-Levels prefix on JobGiver_Work.TryIssueJobPackage lets that call through.
        internal static bool VanillaCallRunning;

        // The colonist of the paused search, and its floor, while that search runs. Null otherwise. While they are set, the floor policy and the floor
        // iterators offer that colonist only its own floor, and the helpers answer nothing across floors.
        internal static Pawn OwnFloorOnlyPawn;
        internal static Map OwnFloorOnlyMap;

        internal static bool OwnFloorOnlyRunning => OwnFloorOnlyPawn != null;

        // True when the thing is the colonist of the paused search.
        internal static bool OwnFloorOnlyFor(Thing thing)
        {
            return thing != null && thing == OwnFloorOnlyPawn;
        }

        // While it is set, the vanilla search checks only the work givers it accepts (see PawnCanUseWorkGiverPatch). Null accepts every giver.
        internal static Func<WorkGiver, bool> GiverFilter;

        internal static ThinkResult Search(JobGiver_Work instance, bool emergency, Pawn pawn, JobIssueParams jobParams, JobTracker jobTracker, Map oldMap, IntVec3 oldPosition)
        {
            int threshold = ZLevelsMod.settings.WorkGiverHighPriorityThreshold;
            List<WorkGiver> givers = emergency ? pawn.workSettings.WorkGiversInOrderEmergency : pawn.workSettings.WorkGiversInOrderNormal;
            Func<WorkGiver, bool> high = giver => IsHighPriority(pawn, giver, threshold);
            Func<WorkGiver, bool> lower = giver => !IsHighPriority(pawn, giver, threshold);
            bool ownAllowed = ZUtils.ZTracker.floorPolicy.AllowsMap(pawn, oldMap, Find.TickManager.TicksGame);

            // The guard puts the colonist back on its own floor and cell when the search ends, also when a search throws.
            using (new FloorRestore(pawn, oldMap, oldPosition))
            {
                // Pass 1: high-priority work. The own floor first, then the other floors nearest first.
                if (givers.Any(high))
                {
                    if (ownAllowed)
                    {
                        ThinkResult own = RunVanillaSearch(instance, pawn, jobParams, jobTracker, oldMap, high);
                        if (own.Job != null)
                        {
                            return own;
                        }
                    }
                    // SearchOtherFloors moves the colonist onto each floor it yields, and it can leave the colonist on the last one. The
                    // lower-priority pass below searches the own floor, so the colonist goes back to its own floor first.
                    ThinkResult highElsewhere;
                    using (new FloorRestore(pawn, oldMap, oldPosition))
                    {
                        highElsewhere = SearchOtherFloors(instance, pawn, jobParams, jobTracker, oldMap, oldPosition, high);
                    }
                    if (highElsewhere.Job != null)
                    {
                        return highElsewhere;
                    }
                }

                // Pass 2: the lower-priority work on the own floor.
                if (ownAllowed)
                {
                    ThinkResult ownLower = RunVanillaSearch(instance, pawn, jobParams, jobTracker, oldMap, lower);
                    if (ownLower.Job != null)
                    {
                        return ownLower;
                    }
                }

                // Pass 3: the lower-priority work on the other floors, nearest first.
                return SearchOtherFloors(instance, pawn, jobParams, jobTracker, oldMap, oldPosition, lower);
            }
        }

        // The other floors the colonist may search, nearest first. Allowed keeps the floors that the colonist's floor lock allows. The iterator
        // moves the colonist to the stairs of each floor as it yields it, so the search runs there. The own floor is skipped, because it is searched
        // on its own.
        private static ThinkResult SearchOtherFloors(JobGiver_Work instance, Pawn pawn, JobIssueParams jobParams, JobTracker jobTracker, Map oldMap, IntVec3 oldPosition, Func<WorkGiver, bool> filter)
        {
            foreach (Map map in ZUtils.ZTracker.floorPolicy.Allowed(pawn, ZUtils.GetAllMapsInClosestOrder(pawn, oldMap, oldPosition)))
            {
                if (map == oldMap)
                {
                    continue;
                }
                FloorPolicy.OtherFloorSearchRuns++;
                ThinkResult found = RunVanillaSearch(instance, pawn, jobParams, jobTracker, map, filter);
                if (found.Job != null)
                {
                    return found;
                }
            }
            return ThinkResult.NoJob;
        }

        // The search of a colonist in the idle pause: the own floor only, and only when the floor lock allows it. The floor's work settings
        // are part of the filter in RunVanillaSearch. The guard puts the colonist back on its cell and floor when the search throws.
        internal static ThinkResult SearchOwnFloorOnly(JobGiver_Work instance, Pawn pawn, JobIssueParams jobParams, JobTracker jobTracker)
        {
            Map ownMap = pawn.Map;
            if (!ZUtils.ZTracker.floorPolicy.AllowsMap(pawn, ownMap, Find.TickManager.TicksGame))
            {
                return ThinkResult.NoJob;
            }
            // The helpers answer across floors while a normal search runs. The paused search keeps them to their vanilla answers, so it finds
            // only work on this floor, with the floor's lock and work settings. Before the own-floor search took over the pause, the paused search
            // was the plain vanilla search, which has no helpers, so this keeps that reach.
            Pawn previousPawn = OwnFloorOnlyPawn;
            Map previousMap = OwnFloorOnlyMap;
            OwnFloorOnlyPawn = pawn;
            OwnFloorOnlyMap = ownMap;
            try
            {
                using (new FloorRestore(pawn, ownMap, pawn.Position))
                {
                    return RunVanillaSearch(instance, pawn, jobParams, jobTracker, ownMap, giver => true);
                }
            }
            finally
            {
                OwnFloorOnlyPawn = previousPawn;
                OwnFloorOnlyMap = previousMap;
            }
        }

        // Runs the vanilla search with the colonist on the given map, and lets through only the work givers the filter accepts. While the search
        // runs, the job tracker names the map being searched, as the copied search did: the reservation and storage code reads that name. A haul
        // to storage keeps the name the storage search set, which is the storage's map. Any other job takes the map searched. A search that finds
        // nothing clears the name. The Z-Levels prefix reads the name when it builds the job list.
        private static ThinkResult RunVanillaSearch(JobGiver_Work instance, Pawn pawn, JobIssueParams jobParams, JobTracker jobTracker, Map map, Func<WorkGiver, bool> filter)
        {
            ThinkResult result;
            jobTracker.mapDest = map;
            // The reachability patch files each cell the search looks at under this map, as the iterator does when it yields a map. The own floor
            // is not yielded by the iterator here, so the map is set directly; without it the cells keep the map of the last other floor.
            ZUtils.currentLookedIntoMap = map;
            // The floor's work settings are part of the filter: a floor that does not do a work type refuses its givers, so the vanilla search skips them.
            int floor = ZUtils.ZTracker.GetZIndexFor(map);
            GiverFilter = giver => filter(giver) && ZUtils.ZTracker.floorPolicy.WorkTypeAllowed(giver.def.workType, floor);
            VanillaCallRunning = true;
            try
            {
                result = instance.TryIssueJobPackage(pawn, jobParams);
            }
            finally
            {
                VanillaCallRunning = false;
                GiverFilter = null;
            }
            if (result.Job == null)
            {
                jobTracker.mapDest = null;
            }
            else if (!IsHaulToStorage(result.Job))
            {
                jobTracker.mapDest = map;
            }
            return result;
        }

        private static bool IsHaulToStorage(Job job)
        {
            return job.def == JobDefOf.HaulToCell || job.def == JobDefOf.HaulToContainer;
        }

        // A work type is high priority when the colonist has it enabled at the threshold or more important. RimWorld priority 1 is the most important.
        private static bool IsHighPriority(Pawn pawn, WorkGiver giver, int threshold)
        {
            WorkTypeDef workType = giver.def.workType;
            if (workType == null)
            {
                return false;
            }
            int priority = pawn.workSettings.GetPriority(workType);
            return priority > 0 && priority <= threshold;
        }
    }

    // The vanilla search asks this about each work giver before it looks for a job. A giver that the current pass does not accept is refused here,
    // so the vanilla search skips it. With no pass running, every giver passes, as before.
    [HarmonyPatch(typeof(JobGiver_Work), "PawnCanUseWorkGiver")]
    public static class PawnCanUseWorkGiverPatch
    {
        private static bool Prefix(WorkGiver giver, ref bool __result)
        {
            Func<WorkGiver, bool> filter = OwnFloorFirstSearch.GiverFilter;
            if (filter == null || filter(giver))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }

    // The work givers that the copied search answered with Z-Levels helpers. Their vanilla validator (HasJobOnThing) and vanilla job (JobOnThing)
    // look only at the colonist's current floor, so work on another floor is missed. While the own-floor search runs, the patches below answer with
    // the helpers the copy used for each giver: the validator and the job, as the copy chose them. Outside the search the vanilla methods run.
    internal static class CrossFloorGiverJob
    {
        private static bool helperRunning;

        // Runs the answer and returns true while the own-floor search is active. Returns false when the vanilla method must run. A helper can call
        // back into the giver it serves (the refuel helper asks HasJobOnThing); that nested call gets the vanilla method, so the calls do not loop.
        internal static bool TryAnswer<T>(Func<T> answer, out T result)
        {
            result = default(T);
            if (!OwnFloorFirstSearch.VanillaCallRunning || OwnFloorFirstSearch.OwnFloorOnlyRunning || helperRunning)
            {
                return false;
            }
            helperRunning = true;
            try
            {
                result = answer();
            }
            finally
            {
                helperRunning = false;
            }
            return true;
        }

        // The helpers that need a job tracker take the colonist's tracker. A colonist without one gets no job.
        private static JobTracker TrackerOf(Pawn pawn)
        {
            return ZUtils.ZTracker.jobTracker.TryGetValue(pawn, out JobTracker jobTracker) ? jobTracker : null;
        }

        internal static Job Haul(Pawn pawn, Thing t, bool forced)
        {
            JobTracker jobTracker = TrackerOf(pawn);
            return jobTracker == null ? null : JobPatches.TryIssueJobPackagePatch.HaulJobOnThing(pawn, t, jobTracker, forced);
        }

        // The vanilla gates for corpses: only a corpse is hauled, and not one that a wild animal has reserved.
        internal static Job CorpseHaul(Pawn pawn, Thing t, bool forced)
        {
            if (!(t is Corpse))
            {
                return null;
            }
            Pawn reserver = pawn.Map.physicalInteractionReservationManager.FirstReserverOf(t);
            if (reserver != null && reserver.RaceProps.Animal && reserver.Faction != Faction.OfPlayer)
            {
                return null;
            }
            return Haul(pawn, t, forced);
        }

        internal static Job DoBill(WorkGiver_DoBill scanner, Pawn pawn, Thing thing, bool forced)
        {
            JobTracker jobTracker = TrackerOf(pawn);
            return jobTracker == null ? null : JobPatches.TryIssueJobPackagePatch.DoBillJobOnThing(scanner, pawn, thing, jobTracker, forced);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_HaulGeneral), "JobOnThing")]
    public static class HaulGeneralJobOnThingPatch
    {
        private static bool Prefix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            return !CrossFloorGiverJob.TryAnswer<Job>(() => t is Corpse ? null : CrossFloorGiverJob.Haul(pawn, t, forced), out __result);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_HaulCorpses), "JobOnThing")]
    public static class HaulCorpsesJobOnThingPatch
    {
        private static bool Prefix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            return !CrossFloorGiverJob.TryAnswer<Job>(() => CrossFloorGiverJob.CorpseHaul(pawn, t, forced), out __result);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), "JobOnThing")]
    public static class DoBillJobOnThingPatch
    {
        private static bool Prefix(WorkGiver_DoBill __instance, Pawn pawn, Thing thing, bool forced, ref Job __result)
        {
            return !CrossFloorGiverJob.TryAnswer<Job>(() => CrossFloorGiverJob.DoBill(__instance, pawn, thing, forced), out __result);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), "HasJobOnThing")]
    public static class ConstructDeliverBlueprintsHasJobOnThingPatch
    {
        private static bool Prefix(WorkGiver_ConstructDeliverResourcesToBlueprints __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            return !CrossFloorGiverJob.TryAnswer<bool>(() => JobPatches.TryIssueJobPackagePatch.ConstructDeliverJobOnThing(__instance, pawn, t, forced) != null, out __result);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), "JobOnThing")]
    public static class ConstructDeliverBlueprintsJobOnThingPatch
    {
        private static bool Prefix(WorkGiver_ConstructDeliverResourcesToBlueprints __instance, Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            return !CrossFloorGiverJob.TryAnswer<Job>(() => JobPatches.TryIssueJobPackagePatch.ConstructDeliverJobOnThing(__instance, pawn, t, forced), out __result);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames), "HasJobOnThing")]
    public static class ConstructDeliverFramesHasJobOnThingPatch
    {
        private static bool Prefix(WorkGiver_ConstructDeliverResourcesToFrames __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            return !CrossFloorGiverJob.TryAnswer<bool>(() => JobPatches.TryIssueJobPackagePatch.ConstructDeliverJobOnThing(__instance, pawn, t, forced) != null, out __result);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames), "JobOnThing")]
    public static class ConstructDeliverFramesJobOnThingPatch
    {
        private static bool Prefix(WorkGiver_ConstructDeliverResourcesToFrames __instance, Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            return !CrossFloorGiverJob.TryAnswer<Job>(() => JobPatches.TryIssueJobPackagePatch.ConstructDeliverJobOnThing(__instance, pawn, t, forced), out __result);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Refuel), "HasJobOnThing")]
    public static class RefuelHasJobOnThingPatch
    {
        private static bool Prefix(WorkGiver_Refuel __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            return !CrossFloorGiverJob.TryAnswer<bool>(() => JobPatches.TryIssueJobPackagePatch.RefuelJobOnThing(__instance, pawn, t, forced) != null, out __result);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Refuel), "JobOnThing")]
    public static class RefuelJobOnThingPatch
    {
        private static bool Prefix(WorkGiver_Refuel __instance, Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            return !CrossFloorGiverJob.TryAnswer<Job>(() => JobPatches.TryIssueJobPackagePatch.RefuelJobOnThing(__instance, pawn, t, forced), out __result);
        }
    }

    // The rescue validator only; the rescue job stays vanilla, as the copy had it.
    [HarmonyPatch(typeof(WorkGiver_RescueDowned), "HasJobOnThing")]
    public static class RescueDownedHasJobOnThingPatch
    {
        private static bool Prefix(WorkGiver_RescueDowned __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            return !CrossFloorGiverJob.TryAnswer<bool>(() => JobPatches.TryIssueJobPackagePatch.HasJobOnThingRescue(__instance, pawn, t, forced), out __result);
        }
    }

    // The tend job only; the tend validator stays vanilla, as the copy had it.
    [HarmonyPatch(typeof(WorkGiver_Tend), "JobOnThing")]
    public static class TendJobOnThingPatch
    {
        private static bool Prefix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            return !CrossFloorGiverJob.TryAnswer<Job>(() => JobPatches.TryIssueJobPackagePatch.TendJobOnThing(pawn, t, forced), out __result);
        }
    }
}
