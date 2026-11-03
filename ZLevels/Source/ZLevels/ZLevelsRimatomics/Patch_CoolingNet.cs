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
    // loop, so only that loop stays out, and the other loops still join. A member with no pipe net is skipped, and a notice is logged once the column has
    // had NoNetGraceTicks to join. When the merge throws, the net takes its own coolers and turbines, so nothing joined stays in the ratio. Each distinct
    // failure is logged once per game, up to 64 distinct failures in a game. A line that Verse or Unity will not write is held when it happens, up to 64
    // held lines. A game tick and a cooling net tick each write the held lines in order while the log will write, and each stops at the first line that the
    // log will not write. A held line past that limit is dropped and counted. A count line says how many, and it clears the dropped keys, so a key that
    // happens again is logged after it, up to the cap of its book.
    // A builder that throws writes one fixed-text line for its key, and a log check that
    // throws writes one fixed-text line for the process, both through UnityEngine.Debug. A column whose name cannot be read is named by its cell and map.
    // No exception leaves the prefix, and the original Tick then runs.
    [HarmonyPatch]
    public static class Patch_CoolingNet
    {
        // The failure lines logged in this game, by key. A key names the map, the site, the exception type, the message, and the top frame's method and
        // IL offset. A key names no net, so a net whose members change keeps its key. A different key logs again, up to MaxFailureKeys keys in a game.
        private static readonly KeyBook failures = new KeyBook(MaxFailureKeys,
            "Z-Levels cooling has logged " + MaxFailureKeys + " distinct failures in this game. Further distinct failures are not logged in this game.");

        // The no-net notices logged in this game, by key. They are kept apart from the failures, so a notice never uses a failure slot.
        private static readonly KeyBook notices = new KeyBook(MaxNoticeKeys,
            "Z-Levels cooling has logged " + MaxNoticeKeys + " notices in this game. Further notices are not logged in this game.");

        // The unknown-loop warnings logged in this game, by key. They have their own book, so a warning never uses a failure slot or a notice slot.
        private static readonly KeyBook unusable = new KeyBook(MaxUnusableKeys,
            "Z-Levels cooling has logged " + MaxUnusableKeys + " unusable-column warnings in this game. Further ones are not logged in this game.");

        // Every book, so that a game change can clear all of them.
        private static readonly KeyBook[] allBooks = { failures, notices, unusable };

        // The most distinct keys one game logs in each book. The first key past the cap writes one line that says so, and nothing else from that book.
        private const int MaxFailureKeys = 64;
        private const int MaxNoticeKeys = 64;
        private const int MaxUnusableKeys = 64;

        // The most lines held while the log will not write. A line past this is dropped and counted.
        private const int MaxHeldLines = 64;

        // The most distinct dropped keys one book keeps. Past this, the count line says "or more", and the keys past it are not recorded.
        private const int MaxDroppedKeys = 1024;

        // A column with no pipe net is reported only once this many ticks have passed since it spawned, because joins start after the next pipe grid rebuild.
        private const int NoNetGraceTicks = 600;

        // The getter of Verse's private PreventLogging flag. It is found once by reflection and kept. Null when the lookup failed, and then lines go through Unity.
        private static Func<bool> preventLoggingGetter;
        private static bool preventLoggingLookedUp;

        // Set once the first log check throws, so that one fixed-text line through Unity says so for the process.
        private static bool checkFailureReported;

        // The keys of one game, with the cap for that book. Keys holds the marked keys, which are written. Dropped holds the keys whose line was not kept,
        // because the held lines were full. The count line clears Dropped. CapWanted is set when a key past the cap was tried, and CapReported keeps the cap
        // line to one.
        private sealed class KeyBook
        {
            public readonly HashSet<string> Keys = new HashSet<string>();
            public readonly HashSet<string> Dropped = new HashSet<string>();
            public readonly int Max;
            public readonly string CapText;
            public bool CapWanted;
            public bool CapReported;

            // Set when the cap was wanted in an earlier game and is reported in this one. Its line then says so.
            public bool CapCarried;

            public KeyBook(int max, string capText)
            {
                Max = max;
                CapText = capText;
            }

            public void Clear()
            {
                Keys.Clear();
                Dropped.Clear();
                CapWanted = false;
                CapReported = false;
                CapCarried = false;
            }

            // A new game starts with no keys. A cap that is wanted and not yet reported stays wanted, so the next flush that can
            // write reports it. A cap that was already reported is not carried, because each game reports its cap once.
            public void ClearForNewGame()
            {
                bool pendingCap = CapWanted && !CapReported;
                Clear();
                CapWanted = pendingCap;
                CapCarried = pendingCap;
            }
        }

        // A line that the log would not write when it happened. Its text was built at that time, so a later change to an object does not alter it.
        private sealed class HeldLine
        {
            public KeyBook Book;
            public string Key;
            public string Text;
            public bool Warning;
        }

        // The held lines, in the order they happened. The list holds at most MaxHeldLines lines.
        private static readonly List<HeldLine> held = new List<HeldLine>();

        // The lines not kept since the last count line, and whether a dropped set has overflowed. The next flush that can write reports them.
        private static int droppedSinceReport;
        private static bool droppedOverflow;

        // The game that the books belong to. The reference is weak, so a game that is over can be collected while the main menu is open. A change from an
        // earlier game clears the books.
        private static System.WeakReference<Verse.Game> loggedGame;

        // The game that the last tick failure was reported for, so that one line is written for each game. The reference is weak.
        private static System.WeakReference<Verse.Game> tickFailureGame;

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
            Guard("write the held lines", FlushHeld);
            try
            {
                Merge(__instance);
            }
            catch (Exception exception)
            {
                Guard("report the merge failure", () => ReportMergeFailure(__instance, exception));
            }
        }

        // Logs a column whose pipe comp is missing. It runs at spawn, so it runs the game check first, and then it goes through the same gate as the other lines.
        // The key is the column's def and map, so one broken def logs once on each map, and the line names the first such column that it logs.
        internal static void LogMissingPipe(CompCoolingColumn column)
        {
            Guard("forget earlier games", ForgetFailuresOfEarlierGames);
            Guard("log a column with no pipe", () =>
            {
                string key = Helper("column key", () => DefAndMapKey(column), "unreadable column");
                LogOnce(failures, "no pipe|" + key,
                    () => "Z-Levels cooling column " + NameOrPlace(column) + " has no Rimatomics pipe comp, so it does not join a loop."
                        + " Further columns of this definition on this map are not logged again in this game.", false);
            });
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
                    // so the getter must not run here, and an empty field is skipped. A notice follows once the grace period has passed.
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
        // listed. A thing that is not a column, or a column whose pipe field is empty, is skipped without a line. CompCoolingColumn logs an empty field at spawn.
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

        // The key of the map that the net is on. The net's own identity is left out, so a net whose members change keeps its key. A net with no map is keyed
        // as "no map".
        private static string MapKey(Rimatomics.CoolingNet net)
        {
            ThingWithComps first = FirstThing(net);
            if (first == null || first.Map == null) return "no map";
            return "map " + first.Map.uniqueID;
        }

        // The def, cell, and map of a column, for a message. A column with no map is named by its def and cell, and the name says that it has no map.
        private static string ColumnName(CompCoolingColumn column)
        {
            ThingWithComps thing = column.parent;
            if (thing == null) return "a cooling column with no place";
            if (thing.Map == null) return thing.def.defName + " at " + thing.Position + " with no map";
            return thing.def.defName + " at " + thing.Position + " on map " + thing.Map.uniqueID;
        }

        // The key of a column. A column with a place is keyed by its map, def, and cell. Any other column is keyed by its number.
        private static string ColumnKey(CompCoolingColumn column)
        {
            ThingWithComps thing = column.parent;
            if (thing == null || thing.Map == null) return "column " + IdentityOf(column);
            return "map " + thing.Map.uniqueID + ", " + thing.def.defName + " at " + thing.Position;
        }

        // The key of a column's def and map. Every column of one def on one map shares it, so one broken def uses one slot on each map. A column with no map is
        // keyed by its def, and a column with no place shares one key.
        private static string DefAndMapKey(CompCoolingColumn column)
        {
            ThingWithComps thing = column.parent;
            if (thing == null) return "no place";
            if (thing.Map == null) return "no map, " + thing.def.defName;
            return "map " + thing.Map.uniqueID + ", " + thing.def.defName;
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
                string where = Helper("key of the net", () => MapKey(net), "map unknown");
                string frame = Helper("top frame", () => TopFrame(failure), "frame unavailable");
                LogOnce(failures, "step " + name + "|" + where + "|" + DescribeException(failure) + "|" + frame,
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
            string where = Helper("key of the net", () => MapKey(net), "map unknown");
            string frame = Helper("top frame", () => TopFrame(exception), "frame unavailable");
            LogOnce(failures, where + "|" + DescribeException(exception) + "|" + frame,
                () => "Z-Levels cooling loop merge failed on the net of " + described
                    + (ownThings ? ", so the net uses its own coolers and turbines: "
                        : ", and the net keeps its last lists, which may still hold joined coolers: ") + ExceptionText(exception), false);
        }

        // Logs one line for a key, once per game in its book. A key that is marked or held is skipped, and a dropped key is skipped until a count line clears it.
        // A key past the cap of its book sets CapWanted, and the cap line is written once when the log will write. A line that the log will not write is held,
        // or dropped and counted when the
        // held lines are full. A line that the log will write is built, marked, and then written. A builder that throws writes one fixed-text line for its key.
        private static void LogOnce(KeyBook book, string key, Func<string> line, bool warning)
        {
            if (book.Keys.Contains(key) || book.Dropped.Contains(key) || IsHeld(book, key)) return;
            if (book.Keys.Count >= book.Max)
            {
                book.CapWanted = true;
                ReportCapIfWanted(book);
                return;
            }
            if (!WillWrite())
            {
                Hold(book, key, line, warning);
                return;
            }
            string text;
            try
            {
                text = line();
            }
            catch (Exception buildFailure)
            {
                ReportBuildFailure(book, key, buildFailure);
                return;
            }
            // The key is marked before the write, so a logger that throws is never asked for the same line again.
            book.Keys.Add(key);
            Write(text, warning);
        }

        // Builds the text of a line that the log will not write now, and holds it until the log will write. When the held lines are full, the key is dropped.
        private static void Hold(KeyBook book, string key, Func<string> line, bool warning)
        {
            if (held.Count >= MaxHeldLines)
            {
                Drop(book, key);
                return;
            }
            string text;
            try
            {
                text = line();
            }
            catch (Exception buildFailure)
            {
                ReportBuildFailure(book, key, buildFailure);
                return;
            }
            held.Add(new HeldLine { Book = book, Key = key, Text = text, Warning = warning });
        }

        // A key whose line could not be held is skipped until a count line clears it, and it is counted once for that count line.
        private static void Drop(KeyBook book, string key)
        {
            if (book.Dropped.Count >= MaxDroppedKeys)
            {
                droppedOverflow = true;
                return;
            }
            if (book.Dropped.Add(key)) droppedSinceReport++;
        }

        // Whether a line for this key is already held.
        private static bool IsHeld(KeyBook book, string key)
        {
            foreach (HeldLine line in held)
            {
                if (ReferenceEquals(line.Book, book) && line.Key == key) return true;
            }
            return false;
        }

        // A builder that throws writes one fixed-text line through Unity, with the exception type. The key is marked only when Unity is on. With Unity off, the
        // line is not written, so the next tick builds it again. The key is marked before the Unity write. That write is the last channel, and a throw from it
        // is dropped inside UnityWrite with no channel left to retry through. The key stays marked, so the line is not built again.
        private static void ReportBuildFailure(KeyBook book, string key, Exception buildFailure)
        {
            if (!UnityLoggerOn()) return;
            book.Keys.Add(key);
            UnityWrite("Z-Levels cooling could not build a log line, so the line is skipped. Its builder threw " + TypeNameOf(buildFailure) + ".", true);
        }

        // Whether the log will write a line now. Unity drops every line while its logger is off. Verse drops a line while PreventLogging is true, which is while
        // a LockMessages lock is held and after the 10,000th message. When the flag cannot be read, lines go through Unity, so only the Unity check applies.
        // A check that throws writes one fixed-text line through Unity, and returns true, so the write is attempted and Write guards it.
        private static bool WillWrite()
        {
            if (!UnityLoggerOn()) return false;
            try
            {
                Func<bool> prevent = PreventLoggingGetter();
                return prevent == null || !prevent();
            }
            catch (Exception checkFailure)
            {
                ReportCheckFailureOnce(checkFailure);
                return true;
            }
        }

        // Whether Unity's logger is on. A read that throws is reported once, and counts as on, so the write is attempted.
        private static bool UnityLoggerOn()
        {
            try
            {
                return UnityEngine.Debug.unityLogger.logEnabled;
            }
            catch (Exception checkFailure)
            {
                ReportCheckFailureOnce(checkFailure);
                return true;
            }
        }

        // The first check that throws writes one fixed-text line through Unity, with the exception type. Later checks write nothing.
        private static void ReportCheckFailureOnce(Exception checkFailure)
        {
            if (checkFailureReported) return;
            checkFailureReported = true;
            UnityWrite("Z-Levels cooling could not check whether the log will write (" + TypeNameOf(checkFailure) + "), so it tries the write.", true);
        }

        // The cap line for a cap that was wanted in an earlier game. The books of the new game are empty, so this line does not say
        // "in this game".
        private const string CarriedCapText = "Z-Levels cooling reached the limit of 64 distinct lines in an earlier game, "
            + "so further lines from that game were not logged.";

        // Writes a book's cap line once, when a key past the cap was tried and the log will write. A cap carried from an earlier game
        // has its own line.
        private static void ReportCapIfWanted(KeyBook book)
        {
            if (!book.CapWanted || book.CapReported) return;
            if (!WillWrite()) return;
            book.CapReported = true;
            Write(book.CapCarried ? CarriedCapText : book.CapText, true);
        }

        // Runs after each DoSingleTick call, from the postfix of GameComponentUtility.GameComponentTick (CoolingLogTickPatch). It writes the held lines even when no
        // cooling net ticks. With nothing pending it returns first, before the game check, and it reads no log flag, so the common tick costs a few field reads.
        internal static void FlushOnGameTick()
        {
            if (NothingPending()) return;
            Guard("forget earlier games", ForgetFailuresOfEarlierGames);
            Guard("write the held lines", FlushHeld);
        }

        // Whether a flush has anything to write: a held line, a count line, or a cap line.
        private static bool NothingPending()
        {
            if (held.Count > 0 || droppedSinceReport > 0) return false;
            foreach (KeyBook book in allBooks)
            {
                if (book.CapWanted && !book.CapReported) return false;
            }
            return true;
        }

        // Writes the held lines in the order they happened, then the cap lines and the count line. Each line is checked on its own. The first line that the log
        // will not write stops the flush, and the rest stay held. Only the lines written are marked. The cap lines and the count line are written only when the
        // log will write. The count line clears the dropped keys, so a key that happens again is logged after it.
        private static void FlushHeld()
        {
            while (held.Count > 0)
            {
                HeldLine line = held[0];
                if (line.Book.Keys.Contains(line.Key))
                {
                    held.RemoveAt(0);
                    continue;
                }
                if (line.Book.Keys.Count >= line.Book.Max)
                {
                    line.Book.CapWanted = true;
                    held.RemoveAt(0);
                    continue;
                }
                if (!WillWrite()) return;
                held.RemoveAt(0);
                line.Book.Keys.Add(line.Key);
                Write(line.Text, line.Warning);
            }
            foreach (KeyBook book in allBooks)
            {
                if (!book.CapWanted || book.CapReported) continue;
                if (!WillWrite()) return;
                ReportCapIfWanted(book);
            }
            if (droppedSinceReport > 0)
            {
                if (!WillWrite()) return;
                Write(DroppedText(), true);
                droppedSinceReport = 0;
                droppedOverflow = false;
                foreach (KeyBook book in allBooks) book.Dropped.Clear();
            }
        }

        // The count line. It says how many lines were not kept, and that each one is logged if it happens again.
        private static string DroppedText()
        {
            return "Z-Levels cooling did not keep " + droppedSinceReport + (droppedOverflow ? " or more" : "")
                + " log lines that happened while the log was locked or silenced. Each of those lines is logged if it happens again.";
        }

        // The getter of Verse's PreventLogging flag. The lookup runs once. When it fails, one notice goes out through Unity, and the getter stays null.
        private static Func<bool> PreventLoggingGetter()
        {
            if (preventLoggingLookedUp) return preventLoggingGetter;
            preventLoggingLookedUp = true;
            try
            {
                PropertyInfo property = typeof(Log).GetProperty("PreventLogging", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                MethodInfo getter = property == null ? null : property.GetGetMethod(true);
                if (getter == null) throw new InvalidOperationException("Verse.Log has no PreventLogging getter.");
                preventLoggingGetter = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
            }
            catch (Exception lookupFailure)
            {
                UnityWrite("Z-Levels cooling could not read Verse's log flag PreventLogging (" + TypeNameOf(lookupFailure)
                    + "). Its lines go out through UnityEngine.Debug.", true);
            }
            return preventLoggingGetter;
        }

        // Writes one line through the Verse logger. When the flag cannot be read, the line goes through Unity. When the Verse call throws, the text goes out
        // once through UnityEngine.Debug.LogError with the exception type. If that call throws too, nothing is written.
        private static void Write(string text, bool warning)
        {
            if (PreventLoggingGetter() == null)
            {
                UnityWrite(text, warning);
                return;
            }
            try
            {
                if (warning) Log.Warning(text);
                else Log.Error(text);
            }
            catch (Exception loggerFailure)
            {
                UnityWrite(text + " Logger threw " + TypeNameOf(loggerFailure) + ".", false);
            }
        }

        // Writes one line through UnityEngine.Debug. A throw from Unity is dropped, because no channel is left after it.
        private static void UnityWrite(string text, bool warning)
        {
            try
            {
                if (warning) UnityEngine.Debug.LogWarning(text);
                else UnityEngine.Debug.LogError(text);
            }
            catch (Exception)
            {
                // Unity logging threw too, so no channel is left. The key stays marked, so the line is not tried again.
            }
        }

        private static void LogHelperFailureOnce(string name, Exception failure)
        {
            LogOnce(failures, "helper " + name + "|" + DescribeException(failure),
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

        // The game check. A game other than the one the books belong to is a change, and the books and the held lines are forgotten. The reference is weak,
        // so a game that is over can be collected. The first game of the process has no earlier game, so it clears nothing, and a line held before the first
        // check is kept. A change from an earlier game counts the held lines as not kept. The count and the overflow flag describe the same lines, so they carry
        // over together, and the next flush that can write reports them. The dropped keys are cleared, because keys belong to one game.
        // A cap that is wanted and not yet reported carries over the same way. A cap that was already reported does not, because each
        // game reports its cap once.
        private static void ForgetFailuresOfEarlierGames()
        {
            Verse.Game game = Current.Game;
            Verse.Game logged;
            if (loggedGame != null && loggedGame.TryGetTarget(out logged) && ReferenceEquals(logged, game)) return;
            if (loggedGame != null)
            {
                droppedSinceReport += held.Count;
                held.Clear();
                foreach (KeyBook book in allBooks) book.ClearForNewGame();
            }
            loggedGame = game == null ? null : new System.WeakReference<Verse.Game>(game);
        }

        // The name of a column for a line. When the name read fails, the cell and the map name the column instead, and each read is guarded.
        private static string NameOrPlace(CompCoolingColumn column)
        {
            string name = Helper("column name", () => ColumnName(column), (string)null);
            if (name != null) return name;
            string cell = Helper("column cell", () => column.parent.Position.ToString(), "a cell that could not be read");
            string place = Helper("column map", () => column.parent.Map == null ? " with no map" : " on map " + column.parent.Map.uniqueID,
                " on a map that could not be read");
            return "at cell " + cell + place;
        }

        // An unusable column forms no loop, so its loop is dropped. The other loops of its net still join. The warning has its own book.
        private static void LogUnknownLoopOnce(CompCoolingColumn start)
        {
            string key = Helper("column key", () => ColumnKey(start), "column " + IdentityOf(start));
            LogOnce(unusable, "unknown loop|" + key,
                () => "Z-Levels cooling column " + NameOrPlace(start) + " is not usable, so its loop is dropped. The other loops of its net still join."
                    + " This is logged once for each column in each game.", true);
        }

        // A column with no pipe net is not a failure. The merge skips it until it joins, and joins start after the next pipe grid rebuild. The notice is written
        // only when the column still has no net NoNetGraceTicks after it spawned. It has its own key set and cap.
        private static void LogNoNetOnce(CompCoolingColumn column)
        {
            if (!PastNoNetGrace(column)) return;
            string key = Helper("column key", () => ColumnKey(column), "column " + IdentityOf(column));
            LogOnce(notices, "no net|" + key,
                () => "Z-Levels cooling column " + NameOrPlace(column) + " has no pipe net in its field " + NoNetGraceTicks
                    + " ticks after it spawned, so it does not join a loop yet. Joins start after the next pipe grid rebuild."
                    + " This is logged once for each column in each game.", true);
        }

        // Whether the column spawned at least NoNetGraceTicks ago. A column with no readable spawn tick is not past the grace period. A failed read logs once.
        private static bool PastNoNetGrace(CompCoolingColumn column)
        {
            return Helper("grace period", () => column.SpawnedAtTick.HasValue
                && Find.TickManager.TicksGame - column.SpawnedAtTick.Value >= NoNetGraceTicks, false);
        }

        // Reports a throw from the game tick hook with one fixed-text line through Unity. The line is written once per game. Later ticks
        // of the same game stay silent. The game is marked before the write, so a write that throws is not tried again on the next tick.
        internal static void ReportTickFailure(Exception failure)
        {
            Verse.Game game = Current.Game;
            Verse.Game reported;
            if (tickFailureGame != null && tickFailureGame.TryGetTarget(out reported) && ReferenceEquals(reported, game)) return;
            tickFailureGame = game == null ? null : new System.WeakReference<Verse.Game>(game);
            UnityWrite("Z-Levels cooling game tick hook threw " + TypeNameOf(failure)
                + ". Held log lines may be delayed. Later ticks in this game do not log this again.", true);
        }
    }

    // A postfix on the static GameComponentUtility.GameComponentTick. RimWorld calls that method once for each DoSingleTick call, so the
    // postfix runs once for each call. Under DebugSettings.fastEcology, one call advances the tick counter by 2000, and the postfix still
    // runs once for that call. The postfix flushes the held cooling log lines, so a held line does not wait for a cooling net to tick.
    // PatchAll applies it only when a Rimatomics mod is active, through RimatomicsBridge. This class names no Rimatomics type.
    [HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.GameComponentTick))]
    public static class CoolingLogTickPatch
    {
        // The postfix runs inside TickManager.DoSingleTick, outside any try block, so nothing may escape it. A throw is reported once
        // per game, and the tick goes on.
        static void Postfix()
        {
            try
            {
                Patch_CoolingNet.FlushOnGameTick();
            }
            catch (Exception failure)
            {
                try
                {
                    Patch_CoolingNet.ReportTickFailure(failure);
                }
                catch (Exception)
                {
                    // Last resort. If UnityWrite throws, or the report throws for any other reason, no channel is left. The throw is
                    // dropped here, so that it cannot reach the tick. UnityWrite catches its own throws, so this is not expected to run.
                }
            }
        }
    }
}
