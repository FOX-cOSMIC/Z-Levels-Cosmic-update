using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
using ZLevels;

namespace ZLevelsRimatomics
{
    // Joins the cooling loops to the Rimatomics nets. Before each CoolingNet.Tick, the net takes the coolers and turbines of every net in the loops of its
    // columns, so Rimatomics computes its cooling ratio over the joined network. The lists are new on each call. A column outside its own loop forms no
    // loop, so only that loop stays out, and the other loops still join. A member whose pipe field is empty is skipped and logged. When the merge throws,
    // the net takes its own coolers and turbines, so nothing joined stays in the ratio. Each distinct failure is logged once per game. Every call on the
    // failure path runs in a wrapper, so no exception leaves the prefix, and the original Tick then runs.
    [HarmonyPatch]
    public static class Patch_CoolingNet
    {
        // The lines already logged, by key. A key names the map and cell of the net or column, the exception type, the message, and the top frame's
        // method and IL offset. A different key logs again.
        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        // The game that the set belongs to. The reference is weak, so a game that is over can be collected while the main menu is open. A different
        // game clears the set.
        private static System.WeakReference<Verse.Game> loggedGame;

        // A number that names an object with no map and cell. The table holds the object weakly, so the number lasts as long as the object does.
        private sealed class Numbered
        {
            public int Value;
        }

        private static readonly ConditionalWeakTable<object, Numbered> numbers = new ConditionalWeakTable<object, Numbered>();
        private static int lastNumber;

        static MethodBase TargetMethod()
        {
            return AccessTools.Method("Rimatomics.CoolingNet:Tick");
        }

