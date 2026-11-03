using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using ZLevels;

namespace ZLevelsRimatomics
{
    // Joins the cooling loops to the Rimatomics nets. Before each CoolingNet.Tick, the net takes the coolers and turbines of every net in the loops of its
    // columns, so Rimatomics computes its cooling ratio over the joined network. The lists are new on each call. A net with no column keeps the lists
    // Rimatomics built. A throw is logged once and the original Tick runs, so the pipe nets after this one still tick.
    [HarmonyPatch]
    public static class Patch_CoolingNet
    {
        private static bool loggedFailure;
        private static bool loggedUnknownLoop;

        static MethodBase TargetMethod()
        {
            return AccessTools.Method("Rimatomics.CoolingNet:Tick");
        }

        static void Prefix(Rimatomics.CoolingNet __instance)
        {
            try
            {
                Merge(__instance);
            }
            catch (Exception exception)
            {
                // The lists are assigned only after the whole merge is built, so a throw leaves them as they were. The original Tick still runs.
                if (loggedFailure) return;
                loggedFailure = true;
                Log.Error("Z-Levels cooling loop merge failed, so this net keeps the lists it had: " + exception);
            }
        }

        private static void Merge(Rimatomics.CoolingNet net)
        {
            List<CompCoolingColumn> columns = ColumnsOn(net);
            if (columns.Count == 0) return;
            // The usable set and the tracker are read once for this net's tick, so each pair does not repeat the grid check or the tracker lookup.
            HashSet<CompCoolingColumn> usable = CoolingLoopRegistry.UsableColumns();
            ZLevelsManager tracker = CoolingLoopRegistry.TrackerOf();
            var nets = new List<Rimatomics.BasePipeNet> { net };
            var inLoops = new HashSet<CompCoolingColumn>();
            foreach (CompCoolingColumn start in columns)
            {
                if (inLoops.Contains(start)) continue;
                List<CompCoolingColumn> loop = CoolingLoopRegistry.LoopOf(start, usable, tracker);
                if (!loop.Contains(start))
                {
                    // An unusable column has no loop to join. It keeps this net's own coolers and turbines, and the other columns still join.
                    LogUnknownLoopOnce(start);
                    continue;
                }
                foreach (CompCoolingColumn member in loop)
                {
                    inLoops.Add(member);
                    Rimatomics.BasePipeNet memberNet = member.pipe?.pipeNetRef;
                    if (memberNet != null && !nets.Contains(memberNet)) nets.Add(memberNet);
                }
            }
            var coolers = new List<Rimatomics.CoolingSystem>();
            var turbines = new List<Rimatomics.Turbine>();
            foreach (Rimatomics.BasePipeNet joined in nets)
            {
                foreach (ThingWithComps thing in joined.PipedThings)
                {
                    if (thing is Rimatomics.CoolingSystem cooler) coolers.Add(cooler);
                    if (thing is Rimatomics.Turbine turbine) turbines.Add(turbine);
                }
            }
            net.Coolers = coolers;
            net.Turbines = turbines;
        }

        // The columns whose pipe is in this net. They are found among the net's own pipes, which PipedThings keeps current, so a removed column is not listed.
        private static List<CompCoolingColumn> ColumnsOn(Rimatomics.CoolingNet net)
        {
            var columns = new List<CompCoolingColumn>();
            foreach (ThingWithComps thing in net.PipedThings)
            {
                CompCoolingColumn column = thing.GetComp<CompCoolingColumn>();
                if (column?.pipe != null && ReferenceEquals(column.pipe.pipeNetRef, net)) columns.Add(column);
            }
            return columns;
        }

        private static void LogUnknownLoopOnce(CompCoolingColumn start)
        {
            if (loggedUnknownLoop) return;
            loggedUnknownLoop = true;
            Log.Warning("Z-Levels cooling column " + (start.parent?.def?.defName ?? "cooling column")
                + " is not in the loop that the registry returns for it, so its loop is unknown and is not joined. This is logged once.");
        }
    }
}
