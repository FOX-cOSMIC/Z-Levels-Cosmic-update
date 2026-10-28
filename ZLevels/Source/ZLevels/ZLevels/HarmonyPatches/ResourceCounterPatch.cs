using System;
using System.Collections.Generic;
using System.Configuration;
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

namespace ZLevels
{
    [StaticConstructorOnStartup]
    public static class ResourceCounterPatches
    {
        [HarmonyPatch(typeof(ResourceCounter))]
        [HarmonyPatch("UpdateResourceCounts")]
        public class UpdateResourceCounts_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(ResourceCounter __instance, Map ___map)
            {
                try
                {
                    var ZTracker = ZUtils.ZTracker;
                    if (ZTracker.ZLevelsTracker.ContainsKey(___map.Tile))
                    {
                        Dictionary<ThingDef, int> countedAllAmounts = new Dictionary<ThingDef, int>();
                        foreach (var map1 in ZTracker.ZLevelsTracker[___map.Tile].ZLevels.Values)
                        {
                            if (map1.resourceCounter != null)
                            {
                                map1.resourceCounter.ResetResourceCounts();
                                List<SlotGroup> allGroupsListForReading = map1.haulDestinationManager.AllGroupsListForReading;
                                for (int i = 0; i < allGroupsListForReading.Count; i++)
                                {
                                    foreach (Thing outerThing in allGroupsListForReading[i].HeldThings)
                                    {
                                        Thing innerIfMinified = outerThing.GetInnerIfMinified();
                                        if (innerIfMinified.def.CountAsResource && map1.resourceCounter.ShouldCount(innerIfMinified))
                                        {
                                            ThingDef def = innerIfMinified.def;
                                            if (countedAllAmounts.ContainsKey(def))
                                            {
                                                countedAllAmounts[def] += innerIfMinified.stackCount;
                                            }
                                            else
                                            {
                                                countedAllAmounts[def] = innerIfMinified.stackCount;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        foreach (var map1 in ZTracker.ZLevelsTracker[___map.Tile].ZLevels.Values)
                        {
                            if (map1.resourceCounter != null)
                            {
                                foreach (var d in countedAllAmounts)
                                {
                                    map1.resourceCounter.AllCountedAmounts[d.Key] = d.Value;
                                }
                            }
                        }
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[Z-Levels] UpdateResourceCounts patch produced an error. That should not happen and will break things. Send a Hugslib log to the Z-Levels developers. Error message: " + ex);
                };
                return true;
            }
        }

        [HarmonyPatch(typeof(Designator_Build))]
        [HarmonyPatch("ProcessInput")]
        public class ProcessInput_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo thingsOfDef = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsOfDef), new[] { typeof(ThingDef) });
                MethodInfo acrossLevels = AccessTools.Method(typeof(ProcessInput_Patch), nameof(ThingsOfDefAcrossLevels));
                int replacements = 0;
                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.Calls(thingsOfDef))
                    {
                        var loadDesignator = new CodeInstruction(OpCodes.Ldarg_0);
                        loadDesignator.labels.AddRange(instruction.labels);
                        loadDesignator.blocks.AddRange(instruction.blocks);
                        yield return loadDesignator;
                        yield return new CodeInstruction(OpCodes.Call, acrossLevels);
                        replacements++;
                    }
                    else
                    {
                        yield return instruction;
                    }
                }
                if (replacements != 1)
                {
                    throw new InvalidOperationException("Z-Levels expected one material availability query in Designator_Build.ProcessInput, found " + replacements);
                }
            }

            public static List<Thing> ThingsOfDefAcrossLevels(ListerThings lister, ThingDef stuff, Designator_Build designator)
            {
                List<Thing> localThings = lister.ThingsOfDef(stuff);
                var tracker = ZUtils.ZTracker;
                if (localThings.Count > 0 || tracker == null || !tracker.ZLevelsTracker.ContainsKey(designator.Map.Tile))
                {
                    return localThings;
                }
                foreach (Map map in tracker.GetAllMaps(designator.Map.Tile))
                {
                    if (map == designator.Map) continue;
                    List<Thing> things = map.listerThings.ThingsOfDef(stuff);
                    if (things.Count > 0) return things;
                }
                return localThings;
            }
        }
    }
}