        static void Prefix(Rimatomics.CoolingNet __instance)
        {
            Guard("forget earlier games", ForgetFailuresOfEarlierGames);
            try
            {
                Merge(__instance);
            }
            catch (Exception exception)
            {
                Guard("report the merge failure", () => ReportMergeFailure(__instance, exception));
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
                    // The field is read as it is. The CompPipe.net getter would rebuild the grids. This code also runs inside a rebuild, from InitNet,
                    // so the getter must not run here, and an empty field is skipped and logged instead.
                    Rimatomics.BasePipeNet memberNet = member.pipe?.pipeNetRef;
                    if (memberNet == null)
                    {
                        LogNoNetOnce(member);
                        continue;
                    }
                    if (!nets.Contains(memberNet)) nets.Add(memberNet);
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

        // The columns whose pipe is in this net. They are found among the net's own things, which PipedThings keeps current, so a removed column is not
        // listed. A column whose pipe field is empty is skipped and logged.
        private static List<CompCoolingColumn> ColumnsOn(Rimatomics.CoolingNet net)
        {
            var columns = new List<CompCoolingColumn>();
            foreach (ThingWithComps thing in net.PipedThings)
            {
                CompCoolingColumn column = thing.GetComp<CompCoolingColumn>();
                if (column?.pipe == null) continue;
                if (column.pipe.pipeNetRef == null)
                {
                    LogNoNetOnce(column);
                    continue;
                }
                if (ReferenceEquals(column.pipe.pipeNetRef, net)) columns.Add(column);
            }
            return columns;
        }

        // The first thing of the net, taken at the smallest z and then the smallest x. Choosing by position names the net the same way on every tick,
        // because InitNet shuffles PipedThings.
        private static ThingWithComps FirstThing(Rimatomics.CoolingNet net)
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
            return first;
        }

        // The def, cell, and map of the net's first thing, for a message.
        private static string DescribeNet(Rimatomics.CoolingNet net)
        {
            ThingWithComps first = FirstThing(net);
            if (first == null) return "a net with no things";
            if (first.Map == null) return first.def.defName + " at " + first.Position;
            return first.def.defName + " at " + first.Position + " on map " + first.Map.uniqueID;
        }

        // The key of the net. A net whose first thing is on a map is keyed by that map, the def, and the cell, so two nets that share a cell on two levels
        // have two keys. Any other net is keyed by its number.
        private static string NetKey(Rimatomics.CoolingNet net)
        {
            ThingWithComps first = FirstThing(net);
            if (first == null || first.Map == null) return "net " + IdentityOf(net);
            return "map " + first.Map.uniqueID + ", " + first.def.defName + " at " + first.Position;
        }

        // The def, cell, and map of a column, for a message.
        private static string ColumnName(CompCoolingColumn column)
        {
            ThingWithComps thing = column.parent;
            if (thing == null || thing.Map == null) return "a cooling column with no place";
            return thing.def.defName + " at " + thing.Position + " on map " + thing.Map.uniqueID;
        }

        // The key of a column. A column with a place is keyed by its map, def, and cell. Any other column is keyed by its number.
        private static string ColumnKey(CompCoolingColumn column)
        {
            ThingWithComps thing = column.parent;
            if (thing == null || thing.Map == null) return "column " + IdentityOf(column);
            return "map " + thing.Map.uniqueID + ", " + thing.def.defName + " at " + thing.Position;
        }

        // A number that names one object for as long as the object lives.
        private static int IdentityOf(object item)
        {
            return numbers.GetValue(item, _ => new Numbered { Value = ++lastNumber }).Value;
        }

        // Runs one call on the failure path. A throw logs once, and the caller carries on.
        private static void Guard(string name, Action call)
        {
            try
            {
                call();
            }
            catch (Exception failure)
            {
                LogHelperFailureOnce(name, failure);
            }
        }

        // Runs one helper on the failure path and returns its value. A throw logs once, and the fallback is returned.
        private static T Helper<T>(string name, Func<T> call, T fallback)
        {
            try
            {
                return call();
            }
            catch (Exception failure)
            {
                LogHelperFailureOnce(name, failure);
                return fallback;
            }
        }

        // Runs one step of the fallback. A throw logs once, with the net, and the caller carries on. Returns false when the step threw.
        private static bool Step(string name, Rimatomics.CoolingNet net, Action call)
        {
            try
            {
                call();
                return true;
            }
            catch (Exception failure)
            {
                string described = Helper("describe the net", () => DescribeNet(net), "a net that could not be described");
                string where = Helper("key of the net", () => NetKey(net), "net " + IdentityOf(net));
                string frame = Helper("top frame", () => TopFrame(failure), "frame unavailable");
                LogOnce("step " + name + "|" + where + "|" + failure.GetType().FullName + "|" + failure.Message + "|" + frame,
                    () => "Z-Levels cooling step " + name + " failed for the net of " + described
                        + ", so the original Tick runs with the lists it has: " + failure, false);
                return false;
            }
        }

        // Reports a merge that threw. The net's own things come first, so the joined things leave the ratio before anything else runs.
        private static void ReportMergeFailure(Rimatomics.CoolingNet net, Exception exception)
        {
            bool ownThings = Step("own things", net, () => UseOwnThings(net));
            string described = Helper("describe the net", () => DescribeNet(net), "a net that could not be described");
            string where = Helper("key of the net", () => NetKey(net), "net " + IdentityOf(net));
            string frame = Helper("top frame", () => TopFrame(exception), "frame unavailable");
            LogOnce(where + "|" + exception.GetType().FullName + "|" + exception.Message + "|" + frame,
                () => "Z-Levels cooling loop merge failed on the net of " + described
                    + (ownThings ? ", so the net uses its own coolers and turbines: " : ": ") + exception, false);
        }

        // Logs one line for a key, once per game. The line is built inside the try, so a throw while building it or logging it cannot escape the prefix.
        // If the logger itself throws, no other channel reaches the game log, so the caller carries on and the original Tick runs.
        private static void LogOnce(string key, Func<string> line, bool warning)
        {
            try
            {
                if (!loggedFailures.Add(key)) return;
                string text = line();
                if (warning) Log.Warning(text);
                else Log.Error(text);
            }
            catch (Exception)
            {
                // The logger threw. Nothing else reaches the game log, so the caller carries on.
            }
        }

        private static void LogHelperFailureOnce(string name, Exception failure)
        {
            LogOnce("helper " + name + "|" + failure.GetType().FullName + "|" + failure.Message,
                () => "Z-Levels cooling helper " + name + " failed, so the original Tick runs: " + failure, false);
        }

        // The method and IL offset of the exception's top frame. The offset is part of the frame, so it does not need a pdb.
        private static string TopFrame(Exception exception)
        {
            System.Diagnostics.StackFrame frame = new System.Diagnostics.StackTrace(exception, true).GetFrame(0);
            if (frame == null) return "no frame";
            MethodBase method = frame.GetMethod();
            string name = method == null ? "unknown method" : method.DeclaringType + "." + method.Name;
            return name + " at IL offset " + frame.GetILOffset();
        }

        // A game other than the one the set belongs to clears the set. The reference is weak, so a game that is over can be collected.
        private static void ForgetFailuresOfEarlierGames()
        {
            Verse.Game game = Current.Game;
            Verse.Game logged;
            if (loggedGame != null && loggedGame.TryGetTarget(out logged) && ReferenceEquals(logged, game)) return;
            loggedGame = game == null ? null : new System.WeakReference<Verse.Game>(game);
            loggedFailures.Clear();
        }

        // An unusable column forms no loop, so its loop is dropped. The other loops of its net still join.
        private static void LogUnknownLoopOnce(CompCoolingColumn start)
        {
            string name = ColumnName(start);
            LogOnce("unknown loop|" + ColumnKey(start),
                () => "Z-Levels cooling column " + name + " is not usable, so its loop is dropped. The other loops of its net still join."
                    + " This is logged once for each column in each game.", true);
        }

        // A column with an empty pipe field has no net that this merge can read, so the merge skips it.
        private static void LogNoNetOnce(CompCoolingColumn column)
        {
            string name = ColumnName(column);
            LogOnce("no net|" + ColumnKey(column),
                () => "Z-Levels cooling column " + name + " has no pipe net in its field, so this merge skips it."
                    + " This is logged once for each column in each game.", true);
        }
    }
}
