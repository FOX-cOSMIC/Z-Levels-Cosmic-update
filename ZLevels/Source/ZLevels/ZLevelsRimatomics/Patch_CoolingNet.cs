using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using ZLevels;

namespace ZLevelsRimatomics
{
    // Joins the cooling loops to the Rimatomics nets. Before each CoolingNet.Tick, the net takes the coolers and turbines of every net in the loops of its
    // columns, so Rimatomics computes its cooling ratio over the joined network. The lists are new on each call. A column outside its own loop forms no
    // loop, so only that loop stays out, and the other loops still join. When the merge throws, the net takes its own coolers and turbines, so nothing
    // joined stays in the ratio. Each distinct failure is logged once per game. The original Tick then runs, even when a call on the failure path throws.
    [HarmonyPatch]
    public static class Patch_CoolingNet
    {
        // The failures already logged, by key. A key names the net, the exception type, and the top frame, so a second site logs too.
        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        // The game that the set belongs to. A new game object clears the set, so each game logs its own first failures again.
        private static Verse.Game loggedGame;

        static MethodBase TargetMethod()
        {
            return AccessTools.Method("Rimatomics.CoolingNet:Tick");
        }

        static void Prefix(Rimatomics.CoolingNet __instance)
        {
            ForgetFailuresOfEarlierGames();
            try
            {
                Merge(__instance);
            }
            catch (Exception exception)
            {
                // The net's own lists come first, so the joined things leave the ratio before anything else runs. Each call below has its own try.
                bool ownThings = RunStep("own things", () => UseOwnThings(__instance));
                string net = StepValue("describe the net", () => DescribeNet(__instance), "a net that could not be described");
                string key = net + "|" + exception.GetType().FullName + "|" + TopFrame(exception);
                if (loggedFailures.Add(key))
                {
                    Log.Error("Z-Levels cooling loop merge failed on the net of " + net
                        + (ownThings ? ", so the net uses its own coolers and turbines: " : ": ") + exception);
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
                    // An unusable column forms no loop. Only its own loop stays out, and the loops of the other columns still join.
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

        // Runs one call from the failure path. A throw logs once, and the call returns false, so the prefix carries on and the original Tick runs.
        private static bool RunStep(string step, Action call)
        {
            try
            {
                call();
                return true;
            }
            catch (Exception failure)
            {
                LogStepFailureOnce(step, failure);
                return false;
            }
        }

        // Runs one call from the failure path and returns its value. A throw logs once, and the fallback is returned instead.
        private static string StepValue(string step, Func<string> call, string fallback)
        {
            try
            {
                return call();
            }
            catch (Exception failure)
            {
                LogStepFailureOnce(step, failure);
                return fallback;
            }
        }

        private static void LogStepFailureOnce(string step, Exception failure)
        {
            if (!loggedFailures.Add("step " + step + "|" + failure.GetType().FullName + "|" + TopFrame(failure))) return;
            Log.Error("Z-Levels cooling step " + step + " failed, so the original Tick runs with the lists it has: " + failure);
        }

        // The method and line where the exception was thrown. Two throws from two sites give two keys.
        private static string TopFrame(Exception exception)
        {
            System.Diagnostics.StackFrame frame = new System.Diagnostics.StackTrace(exception, true).GetFrame(0);
            if (frame == null) return "no frame";
            MethodBase method = frame.GetMethod();
            string name = method == null ? "unknown method" : method.DeclaringType + "." + method.Name;
            return name + " line " + frame.GetFileLineNumber();
        }

        // A new game object clears the set, so each game logs its own first failures again. The comparison runs on each prefix call.
        private static void ForgetFailuresOfEarlierGames()
        {
            if (ReferenceEquals(loggedGame, Current.Game)) return;
            loggedGame = Current.Game;
            loggedFailures.Clear();
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
