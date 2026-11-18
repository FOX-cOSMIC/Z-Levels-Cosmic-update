using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using static Verse.AI.ReservationManager;

namespace ZLevels
{
    [HarmonyPatch(typeof(LocalTargetInfo), MethodType.Constructor, new Type[] { typeof(IntVec3) })]
    public class LocalTargetInfo_Constructor_Patch
    {
        public static JobTracker curPawnJobTracker;
        private static void Postfix(LocalTargetInfo __instance)
        {
            if (curPawnJobTracker != null && __instance.cellInt.IsValid)
            {
                curPawnJobTracker.lookedAtLocalCellMap[__instance.cellInt] = ZUtils.currentLookedIntoMap;
                //ZLogger.Message($"{curPawnJobTracker.pawn} is looking into {__instance.cellInt} - {ZUtils.currentLookedIntoMap}");//, stacktrace: {new StackTrace().ToString()}");
            }
        }
    }
    
    [HarmonyPatch(typeof(Reachability), "CanReach", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
    public class CanReach_Patch
    {
        // Where the colonist stood before the prefix moved it to the target's floor. It travels in Harmony's __state, so a reach check nested
        // in another keeps its own copy.
        public sealed class Restore
        {
            public Map map;
            public IntVec3 cell;
        }

        // A map that has left the game can still sit in a look-up table. Its reachability is gone, so it must not be used.
        private static bool IsLiveMap(Map map)
        {
            return map != null && map.reachability != null && Find.Maps.Contains(map);
        }

        private static bool Prefix(ref bool __result, Reachability __instance, IntVec3 start, LocalTargetInfo dest, PathEndMode peMode, TraverseParms traverseParams, out Restore __state)
        {
            __state = null;
            var pawn = traverseParams.pawn;
            if (pawn != null && pawn.Map != null)
            {
                if (dest.HasThing && IsLiveMap(dest.thingInt.Map))
                {
                    if (dest.thingInt.Map != pawn.Map)
                    {
                        var cellToTeleport = ZUtils.GetCellToTeleportFrom(pawn.Map, pawn.Position, dest.thingInt.Map);
                        var cell = cellToTeleport.IsValid ? cellToTeleport : pawn.Position;
    
                        __state = new Restore { map = pawn.Map, cell = pawn.Position };
                        ZUtils.TeleportThing(pawn, dest.thingInt.Map, cell);
                        if (dest.thingInt.Map != __instance.map)
                        {
                            __result = dest.thingInt.Map.reachability.CanReach(cell, dest, peMode, traverseParams);
                            ZLogger.Message($"1 CanReach: Used dest thing map reachability: __instance.map: {__instance.map}, pawn: {pawn}, thing: {dest.thingInt}, pawn.Map: {pawn.Map}, thing.Map: {dest.thingInt.Map}, result: {__result}");
                            return false;
                        }
                    }
                    else if (dest.thingInt.Map != __instance.map)
                    {
                        __result = dest.thingInt.Map.reachability.CanReach(start, dest, peMode, traverseParams);
                        ZLogger.Message($"2 CanReach: Used dest thing map reachability: __instance.map: {__instance.map}, pawn: {pawn}, thing: {dest.thingInt}, pawn.Map: {pawn.Map}, thing.Map: {dest.thingInt.Map}, result: {__result}");
                        return false;
                    }
                }
                else if (ZUtils.ZTracker.jobTracker.TryGetValue(pawn, out var jobTracker) && jobTracker.lookedAtLocalCellMap != null
                    && jobTracker.lookedAtLocalCellMap.TryGetValue(dest.cellInt, out var mapDest) && IsLiveMap(mapDest))
                {
                    if (mapDest != pawn.Map)
                    {
                        var cellToTeleport = ZUtils.GetCellToTeleportFrom(pawn.Map, pawn.Position, mapDest);
                        var cell = cellToTeleport.IsValid ? cellToTeleport : pawn.Position;
    
                        __state = new Restore { map = pawn.Map, cell = pawn.Position };
                        ZUtils.TeleportThing(pawn, mapDest, cell);
                        if (mapDest != __instance.map)
                        {
                            __result = mapDest.reachability.CanReach(cell, dest, peMode, traverseParams);
                            ZLogger.Message($"3 CanReach: Used mapDest map reachability: __instance.map: {__instance.map}, pawn: {pawn}, mapDest: {mapDest}, pawn.Map: {pawn.Map}, result: {__result}");
                            return false;
                        }
                    }
                    else if (mapDest != __instance.map)
                    {
                        __result = mapDest.reachability.CanReach(start, dest, peMode, traverseParams);
                        ZLogger.Message($"4 CanReach: Used mapDest map reachability: __instance.map: {__instance.map}, pawn: {pawn}, mapDest: {mapDest}, pawn.Map: {pawn.Map}, result: {__result}");
                        return false;
                    }
                }
            }
            return true;
        }
        // The finalizer moves the colonist back after every call the prefix moved it for, also when the reach check on the other floor throws.
        // A postfix would be skipped then and leave the colonist on that floor (decision D15). The exception still goes on to the caller.
        // Harmony runs finalizers after every postfix, so another mod's postfix on this method sees the colonist still on the target's floor.
        private static void Finalizer(TraverseParms traverseParams, Restore __state)
        {
            if (__state != null && traverseParams.pawn != null)
            {
                ZUtils.TeleportThing(traverseParams.pawn, __state.map, __state.cell);
            }
        }
    }
    
    [HarmonyPatch(typeof(ReservationManager))]
    [HarmonyPatch("CanReserveStack")]
    public class ReservationManager_CanReserveStack_Patch
    {
        // Where the claimant stood travels in __state, so a nested check keeps its own copy, and the finalizer moves the claimant back also when
        // the check throws. A postfix would be skipped then and leave the claimant on the target's floor (D15 follow-ups, as CanReach_Patch).
        private static void Prefix(ref int __result, ReservationManager __instance, Pawn claimant, LocalTargetInfo target, out CanReach_Patch.Restore __state, int maxPawns = 1, ReservationLayerDef layer = null, bool ignoreOtherReservations = false)
        {
            __state = null;
            if (target.HasThing && target.Thing.Map != null && claimant.Map != null && target.Thing.Map != claimant.Map)
            {
                __state = new CanReach_Patch.Restore { map = claimant.Map, cell = claimant.Position };
                ZUtils.TeleportThing(claimant, target.Thing.Map, claimant.Position);
            }
        }

        private static void Finalizer(Pawn claimant, CanReach_Patch.Restore __state)
        {
            if (__state != null)
            {
                ZUtils.TeleportThing(claimant, __state.map, __state.cell);
            }
        }
    }
    [HarmonyPatch(typeof(ReservationManager))]
    [HarmonyPatch("CanReserve")]
    public class ReservationManager_CanReserve_Patch
    {
        // Where the claimant stood travels in __state; the finalizer moves it back, also when the check on the other floor throws (D15
        // follow-ups, as CanReach_Patch).
        private static bool Prefix(ref bool __result, ReservationManager __instance, Pawn claimant, LocalTargetInfo target, out CanReach_Patch.Restore __state, int maxPawns = 1, int stackCount = -1, ReservationLayerDef layer = null, bool ignoreOtherReservations = false)
        {
            __state = null;
            if (claimant.RaceProps.Humanlike && claimant.Map != null)
            {
                if (target.HasThing)
                {
                    if (target.thingInt.Map != null && target.thingInt.Map != claimant.Map)
                    {
                        var cellToTeleport = ZUtils.GetCellToTeleportFrom(claimant.Map, claimant.Position, target.thingInt.Map);
                        var cell = cellToTeleport.IsValid ? cellToTeleport : claimant.Position;
    
                        __state = new CanReach_Patch.Restore { map = claimant.Map, cell = claimant.Position };
                        ZUtils.TeleportThing(claimant, target.thingInt.Map, cell);
                        __result = claimant.CanReserve(target, maxPawns, stackCount, layer, ignoreOtherReservations);
                        return false;
                    }
                }
                else
                {
                    if (ZUtils.ZTracker.jobTracker.TryGetValue(claimant, out var jobTracker) && jobTracker.lookedAtLocalCellMap != null
                        && jobTracker.lookedAtLocalCellMap.TryGetValue(target.cellInt, out var mapDest))
                    {
                        if (mapDest != null && mapDest != claimant.Map)
                        {
                            var cellToTeleport = ZUtils.GetCellToTeleportFrom(claimant.Map, claimant.Position, mapDest);
                            var cell = cellToTeleport.IsValid ? cellToTeleport : claimant.Position;
    
                            __state = new CanReach_Patch.Restore { map = claimant.Map, cell = claimant.Position };
                            ZUtils.TeleportThing(claimant, mapDest, cell);
                            __result = claimant.CanReserve(target, maxPawns, stackCount, layer, ignoreOtherReservations);
                            return false;
                        }
                    }
                }
            }
            return true;
        }
    
        // Harmony runs finalizers after every postfix, so the postfix below still sees the claimant on the target's floor. It reads no floor of
        // the claimant.
        private static void Finalizer(Pawn claimant, CanReach_Patch.Restore __state)
        {
            if (__state != null)
            {
                ZUtils.TeleportThing(claimant, __state.map, __state.cell);
            }
        }

        private static void Postfix(ref bool __result, Pawn claimant, LocalTargetInfo target, int maxPawns = 1, int stackCount = -1, ReservationLayerDef layer = null, bool ignoreOtherReservations = false)
        {
            if (__result)
            {
                var ZTracker = ZUtils.ZTracker;
                if (claimant.RaceProps.Humanlike && ZTracker.jobTracker != null)
                {
                    var thing = target.thingInt;
                    if (thing != null)
                    {
                        foreach (var data in ZTracker.jobTracker)
                        {
                            var otherPawn = data.Key;
                            var otherPawnJobTracker = data.Value;
                            if (otherPawn != claimant && otherPawnJobTracker.reservedThings != null)
                            {
                                foreach (var reservation in otherPawnJobTracker.reservedThings)
                                {
                                    if (reservation.thingInt == target.thingInt)
                                    {
                                        var shouldChangeResult = !(otherPawn.jobs?.curDriver is JobDriver_TakeToBed);// || !(data.Key.jobs?.curDriver is JobDriver_LayDown));
                                        if (shouldChangeResult)
                                        {
                                            try
                                            {
                                                ZLogger.Message("reservation.thingInt: " + reservation.thingInt + " - target.thingInt: " + target.thingInt);
                                                ZLogger.Message(data.Key + " - 1 data.Value.mainJob: " + data.Value.mainJob);
                                                ZLogger.Message(data.Key + " - 1 data.Key.CurJob: " + data.Key.CurJob);
                                                ZLogger.Message(data.Key + " - 1 thing.Map.reservationManager.reservations.Any(x => x.Target.thingInt == thing && x.claimant == data.Key): " + thing.Map.reservationManager.reservations.Any(x => x.Target.thingInt == thing && x.claimant == data.Key));
                                            }
                                            catch
                                            {
    
                                            }
                                           if (otherPawn.Map == thing.Map && !thing.Map.reservationManager.reservations.Any(x => x.Target.thingInt == thing && x.claimant == otherPawn))
                                            {
                                                ZLogger.Message($"PREVENTED ZTRACKER reservation disfunction: claimant: {claimant}, pawn: {data.Key}, thing: {thing}");
                                                continue;
                                            }
                                            __result = false;
                                            ZLogger.Message($"shouldChangeResult Detected ZTRACKER reservation disfunction: claimant: {claimant}, pawn: {data.Key}, thing: {thing}");
                                            return;
                                        }
                                    }
                                    //Log.Message($"ZTracker reservation: map: Reservation: {data.Key}, target: {reservation}, {data.Key.Map} - {reservation.Thing?.Map}");
                                }
                            }
                        }
                    }
                    else if (ZTracker.jobTracker.TryGetValue(claimant, out var jobTracker) && jobTracker.lookedAtLocalCellMap.TryGetValue(target.cellInt, out var mapDest) && mapDest != null)
                    {
                        var cell = target.cellInt;
                        foreach (var data in ZTracker.jobTracker)
                        {
                            var otherPawn = data.Key;
                            var otherPawnJobTracker = data.Value;
                            if (otherPawn != claimant && otherPawnJobTracker.reservedCells != null)
                            {
                                foreach (var reservation in otherPawnJobTracker.reservedCells)
                                {
                                    if (reservation.cellInt == cell && otherPawnJobTracker.mapDest == jobTracker.mapDest)
                                    {
                                        try
                                        {
                                            ZLogger.Message("claimant: " + claimant + " - " + otherPawn + " - 2 data.Value.mainJob: " + data.Value.mainJob);
                                            ZLogger.Message("claimant: " + claimant + " - " + otherPawn + " - 2 data.Key.CurJob: " + data.Key.CurJob);
                                            ZLogger.Message("claimant: " + claimant + " - " + otherPawn + " - 2 jobTracker.lookedAtMap.reservationManager.reservations.Any(x => !x.Target.HasThing && x.Target.cellInt == cell && x.claimant == data.Key)): " + jobTracker.mapDest.reservationManager.reservations.Any(x => !x.Target.HasThing && x.Target.cellInt == cell && x.claimant == data.Key));
                                        }
                                        catch { }
                                        if (otherPawn.Map == jobTracker.mapDest && !jobTracker.mapDest.reservationManager.reservations.Any(x => !x.Target.HasThing && x.Target.cellInt == cell && x.claimant == otherPawn))
                                        {
                                            ZLogger.Message($"2 PREVENTED ZTRACKER reservation disfunction: claimant: {claimant}, pawn: {data.Key}, cell: {cell}");
                                            continue;
                                        }
                                        __result = false;
                                        ZLogger.Message($"reservation.cellInt == cell 2 Detected ZTRACKER reservation disfunction: claimant: {claimant}, pawn: {data.Key}, cell: {cell}");
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            }
    
            if (ZLogger.DebugEnabled)
            {
                if (!__result)
                {
                    ZLogger.Error($"claimant {claimant} can't reserve target: {target}");
                }
            }
        }
    }
}