using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using ZLevels;

namespace ZLevelsRimatomics
{
    // Joins the cooling loops to the Rimatomics nets. Before each CoolingNet.Tick, the net takes the coolers and turbines of every net in the loops of its
    // columns, so Rimatomics computes its cooling ratio over the joined network. The lists are new on each call. When the merge throws, or a column's
    // loop is unknown, the net takes its own coolers and turbines, so nothing joined stays in the ratio. Each distinct failure is logged once. The
    // original Tick then runs.
    [HarmonyPatch]
    public static class Patch_CoolingNet
    {
        // The failures already logged, by key. A failure logs once. A different failure has a different key, so it logs too.
        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

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
                // The net's own lists come first, so the joined things leave the ratio before anything else runs.
                UseOwnThings(__instance);
                string key = exception.GetType().FullName + ": " + exception.Message;
                if (loggedFailures.Add(key))
                {
                    Log.Error("Z-Levels cooling loop merge failed on the net of " + DescribeNet(__instance)
                        + ", so the net uses its own coolers and turbines: " + exception);
                }
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
                    // A column with no loop cannot say what joins, so the net uses its own things for this tick.
                    LogUnknownLoopOnce(start);
                    UseOwnThings(net);
                    return;
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
                AddThings(joined, coolers, turbines);
            }
            net.Coolers = coolers;
            net.Turbines = turbines;
        }

        // Sets the net's coolers and turbines from its own PipedThings, the same lists that InitNet builds. Nothing from another net is in the result.
        private static void UseOwnThings(Rimatomics.CoolingNet net)
        {
            var coolers = new List<Rimatomics.CoolingSystem>();
            var turbines = new List<Rimatomics.Turbine>();
            AddThings(net, coolers, turbines);
            net.Coolers = coolers;
            net.Turbines = turbines;
        }

        private static void AddThings(Rimatomics.BasePipeNet net, List<Rimatomics.CoolingSystem> coolers, List<Rimatomics.Turbine> turbines)
        {
            foreach (ThingWithComps thing in net.PipedThings)
            {
                if (thing is Rimatomics.CoolingSystem cooler) coolers.Add(cooler);
                if (thing is Rimatomics.Turbine turbine) turbines.Add(turbine);
            }
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

        // The def and cell of the net's first thing, taken at the smallest z and then the smallest x. Choosing by cell names the net the same way on every
        // tick, because InitNet shuffles PipedThings.
        private static string DescribeNet(Rimatomics.CoolingNet net)
        {
            ThingWithComps first = null;
            foreach (ThingWithComps thing in net.PipedThings)
            {
                if (first == null || thing.Position.z < first.Position.z
                    || (thing.Position.z == first.Position.z && thing.Position.x < first.Position.x))
                {
                    first = thing;
                }
            }
            return first == null ? "a net with no things" : first.def.defName + " at " + first.Position;
        }

        private static void LogUnknownLoopOnce(CompCoolingColumn start)
        {
            string column = start.parent == null
                ? "a cooling column with no thing"
                : start.parent.def.defName + " at " + start.parent.Position;
            if (!loggedFailures.Add("unknown loop|" + column)) return;
            Log.Warning("Z-Levels cooling column " + column + " is not in the loop that the registry returns for it, so the cooling net that holds it"
                + " uses its own coolers and turbines. This is logged once for each column.");
        }
    }
}
