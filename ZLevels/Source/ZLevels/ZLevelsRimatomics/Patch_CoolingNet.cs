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
    // the net takes its own coolers and turbines, so nothing joined stays in the ratio. Each distinct failure is logged once per game, up to 64 distinct
    // failures in a game. Each read and call on the failure path that can run game or exception code is guarded, and a throw there logs its exception
    // type. A column whose name cannot be read is named by its cell and map. No exception leaves the prefix, and the original Tick then runs.
    [HarmonyPatch]
    public static class Patch_CoolingNet
    {
        // The lines already logged, by key. A key names the map and cell of the net or column, the exception type, the message, and the top frame's
        // method and IL offset. A different key logs again, up to MaxFailureKeys keys in a game.
        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        // The most distinct keys one game logs. The first key past the cap writes one line that says so, and then nothing else is written in the game.
        private const int MaxFailureKeys = 64;

        // Set once the cap line is written in this game.
        private static bool capReported;

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
                LogOnce("step " + name + "|" + where + "|" + DescribeException(failure) + "|" + frame,
                    () => "Z-Levels cooling step " + name + " failed for the net of " + described
                        + ", so the original Tick runs with the lists it has: " + ExceptionText(failure), false);
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
            LogOnce(where + "|" + DescribeException(exception) + "|" + frame,
                () => "Z-Levels cooling loop merge failed on the net of " + described
                    + (ownThings ? ", so the net uses its own coolers and turbines: "
                        : ", and the net keeps its last lists, which may still hold joined coolers: ") + ExceptionText(exception), false);
        }

        // Logs one line for a key, once per game. A line that cannot be built writes nothing, and the key stays unmarked, so the next tick builds it again.
        // The key is marked before the Verse call, so a logger that fails is never asked for the same line twice.
        private static void LogOnce(string key, Func<string> line, bool warning)
        {
            if (loggedFailures.Contains(key)) return;
            string text;
            try
            {
                text = line();
            }
            catch (Exception)
            {
                // The line could not be built, so nothing is written and the key stays unmarked. The next tick builds the line again.
                return;
            }
            if (loggedFailures.Count >= MaxFailureKeys)
            {
                if (capReported) return;
                capReported = true;
                Write("Z-Levels cooling has logged " + MaxFailureKeys + " distinct failures in this game. Further distinct failures are not logged in this game.",
                    true);
                return;
            }
            loggedFailures.Add(key);
            Write(text, warning);
        }

        // Writes one line through the Verse logger. The key is already marked, so a throw from the logger is never retried. If the Verse call throws,
        // the text goes out once through UnityEngine.Debug.LogError with the exception type. If that call throws too, nothing is written.
        private static void Write(string text, bool warning)
        {
            try
            {
                if (warning) Log.Warning(text);
                else Log.Error(text);
            }
            catch (Exception loggerFailure)
            {
                try
                {
                    UnityEngine.Debug.LogError(text + " Logger threw " + TypeNameOf(loggerFailure) + ".");
                }
                catch (Exception)
                {
                    // Unity logging threw as well, so no channel is left. The key stays marked, so the line is not tried again.
                    return;
                }
            }
        }

        private static void LogHelperFailureOnce(string name, Exception failure)
        {
            LogOnce("helper " + name + "|" + DescribeException(failure),
                () => "Z-Levels cooling helper " + name + " failed, so the original Tick runs: " + ExceptionText(failure), false);
        }

        // The type and message of an exception, for a key or a log line. Each read is guarded. A getter that throws is named by the type it threw, so
        // the original type still reaches the log. This never throws.
        private static string DescribeException(Exception exception)
        {
            string text;
            try
            {
                text = exception.Message;
            }
            catch (Exception getterFailure)
            {
                text = "message getter threw " + TypeNameOf(getterFailure);
            }
            return TypeNameOf(exception) + ": " + text;
        }

        // The full type name of an exception. It is guarded like the other reads on the failure path, and it falls back to a fixed name.
        private static string TypeNameOf(Exception exception)
        {
            try
            {
                return exception.GetType().FullName;
            }
            catch (Exception)
            {
                return "an exception of unknown type";
            }
        }

        // The exception as text for a log line. ToString reads the message, so a throwing getter falls back to DescribeException. This never throws.
        private static string ExceptionText(Exception exception)
        {
            try
            {
                return exception.ToString();
            }
            catch (Exception)
            {
                return DescribeException(exception);
            }
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
            capReported = false;
        }

        // The name of a column for a line. When the name read fails, the cell and the map name the column instead, and each read is guarded.
        private static string NameOrPlace(CompCoolingColumn column)
        {
            string name = Helper("column name", () => ColumnName(column), (string)null);
            if (name != null) return name;
            string cell = Helper("column cell", () => column.parent.Position.ToString(), "a cell that could not be read");
            string map = Helper("column map", () => column.parent.Map.uniqueID.ToString(), "a map that could not be read");
            return "at cell " + cell + " on map " + map;
        }

        // An unusable column forms no loop, so its loop is dropped. The other loops of its net still join.
        private static void LogUnknownLoopOnce(CompCoolingColumn start)
        {
            string name = NameOrPlace(start);
            string key = Helper("column key", () => ColumnKey(start), "column " + IdentityOf(start));
            LogOnce("unknown loop|" + key,
                () => "Z-Levels cooling column " + name + " is not usable, so its loop is dropped. The other loops of its net still join."
                    + " This is logged once for each column in each game.", true);
        }

        // A column with an empty pipe field has no net that this merge can read, so the merge skips it. This is the normal case while a column is
        // placed. Joins start after the next pipe grid rebuild.
        private static void LogNoNetOnce(CompCoolingColumn column)
        {
            string name = NameOrPlace(column);
            string key = Helper("column key", () => ColumnKey(column), "column " + IdentityOf(column));
            LogOnce("no net|" + key,
                () => "Z-Levels cooling column " + name + " has no pipe net in its field yet. This is the normal case while a column is placed."
                    + " Joins start after the next pipe grid rebuild, so this merge skips the column until then."
                    + " This is logged once for each column in each game.", true);
        }
    }
}
