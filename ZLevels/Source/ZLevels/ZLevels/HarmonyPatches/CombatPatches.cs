using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZLevels
{
    // A fault that a combat patch contains. The patch logs one error line per distinct failure, keyed by the patch, the exception type,
    // the message and the IL offset, so a repeated fault does not flood the log. The game keeps its own result.
    internal static class CombatFaults
    {
        private static readonly HashSet<string> loggedFaults = new HashSet<string>();

        public static void Log(string patch, Exception exception)
        {
            int offset = new System.Diagnostics.StackTrace(exception, false).GetFrame(0)?.GetILOffset() ?? -1;
            string key = patch + "|" + exception.GetType().FullName + "|" + exception.Message + "|" + offset;
            if (loggedFaults.Add(key))
            {
                ZLogger.Error("Combat patch " + patch + " faulted and was contained: " + exception.GetType().FullName + ": " + exception.Message + " (IL offset " + offset + ")");
            }
        }
    }

    [HarmonyPatch(typeof(AttackTargetFinder), "BestShootTargetFromCurrentPosition")]
    public static class BestShootTargetFromCurrentPosition_Patch
    {
        private static void Prefix()
        {
            try
            {
                CombatPatches_BestAttackTarget_Patch.multiMapSearch = true;
            }
            catch (Exception exception)
            {
                CombatFaults.Log("BestShootTargetFromCurrentPosition.Prefix", exception);
            }
        }
        private static void Postfix()
        {
            try
            {
                CombatPatches_BestAttackTarget_Patch.multiMapSearch = false;
            }
            catch (Exception exception)
            {
                CombatFaults.Log("BestShootTargetFromCurrentPosition.Postfix", exception);
            }
        }
    }
    
    [HarmonyPatch(typeof(JobGiver_AIFightEnemy), "FindAttackTarget")]
    public class FindAttackTarget_Patch
    {
        private static void Prefix()
        {
            try
            {
                CombatPatches_BestAttackTarget_Patch.multiMapSearch = true;
            }
            catch (Exception exception)
            {
                CombatFaults.Log("FindAttackTarget.Prefix", exception);
            }
        }
        private static void Postfix()
        {
            try
            {
                CombatPatches_BestAttackTarget_Patch.multiMapSearch = false;
            }
            catch (Exception exception)
            {
                CombatFaults.Log("FindAttackTarget.Postfix", exception);
            }
        }
    }
    
    [HarmonyPatch(typeof(AttackTargetFinder), "BestAttackTarget")]
    public static class CombatPatches_BestAttackTarget_Patch
    {
        public static bool recursiveTrap = false;
    
        public static bool multiMapSearch = false;
        public static bool Prefix(ref IAttackTarget __result, IAttackTargetSearcher searcher, TargetScanFlags flags, Predicate<Thing> validator = null, float minDist = 0f, float maxDist = 9999f, IntVec3 locus = default(IntVec3),
                float maxTravelRadiusFromLocus = float.MaxValue, bool canBashDoors = false, bool canTakeTargetsCloserThanEffectiveMinRange = true, bool canBashFences = false, bool onlyRanged = false)
        {
            bool result = true;
            try
            {
                if (!recursiveTrap && multiMapSearch)
                {
                    recursiveTrap = true;
                    Map oldMap = searcher.Thing.Map;
                    IntVec3 oldPosition = searcher.Thing.Position;
                    bool dontCheckForStairs = searcher.Thing is Building;
                    try
                    {
                        foreach (var map in ZUtils.GetAllMapsInClosestOrder(searcher.Thing, oldMap, oldPosition, dontCheckForStairs: dontCheckForStairs))
                        {
                            if (map != oldMap)
                            {
                                if (ZUtils.ZTracker.GetZIndexFor(map) < ZUtils.ZTracker.GetZIndexFor(oldMap))
                                {
                                    CanBeSeenOverFast_Patch.checkLevels = true;
                                    CanBeSeenOverFast_Patch.upperMap = oldMap;
                                    CanBeSeenOverFast_Patch.lowerMap = map;
                                    CanBeSeenOverFast_Patch.caster = searcher.Thing;
                                }
                                var target = AttackTargetFinder.BestAttackTarget(searcher, flags, validator, minDist,
                                        maxDist, locus, maxTravelRadiusFromLocus, canBashDoors, canTakeTargetsCloserThanEffectiveMinRange, canBashFences, onlyRanged);
                                //ZLogger.Message(searcher.Thing + " - 1: " + ZUtils.ZTracker.GetMapInfo(searcher.Thing.Map) + " - result: " + target);
                                if (target != null)
                                {
                                    __result = target;
                                    result = false;
                                    break;
                                }
                            }
                        }
                    }
                    finally
                    {
                        // The loop moves the searcher from map to map. It goes back on every exit, including a fault inside the loop.
                        recursiveTrap = false;
                        CanBeSeenOverFast_Patch.checkLevels = false;
                        CanBeSeenOverFast_Patch.upperMap = null;
                        CanBeSeenOverFast_Patch.lowerMap = null;
                        CanBeSeenOverFast_Patch.caster = null;
                        //ZLogger.Message("1 Trying to get " + searcher.Thing + " back to " + oldMap + " - " + oldPosition);
                        ZUtils.TeleportThing(searcher.Thing, oldMap, oldPosition);
                    }
                }
            }
            catch (Exception exception)
            {
                // The original search runs, so this Prefix returns true. __result is left for the original to write.
                CombatFaults.Log("AttackTargetFinder.BestAttackTarget.Prefix", exception);
                return true;
            }
            return result;
        }
    }
    
    [HarmonyPatch(typeof(GenGrid), "CanBeSeenOverFast")]
    public static class CanBeSeenOverFast_Patch
    {
        public static bool checkLevels = false;
    
        public static Map upperMap = null;
    
        public static Map lowerMap = null;
    
        public static Thing caster = null;
        public static bool Prefix(IntVec3 c, Map map, ref bool __result)
        {
            try
            {
                if (checkLevels)
                {
                    __result = true;
                    return false;
                }
                if (checkLevels && c.GetTerrain(upperMap) == ZLevelsDefOf.ZL_OutsideTerrain)
                {
                    ZLogger.Message(c + " - Return true: upper - " + c.GetFirstBuilding(upperMap) + " - " + c.GetTerrain(upperMap));
                    ZLogger.Message(c + " - Return true: lower - " + c.GetFirstBuilding(lowerMap) + " - " + c.GetTerrain(lowerMap));
                    __result = true;
                    return false;
                }
                else if (checkLevels && c != caster.positionInt)
                {
                    ZLogger.Message(caster + " - " + c + " - Return false: upper - " + c.GetFirstBuilding(upperMap) + " - " + c.GetTerrain(upperMap));
                    ZLogger.Message(caster + " - " + c + " - Return false: lower - " + c.GetFirstBuilding(lowerMap) + " - " + c.GetTerrain(lowerMap));
                }
                else if (checkLevels && c == caster.positionInt)
                {
                    ZLogger.Message(c + " - Return true: upper - " + c.GetFirstBuilding(upperMap) + " - " + c.GetTerrain(upperMap));
                    ZLogger.Message(c + " - Return true: lower - " + c.GetFirstBuilding(lowerMap) + " - " + c.GetTerrain(lowerMap));
                    __result = true;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                // The vanilla check runs, so this Prefix returns true. __result is left for the vanilla check to write.
                CombatFaults.Log("GenGrid.CanBeSeenOverFast.Prefix", exception);
                return true;
            }
        }
    }
    
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class TryCastShot_Patch
    {
        public static Map casterOldMap;
        public static Map targetOldMap;
    
        public static bool teleportBack;
        private static Thing movedThing;
        public static void Prefix(Verb_LaunchProjectile __instance, LocalTargetInfo ___currentTarget, ref bool __result)
        {
            //ZLogger.Message("__instance.caster: " + __instance.caster);
            //ZLogger.Message("__instance.caster.Map: " + __instance.caster.Map);
            //ZLogger.Message("__instance.caster.Map.Tile: " + __instance.caster.Map.Tile);
            //
            //ZLogger.Message("___currentTarget.Thing: " + ___currentTarget.Thing);
            //ZLogger.Message("___currentTarget.Thing?.Map: " + ___currentTarget.Thing?.Map);
            //ZLogger.Message("___currentTarget.Thing?.Map.Tile: " + ___currentTarget.Thing?.Map.Tile);
        
            try
            {
                if (__instance.caster.Map != ___currentTarget.Thing?.Map && __instance.caster.Map.Tile == ___currentTarget.Thing?.Map?.Tile)
                {
                    var ind1 = ZUtils.ZTracker.GetZIndexFor(__instance.caster.Map);
                    var ind2 = ZUtils.ZTracker.GetZIndexFor(___currentTarget.Thing.Map);
                    if (ind1 > ind2)
                    {
                        teleportBack = true;
                        targetOldMap = ___currentTarget.Thing.Map;
                        movedThing = ___currentTarget.Thing;
                        ZUtils.TeleportThing(___currentTarget.Thing, __instance.caster.Map, ___currentTarget.Thing.Position);
                        RoofAndFloorPatches.Patch_SpawnSetup.doTeleport = false;
                    }
                    else if (ind1 < ind2)
                    {
                        teleportBack = true;
                        casterOldMap = __instance.caster.Map;
                        movedThing = __instance.caster;
                        ZUtils.TeleportThing(__instance.caster, ___currentTarget.Thing.Map, __instance.caster.Position);
                        RoofAndFloorPatches.Patch_SpawnSetup.doTeleport = false;
                    }
                }
            }
            catch (Exception exception)
            {
                // A teleport that happened is undone by Postfix, because teleportBack and the old map are already set. The fault is logged.
                CombatFaults.Log("Verb_LaunchProjectile.TryCastShot.Prefix", exception);
            }
        }
        
        public static void Postfix(Verb_LaunchProjectile __instance, LocalTargetInfo ___currentTarget, ref bool __result)
        {
            try
            {
                // Nothing is decided on this path. The undo of the Prefix's teleport is in the finally, so it runs on every exit.
            }
            finally
            {
                if (teleportBack)
                {
                    UndoTeleport();
                }
            }
        }

        // Puts back the thing that the Prefix moved, then clears the pending move. The Postfix calls it on the normal path. The finalizer
        // calls it after a fault in the Prefix or in the original method, because the Postfix does not run then. It does nothing when no move is pending.
        private static void UndoTeleport()
        {
            try
            {
                if (movedThing != null)
                {
                    Map home = targetOldMap ?? casterOldMap;
                    if (home != null)
                    {
                        ZUtils.TeleportThing(movedThing, home, movedThing.Position);
                    }
                    RoofAndFloorPatches.Patch_SpawnSetup.doTeleport = true;
                }
            }
            catch (Exception exception)
            {
                CombatFaults.Log("Verb_LaunchProjectile.TryCastShot.UndoTeleport", exception);
            }
            finally
            {
                movedThing = null;
                targetOldMap = null;
                casterOldMap = null;
            }
        }

        // Runs after a fault in the Prefix or in the original method. Harmony passes __exception by value, so this cannot clear it,
        // and the exception still reaches the caller.
        [HarmonyFinalizer]
        private static void Finalizer(Exception __exception)
        {
            UndoTeleport();
        }
    }
    
    //[HarmonyPatch(typeof(Projectile), "Launch", new Type[]
    //{
    //    typeof(Thing),
    //    typeof(Vector3),
    //    typeof(LocalTargetInfo),
    //    typeof(LocalTargetInfo),
    //    typeof(ProjectileHitFlags),
    //    typeof(Thing),
    //    typeof(ThingDef)
    //})]
    //internal class PatchProjectileLaunch
    //{
    //    private static void Postfix(Projectile __instance, Vector3 ___destination, Thing launcher, ref Vector3 ___origin, LocalTargetInfo intendedTarget, Thing equipment, ref int ___ticksToImpact)
    //    {
    //        if (TryCastShot_Patch.targetOldMap != null && launcher.Map != TryCastShot_Patch.targetOldMap)
    //        {
    //            ZLogger.Message("TryCastShot_Patch.targetOldMap: " + TryCastShot_Patch.targetOldMap);
    //            RoofAndFloorPatches.Patch_SpawnSetup.doTeleport = false;
    //            ZUtils.ZTracker.TeleportThing(__instance, __instance.Position, TryCastShot_Patch.targetOldMap);
    //            RoofAndFloorPatches.Patch_SpawnSetup.doTeleport = true;
    //        }
    //        else if (TryCastShot_Patch.casterOldMap != null && intendedTarget.Thing.Map != TryCastShot_Patch.casterOldMap)
    //        {
    //            ZLogger.Message("intendedTarget.Thing.Map: " + intendedTarget.Thing.Map);
    //            RoofAndFloorPatches.Patch_SpawnSetup.doTeleport = false;
    //            ZUtils.ZTracker.TeleportThing(__instance, __instance.Position, intendedTarget.Thing.Map);
    //            RoofAndFloorPatches.Patch_SpawnSetup.doTeleport = true;
    //        }
    //    }
    //}
    
    [HarmonyPatch(typeof(Building_TurretGun), "TryFindNewTarget")]
    public class TryFindNewTarget_Patch
    {
        public static void Postfix(ref Building_TurretGun __instance, ref LocalTargetInfo __result)
        {
            try
            {
                //ZLogger.Message(__instance + " got target " + __result);
            }
            catch (Exception exception)
            {
                CombatFaults.Log("Building_TurretGun.TryFindNewTarget.Postfix", exception);
            }
        }
    }
    
    
    [HarmonyPatch(typeof(Verb), "TryFindShootLineFromTo")]
    public static class TryFindShootLineFromTo_Patch
    {
        public static Map oldMap1;
        public static Map oldMap2;
        public static bool teleportBack;
        private static Thing movedThing;
        public static int ind1 = 0;
        public static int ind2 = 0;
        public static void Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, ref bool __result)
        {
            try
            {
                if (__instance.caster?.Map != targ.Thing?.Map && __instance.caster?.Map?.Tile == targ.Thing?.Map?.Tile)
                {
                    ind1 = ZUtils.ZTracker.GetZIndexFor(__instance.caster.Map);
                    ind2 = ZUtils.ZTracker.GetZIndexFor(targ.Thing.Map);
                    if (ind1 > ind2)
                    {
                        teleportBack = true;
                        oldMap1 = targ.Thing.Map;
                        movedThing = targ.Thing;
                        ZUtils.TeleportThing(targ.Thing, __instance.caster.Map, targ.Thing.Position);
                    }
                    else if (ind1 < ind2)
                    {
                        teleportBack = true;
                        oldMap2 = __instance.caster.Map;
                        movedThing = __instance.caster;
                        ZUtils.TeleportThing(__instance.caster, targ.Thing.Map, __instance.caster.Position);
                    }
                }
            }
            catch (Exception exception)
            {
                // A teleport that happened is undone by Postfix, because teleportBack and the old map are already set. The fault is logged.
                CombatFaults.Log("Verb.TryFindShootLineFromTo.Prefix", exception);
            }
        }
        public static void Postfix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, ref bool __result)
        {
            if (teleportBack)
            {
                try
                {
                    if (__result)
                    {
                        if (ind1 > ind2 && !IsVoidsEverywhereInShootingLine(resultingLine, __instance.caster.Map, __instance.caster, targ.Thing))
                        {
                            __result = false;
                        }
                        else if (ind1 < ind2 && !IsVoidsEverywhereInShootingLineInBackWard(resultingLine, targ.Thing.Map, __instance.caster, targ.Thing))
                        {
                            __result = false;
                        }
                    }
                }
                catch (Exception exception)
                {
                    // The verdict stays as the game set it. The finally below still puts the teleported thing back.
                    CombatFaults.Log("Verb.TryFindShootLineFromTo.Postfix", exception);
                }
                finally
                {
                    // The Prefix moved the target or the caster to another map. It goes back here on every exit.
                    UndoTeleport();
                }
            }
        }

        // Puts back the thing that the Prefix moved, then clears the state of the call. The Postfix calls it on the normal path. The finalizer
        // calls it after a fault in the Prefix or in the original method, because the Postfix does not run then. It does nothing when no move is pending.
        private static void UndoTeleport()
        {
            try
            {
                if (movedThing != null)
                {
                    Map home = oldMap1 ?? oldMap2;
                    if (home != null)
                    {
                        ZUtils.TeleportThing(movedThing, home, movedThing.Position);
                    }
                }
            }
            catch (Exception exception)
            {
                CombatFaults.Log("Verb.TryFindShootLineFromTo.UndoTeleport", exception);
            }
            finally
            {
                movedThing = null;
                oldMap1 = null;
                oldMap2 = null;
                ind1 = 0;
                ind2 = 0;
                teleportBack = false;
            }
        }

        // Runs after a fault in the Prefix or in the original method. Harmony passes __exception by value, so this cannot clear it,
        // and the exception still reaches the caller.
        [HarmonyFinalizer]
        private static void Finalizer(Exception __exception)
        {
            UndoTeleport();
        }

        public static bool IsVoidsEverywhereInShootingLine(ShootLine resultingLine, Map map, Thing caster, Thing target)
        {
            var points = resultingLine.Points().ToList();
            if (points.Count > 2)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    if (IntVec3Utility.DistanceTo(caster.Position, points[i]) >= 2)
                    {
                        if (points[i].GetTerrain(map) != ZLevelsDefOf.ZL_OutsideTerrain)
                        {
                            ZLogger.Message("1: " + caster + " - " + i + " - " + points[i] + " - " + points[i].GetTerrain(map) + " - " + map);
                            return false;
                        }
                    }
                }
                if (resultingLine.dest.GetCover(map)?.def?.Fillage == FillCategory.Full)
                {
                    ZLogger.Message("3: " + caster + " - " + resultingLine.dest + " - " + resultingLine.dest.GetCover(map) + " - " + map);
                    return false;
                }
            }
            else
            {
                for (int i = 0; i < points.Count; i++)
                {
                    if (points[i] != resultingLine.Source)
                    {
                        if (points[i].GetTerrain(map) != ZLevelsDefOf.ZL_OutsideTerrain)
                        {
                            ZLogger.Message("4: " + caster + " - " + i + " - " + points[i] + " - " + points[i].GetTerrain(map) + " - " + map);
                            return false;
                        }
                    }
                }
                if (resultingLine.dest.GetCover(map)?.def?.Fillage == FillCategory.Full)
                {
                    ZLogger.Message("5: " + caster + " - " + resultingLine.dest + " - " + resultingLine.dest.GetCover(map) + " - " + map);
                    return false;
                }
            }
            return true;
        }
    
        public static bool IsVoidsEverywhereInShootingLineInBackWard(ShootLine resultingLine, Map map, Thing caster, Thing target)
        {
            var points = resultingLine.Points().ToList();
            if (points.Count > 2)
            {
                for (int i = points.Count - 1; i >= 0; i--)
                {
                    if (IntVec3Utility.DistanceTo(target.Position, points[i]) >= 2)
                    {
                        if (i < points.Count - 1 && points[i].GetTerrain(map) != ZLevelsDefOf.ZL_OutsideTerrain)
                        {
                            //ZLogger.Message("6: " + caster + " - " + " - " + i + " - " + points.Count + " - " + points[i] + " - " + points[i].GetTerrain(map) + " - " + map);
                            return false;
                        }
                        else if (i == points.Count - 1 && points[i].GetCover(map)?.def?.Fillage == FillCategory.Full)
                        {
                            ZLogger.Message("7: " + caster + " - " + i + " - " + points.Count + " - " + points[i] + " - " + points[i].GetCover(map) + " - " + map);
                            return false;
                        }
                    }
                }
                if (resultingLine.dest.GetCover(map)?.def?.Fillage == FillCategory.Full)
                {
                    ZLogger.Message("8: " + caster + " - " + resultingLine.dest + " - " + resultingLine.dest.GetCover(map) + " - " + map);
                    return false;
                }
            }
            else
            {
                for (int i = points.Count - 1; i >= 0; i--)
                {
                    if (points[i] != resultingLine.Dest)
                    {
                        if (points[i].GetTerrain(map) != ZLevelsDefOf.ZL_OutsideTerrain)
                        {
                            ZLogger.Message("9: " + caster + " - " + i + " - " + points.Count + " - " + points[i] + " - " + points[i].GetTerrain(map) + " - " + map);
                            return false;
                        }
                    }
                }
                if (resultingLine.dest.GetCover(map)?.def?.Fillage == FillCategory.Full)
                {
                    ZLogger.Message("10: " + caster + " - " + resultingLine.dest + " - " + resultingLine.dest.GetCover(map) + " - " + map);
                    return false;
                }
            }
            return true;
        }
    }
    
    [HarmonyPatch(typeof(JobGiver_ConfigurableHostilityResponse), "TryGiveJob")]
    public class JobGiver_ConfigurableHostilityResponsePatch
    {
        public static bool recursiveTrap = false;
    
        [HarmonyPostfix]
        private static void JobGiver_ConfigurableHostilityResponsePostfix(JobGiver_ConfigurableHostilityResponse __instance, ref Job __result, Pawn pawn)
        {
            try
            {
                if (__result == null && !recursiveTrap)
                {
                    recursiveTrap = true;
                    CombatPatches_BestAttackTarget_Patch.multiMapSearch = true;
                    try
                    {
                        var job = __instance.TryGiveJob(pawn);
                        if (job != null)
                        {
                            ZLogger.Message("Second block: " + job);
                            if (job.targetA.Thing?.Map != null && job.targetA.Thing.Map != pawn.Map)
                            {
                                ZUtils.ZTracker.ResetJobTrackerFor(pawn);
                                ZUtils.ZTracker.BuildJobListFor(pawn, job.targetA.Thing.Map, job);
                                ZUtils.ZTracker.jobTracker[pawn].targetDest = new TargetInfo(job.targetA.Thing);
                                ZUtils.ZTracker.jobTracker[pawn].forceGoToDestMap = true;
                                __result = ZUtils.ZTracker.jobTracker[pawn].activeJobs[0];
                                ZUtils.ZTracker.jobTracker[pawn].activeJobs.RemoveAt(0);
                            }
                            else
                            {
                                __result = job;
                            }
                        }
                    }
                    finally
                    {
                        // The flags go back on every exit, so a fault cannot leave the search switched on for later calls.
                        recursiveTrap = false;
                        CombatPatches_BestAttackTarget_Patch.multiMapSearch = false;
                    }
                }
                else if (__result != null && __result.targetA.Thing?.Map != null && __result.targetA.Thing.Map != pawn.Map)
                {
                    ZLogger.Message("Second block: " + __result);
                    ZUtils.ZTracker.ResetJobTrackerFor(pawn);
                    ZUtils.ZTracker.BuildJobListFor(pawn, __result.targetA.Thing.Map, __result);
                    ZUtils.ZTracker.jobTracker[pawn].targetDest = new TargetInfo(__result.targetA.Thing);
                    ZUtils.ZTracker.jobTracker[pawn].forceGoToDestMap = true;
                    __result = ZUtils.ZTracker.jobTracker[pawn].activeJobs[0];
                    ZUtils.ZTracker.jobTracker[pawn].activeJobs.RemoveAt(0);
                }
            }
            catch (Exception exception)
            {
                // The game keeps the result it has. __result is not written here.
                CombatFaults.Log("JobGiver_ConfigurableHostilityResponse.TryGiveJob.Postfix", exception);
            }
        }
    }
    
    [HarmonyPatch(typeof(JobGiver_AIGotoNearestHostile), "TryGiveJob")]
    public class JobGiver_AIGotoNearestHostilePatch
    {
        public static bool recursiveTrap = false;
    
        [HarmonyPostfix]
        private static void JobGiver_AIGotoNearestHostilePostfix(JobGiver_AIGotoNearestHostile __instance, ref Job __result, Pawn pawn)
        {
            try
            {
                ZLogger.Message(pawn + " got response 4: " + __result);
                if (__result == null && !recursiveTrap)
                {
                    recursiveTrap = true;
                    CombatPatches_BestAttackTarget_Patch.multiMapSearch = true;
                    try
                    {
                        var job = __instance.TryGiveJob(pawn);
                        if (job != null)
                        {
                            ZLogger.Message("Second block: " + job);
                            if (job.targetA.Thing?.Map != null && job.targetA.Thing.Map != pawn.Map)
                            {
                                ZUtils.ZTracker.ResetJobTrackerFor(pawn);
                                ZUtils.ZTracker.BuildJobListFor(pawn, job.targetA.Thing.Map, job);
                                ZUtils.ZTracker.jobTracker[pawn].targetDest = new TargetInfo(job.targetA.Thing);
                                ZUtils.ZTracker.jobTracker[pawn].forceGoToDestMap = true;
                                __result = ZUtils.ZTracker.jobTracker[pawn].activeJobs[0];
                                ZUtils.ZTracker.jobTracker[pawn].activeJobs.RemoveAt(0);
                            }
                            else
                            {
                                __result = job;
                            }
                        }
                    }
                    finally
                    {
                        // The flags go back on every exit, so a fault cannot leave the search switched on for later calls.
                        recursiveTrap = false;
                        CombatPatches_BestAttackTarget_Patch.multiMapSearch = false;
                    }
                }
                else if (__result != null && __result.targetA.Thing?.Map != null && __result.targetA.Thing.Map != pawn.Map)
                {
                    ZLogger.Message("Second block: " + __result);
                    ZUtils.ZTracker.ResetJobTrackerFor(pawn);
                    ZUtils.ZTracker.BuildJobListFor(pawn, __result.targetA.Thing.Map, __result);
                    ZUtils.ZTracker.jobTracker[pawn].targetDest = new TargetInfo(__result.targetA.Thing);
                    ZUtils.ZTracker.jobTracker[pawn].forceGoToDestMap = true;
                    __result = ZUtils.ZTracker.jobTracker[pawn].activeJobs[0];
                    ZUtils.ZTracker.jobTracker[pawn].activeJobs.RemoveAt(0);
                }
                ZLogger.Message(pawn + " got result 4: " + __result + " in " + pawn.Map + " - " + __result?.targetA.Thing?.Map + " - mind enemy: " + pawn.mindState.enemyTarget);
            }
            catch (Exception exception)
            {
                // The game keeps the result it has. __result is not written here.
                CombatFaults.Log("JobGiver_AIGotoNearestHostile.TryGiveJob.Postfix", exception);
            }
        }
    }
    
    [HarmonyPatch(typeof(JobGiver_AIDefendPawn), "TryGiveJob")]
    public class JobGiver_AIDefendPawn_Patch
    {
        private static void Postfix(ref JobGiver_AIDefendPawn __instance, ref Job __result, ref Pawn pawn)
        {
            try
            {
                ZLogger.Message(pawn + " got response 4: " + __result);
            }
            catch (Exception exception)
            {
                CombatFaults.Log("JobGiver_AIDefendPawn.TryGiveJob.Postfix", exception);
            }
        }
    }
    
    [HarmonyPatch(typeof(JobGiver_AIFightEnemy), "TryGiveJob")]
    public class JobGiver_AIFightEnemy_Patch
    {
        public static bool recursiveTrap = false;
        private static void Postfix(ref JobGiver_AIFightEnemy __instance, ref Job __result, ref Pawn pawn)
        {
            try
            {
                if (__result == null && !recursiveTrap)
                {
                    recursiveTrap = true;
                    CombatPatches_BestAttackTarget_Patch.multiMapSearch = true;
                    try
                    {
                        var job = __instance.TryGiveJob(pawn);
                        if (job != null)
                        {
                            ZLogger.Message("Second block: " + job);
                            if (job.targetA.Thing?.Map != null && job.targetA.Thing.Map != pawn.Map)
                            {
                                if (!ZUtils.ZTracker.jobTracker.ContainsKey(pawn)) ZUtils.ZTracker.jobTracker[pawn] = new JobTracker
                                {
                                    activeJobs = new List<Job>()
                                };
                                ZUtils.ZTracker.ResetJobTrackerFor(pawn);
                                ZUtils.ZTracker.BuildJobListFor(pawn, job.targetA.Thing.Map, job);
                                ZUtils.ZTracker.jobTracker[pawn].targetDest = new TargetInfo(job.targetA.Thing);
                                ZUtils.ZTracker.jobTracker[pawn].forceGoToDestMap = true;
                                __result = ZUtils.ZTracker.jobTracker[pawn].activeJobs[0];
                                ZUtils.ZTracker.jobTracker[pawn].activeJobs.RemoveAt(0);
                            }
                            else
                            {
                                __result = job;
                            }
                        }
                    }
                    finally
                    {
                        // The flags go back on every exit, so a fault cannot leave the search switched on for later calls.
                        recursiveTrap = false;
                        CombatPatches_BestAttackTarget_Patch.multiMapSearch = false;
                    }
                }
                else if (__result != null && __result.targetA.Thing?.Map != null && __result.targetA.Thing.Map != pawn.Map)
                {
                    ZLogger.Message("Second block: " + __result);
                    if (!ZUtils.ZTracker.jobTracker.ContainsKey(pawn)) ZUtils.ZTracker.jobTracker[pawn] = new JobTracker
                    {
                        activeJobs = new List<Job>()
                    };
                    ZUtils.ZTracker.ResetJobTrackerFor(pawn);
                    ZUtils.ZTracker.BuildJobListFor(pawn, __result.targetA.Thing.Map, __result);
                    ZUtils.ZTracker.jobTracker[pawn].targetDest = new TargetInfo(__result.targetA.Thing);
                    ZUtils.ZTracker.jobTracker[pawn].forceGoToDestMap = true;
                    __result = ZUtils.ZTracker.jobTracker[pawn].activeJobs[0];
                    ZUtils.ZTracker.jobTracker[pawn].activeJobs.RemoveAt(0);
                }
                else if (__result == null && pawn.mindState.enemyTarget?.Map != null && pawn.mindState.enemyTarget.Map != pawn.Map)
                {
                    ZLogger.Message("Third block: " + __result);
                    if (!ZUtils.ZTracker.jobTracker.ContainsKey(pawn)) ZUtils.ZTracker.jobTracker[pawn] = new JobTracker
                    {
                        activeJobs = new List<Job>()
                    };
                    ZUtils.ZTracker.jobTracker[pawn].targetDest = new TargetInfo(pawn.mindState.enemyTarget);
                    ZUtils.ZTracker.jobTracker[pawn].forceGoToDestMap = true;
                    __result = JobMaker.MakeJob(ZLevelsDefOf.ZL_GoToMap);
                    ZUtils.ZTracker.jobTracker[pawn].mainJob = __result;
                    ZUtils.ZTracker.jobTracker[pawn].failIfTargetMapIsNotDest = true;
                    ZUtils.ZTracker.jobTracker[pawn].target = pawn.mindState.enemyTarget;
                }
                ZLogger.Message(pawn + " got result 5: " + __result + " in " + pawn.Map + " - " + __result?.targetA.Thing?.Map + " - mind enemy: " + pawn.mindState.enemyTarget);
            }
            catch (Exception exception)
            {
                // The game keeps the result it has. __result is not written here.
                CombatFaults.Log("JobGiver_AIFightEnemy.TryGiveJob.Postfix", exception);
            }
        }
    }
}
