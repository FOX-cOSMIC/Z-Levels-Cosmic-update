using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    // Q3 (review rounds 5 to 7 of the multifloors-ideas branch). A work search that throws must not leave its floor state behind. The search
    // sets the floor it looks into (ZUtils.currentLookedIntoMap) and the job's floor (JobTracker.mapDest). While a colonist's tracker is the
    // one being filled, the reachability patch files every cell built from then on under that floor, and answers the colonist's reach to that
    // cell on that floor. The scenario makes the search throw, then checks what the search left behind. A normal search that finds nothing
    // must leave the floor field on the own floor as well (decision D13). A paused search that throws must give no job, and must not let the
    // vanilla search run again without the paused search's limits (review finding R1).
    //
    // The harness adds its own Harmony prefix to JobGiver_Work.TryIssueJobPackage, the vanilla search the own-floor search calls once per
    // floor. While it is armed for the scenario's colonist and floor, it files one cell, as the vanilla scan does when it builds a target from
    // a cell, and throws. The scenario first calls OwnFloorFirstSearch.Search and SearchOwnFloorOnly itself, after the same setup the
    // Z-Levels prefix does, and catches the exception. Then it runs the same failures through the Z-Levels prefix, as in play. The prefix logs
    // the exception as an error. A second harness prefix, on Verse.Log.Error, keeps an error out of the log only while it is armed, and only
    // when the error's text carries this scenario's own exception message. It counts each one it keeps, and the rows check the count. Every
    // other error reaches the log, and the log gate fails the run on it.
    public partial class RuntimeValidation
    {
        private const string SearchThrowMessage = "Z-Levels search throw test: the work search failed on its floor";

        // The harness prefix on the vanilla search, and what it saw. Armed only around the calls of the scenario.
        internal static class SearchThrowProbe
        {
            internal static Pawn ArmedPawn;
            internal static Map ArmedMap;
            internal static IntVec3 FiledCell = IntVec3.Invalid;
            // When set, the probe moves the floor field to this floor before it throws. That stands for a step inside the search that left the
            // field on another floor, so a row can tell whether the search puts the field back when it fails.
            internal static Map MoveFieldTo;
            internal static FieldInfo LookedIntoField;
            internal static FieldInfo VanillaCallRunningField;
            internal static Map ThrewOn;
            internal static int Throws;
            // Outer calls of the armed colonist whose vanilla body ran, because the Z-Levels prefix let it run.
            internal static int OuterOriginalRuns;

            private static bool VanillaCallRunning => (bool)VanillaCallRunningField.GetValue(null);

            // Only the vanilla search that the own-floor search calls is made to fail. The outer call, which the Z-Levels prefix takes over, is
            // left alone. The probe runs before the Z-Levels prefix, so on the outer call it would otherwise throw before that prefix runs.
            public static void Prefix(Pawn pawn)
            {
                if (ArmedPawn == null || pawn != ArmedPawn || pawn.Map != ArmedMap || !VanillaCallRunning)
                {
                    return;
                }
                if (FiledCell.IsValid)
                {
                    // The vanilla scan builds a target from each cell it checks. The Z-Levels constructor patch files the cell under the floor the
                    // search looks into.
                    _ = new LocalTargetInfo(FiledCell);
                }
                if (MoveFieldTo != null)
                {
                    LookedIntoField.SetValue(null, MoveFieldTo);
                }
                ThrewOn = pawn.Map;
                Throws++;
                throw new InvalidOperationException(SearchThrowMessage);
            }

            // Harmony skips the postfix when the call throws, so it sees the calls that returned. On an outer call, __runOriginal tells whether the
            // vanilla body ran after the Z-Levels prefix.
            public static void Postfix(Pawn pawn, bool __runOriginal)
            {
                if (ArmedPawn != null && pawn == ArmedPawn && __runOriginal && !VanillaCallRunning)
                {
                    OuterOriginalRuns++;
                }
            }

            internal static void Arm(Pawn pawn, Map map, IntVec3 filedCell, Map moveFieldTo)
            {
                Throws = 0;
                OuterOriginalRuns = 0;
                ThrewOn = null;
                ArmedPawn = pawn;
                ArmedMap = map;
                FiledCell = filedCell;
                MoveFieldTo = moveFieldTo;
            }

            internal static void Disarm()
            {
                ArmedPawn = null;
                ArmedMap = null;
                FiledCell = IntVec3.Invalid;
                MoveFieldTo = null;
            }
        }

        // The harness prefix on Verse.Log.Error. While armed, an error whose text carries SearchThrowMessage is kept here and not written to the
        // log. Every other error is written as usual.
        internal static class SearchThrowLogProbe
        {
            internal static bool Armed;
            internal static readonly List<string> Kept = new List<string>();

            public static bool Prefix(string text)
            {
                if (!Armed || text == null || !text.Contains(SearchThrowMessage))
                {
                    return true;
                }
                Kept.Add(text);
                return false;
            }
        }

        private void RunSearchThrowStateCheck()
        {
            const string label = "search throw state";
            Harmony harmony = new Harmony("zlevels.validation.searchthrow");
            MethodInfo vanillaSearch = AccessTools.Method(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage));
            MethodInfo logError = AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) });
            bool patched = false;
            try
            {
                Type searchType = AccessTools.TypeByName("ZLevels.OwnFloorFirstSearch") ?? throw new InvalidOperationException("ZLevels.OwnFloorFirstSearch is missing");
                MethodInfo search = AccessTools.Method(searchType, "Search") ?? throw new InvalidOperationException("OwnFloorFirstSearch.Search is missing");
                MethodInfo searchOwnFloorOnly = AccessTools.Method(searchType, "SearchOwnFloorOnly") ?? throw new InvalidOperationException("OwnFloorFirstSearch.SearchOwnFloorOnly is missing");
                FieldInfo vanillaCallRunning = AccessTools.Field(searchType, "VanillaCallRunning") ?? throw new InvalidOperationException("OwnFloorFirstSearch.VanillaCallRunning is missing");
                FieldInfo giverFilter = AccessTools.Field(searchType, "GiverFilter") ?? throw new InvalidOperationException("OwnFloorFirstSearch.GiverFilter is missing");
                FieldInfo ownFloorOnlyPawn = AccessTools.Field(searchType, "OwnFloorOnlyPawn") ?? throw new InvalidOperationException("OwnFloorFirstSearch.OwnFloorOnlyPawn is missing");
                FieldInfo lookedIntoField = AccessTools.Field(AccessTools.TypeByName("ZLevels.ZUtils"), "currentLookedIntoMap") ?? throw new InvalidOperationException("ZUtils.currentLookedIntoMap is missing");
                if (logError == null)
                {
                    throw new InvalidOperationException("Verse.Log.Error(string) is missing");
                }
                SearchThrowProbe.LookedIntoField = lookedIntoField;
                SearchThrowProbe.VanillaCallRunningField = vanillaCallRunning;
                Func<Map> lookedInto = () => (Map)lookedIntoField.GetValue(null);

                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                lower.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 2);
                Func<Map, string> describeFloor = map => map == surface ? "the own floor" : map == lower ? "the lower floor" : map?.ToString() ?? "null";

                // One colonist beside the stairs, with all work off. The own-floor passes find nothing, so the search goes on to the lower floor.
                Pawn colonist = SpawnColonistNear(surface, stairs.Position, new List<IntVec3>());
                colonist.workSettings.DisableAll();
                JobGiver_Work giver = FindNormalWorkNode(colonist.thinker.MainThinkNodeRoot) ?? throw new InvalidOperationException("the colonist's think tree has no normal JobGiver_Work");

                // A surface cell the colonist reaches on the surface, which is not walkable on the lower floor. A reach to it that is answered
                // on the lower floor is refused. The plain checks pass no pawn, so the Z-Levels reachability patch leaves them alone.
                IntVec3 cell = GenRadial.RadialCellsAround(colonist.Position, 15f, false)
                    .Where(c => c.InBounds(surface) && c.DistanceTo(colonist.Position) >= 3f && c.Standable(surface) && !c.Walkable(lower)
                        && surface.reachability.CanReach(colonist.Position, c, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if (!cell.IsValid)
                {
                    throw new InvalidOperationException("no surface cell within 15 of the colonist that it reaches and that is solid on the lower floor");
                }
                Record("search throw cell", "INFO: colonist at " + colonist.Position + ", cell " + cell + ", stairs at " + stairs.Position);
                bool baseline = colonist.CanReach(cell, PathEndMode.OnCell, Danger.Deadly);
                Record("the colonist reaches the cell before any search", baseline ? "PASS" : "FAIL: the colonist's reach to " + cell + " is refused before any search");

                harmony.Patch(vanillaSearch, prefix: new HarmonyMethod(typeof(SearchThrowProbe), nameof(SearchThrowProbe.Prefix)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(SearchThrowProbe), nameof(SearchThrowProbe.Postfix)));
                harmony.Patch(logError, prefix: new HarmonyMethod(typeof(SearchThrowLogProbe), nameof(SearchThrowLogProbe.Prefix)) { priority = Priority.First });
                patched = true;

                // Part 1. The full search, called directly, set up as the Z-Levels prefix sets it up (JobPatches.TryIssueJobPackagePatch): the tracker
                // is reset, which makes it the tracker that cells are filed into, and marked as searching.
                IntVec3 colonistCell = colonist.Position;
                tracker.ResetJobTrackerFor(colonist);
                JobTracker jobTracker = tracker.jobTracker[colonist];
                jobTracker.searchingJobsNow = true;
                jobTracker.oldMap = surface;
                jobTracker.pawn = colonist;
                SearchThrowProbe.Arm(colonist, lower, cell, null);
                Exception fullThrown;
                try
                {
                    fullThrown = InvokeSearch(search, giver, false, colonist, new JobIssueParams(), jobTracker, surface, colonistCell);
                }
                finally
                {
                    SearchThrowProbe.Disarm();
                    jobTracker.searchingJobsNow = false;
                }

                bool failedBelow = IsSearchThrow(fullThrown) && SearchThrowProbe.ThrewOn == lower && SearchThrowProbe.Throws == 1;
                Record("the work search fails on another floor", failedBelow ? "PASS" : "FAIL: " + DescribeThrow(fullThrown) + ", threw on " + (SearchThrowProbe.ThrewOn?.ToString() ?? "no floor") + ", " + SearchThrowProbe.Throws + " throws");
                bool back = colonist.Map == surface && colonist.Position == colonistCell;
                Record("colonist is back on its own floor and cell", back ? "PASS" : "FAIL: the colonist is at " + colonist.Position + " on " + colonist.Map);
                bool mapDestCleared = jobTracker.mapDest == null;
                Record("the job's floor is cleared after a failed search", mapDestCleared ? "PASS" : "FAIL: the tracker's job floor is still " + jobTracker.mapDest);
                Map lookedAfter = lookedInto();
                bool lookedOwn = lookedAfter == surface;
                Record("the floor field is the colonist's own floor after a failed search", lookedOwn ? "PASS" : "FAIL: the floor field is " + describeFloor(lookedAfter));
                List<IntVec3> filedBelow = FiledUnder(jobTracker, lower);
                bool noneFiledBelow = filedBelow.Count == 0;
                Record("no cell stays filed under the floor where the search failed", noneFiledBelow ? "PASS" : "FAIL: " + filedBelow.Count + " cells are filed under the lower floor, among them " + string.Join(", ", filedBelow.Take(3)));
                // The reach check builds a target from the cell, which files the cell again under the floor field, and then reads it.
                bool reachAfter = colonist.CanReach(cell, PathEndMode.OnCell, Danger.Deadly);
                Record("the colonist still reaches the cell on its own floor after a failed search", reachAfter ? "PASS" : "FAIL: the colonist's reach to " + cell + " is refused; it is filed under " + DescribeFiled(jobTracker, cell));

                // Part 2. The paused search, called directly. It looks into the own floor only. The job's floor starts empty, and the probe moves the
                // floor field to the lower floor before it throws.
                jobTracker.mapDest = null;
                SearchThrowProbe.Arm(colonist, surface, IntVec3.Invalid, lower);
                Exception ownThrown;
                try
                {
                    ownThrown = InvokeSearch(searchOwnFloorOnly, giver, colonist, new JobIssueParams(), jobTracker);
                }
                finally
                {
                    SearchThrowProbe.Disarm();
                }
                bool failedOwn = IsSearchThrow(ownThrown) && SearchThrowProbe.ThrewOn == surface && SearchThrowProbe.Throws == 1;
                Record("the own-floor search fails on the own floor", failedOwn ? "PASS" : "FAIL: " + DescribeThrow(ownThrown) + ", threw on " + (SearchThrowProbe.ThrewOn?.ToString() ?? "no floor") + ", " + SearchThrowProbe.Throws + " throws");
                bool ownMapDestCleared = jobTracker.mapDest == null;
                Record("the job's floor is cleared after a failed own-floor search", ownMapDestCleared ? "PASS" : "FAIL: the tracker's job floor is still " + jobTracker.mapDest);
                Map lookedAfterOwn = lookedInto();
                bool lookedOwnAfterOwn = lookedAfterOwn == surface;
                Record("the floor field is the colonist's own floor after a failed own-floor search", lookedOwnAfterOwn ? "PASS" : "FAIL: the floor field is " + describeFloor(lookedAfterOwn));
                bool marksCleared = !(bool)vanillaCallRunning.GetValue(null) && giverFilter.GetValue(null) == null && ownFloorOnlyPawn.GetValue(null) == null;
                Record("the search marks are cleared after both failed searches", marksCleared ? "PASS" : "FAIL: vanilla call running " + vanillaCallRunning.GetValue(null) + ", giver filter " + (giverFilter.GetValue(null) == null ? "none" : "set") + ", own-floor colonist " + (ownFloorOnlyPawn.GetValue(null) ?? "none"));

                // Part 3. The failed full search through the Z-Levels prefix, as in play (review finding R3). The idle pause is off, because no search
                // has found nothing yet. The prefix resets the tracker, catches the exception, and logs it.
                bool idleAllowed = tracker.floorPolicy.IdleSearchAllowed(colonist, Find.TickManager.TicksGame);
                Record("the idle pause is off before the search through the prefix", idleAllowed ? "PASS" : "FAIL: the idle pause is on");
                PrefixCall prefixFull = CallThroughPrefix(colonist, lower, cell, null);
                bool prefixFullOk = prefixFull.Escaped == null && prefixFull.Job == null && SearchThrowProbe.Throws == 1 && SearchThrowProbe.OuterOriginalRuns == 0 && SearchThrowLogProbe.Kept.Count == 1;
                Record("through the prefix, the failed search gives no job and logs one error", prefixFullOk ? "PASS" : "FAIL: " + prefixFull.Describe());
                Record("error logged by the prefix", "INFO: " + FirstLine(SearchThrowLogProbe.Kept));
                Map lookedAfterPrefix = lookedInto();
                List<IntVec3> filedBelowPrefix = FiledUnder(jobTracker, lower);
                bool prefixStateOk = jobTracker.mapDest == null && lookedAfterPrefix == surface && filedBelowPrefix.Count == 0;
                Record("through the prefix, the failed search leaves no floor state", prefixStateOk ? "PASS" : "FAIL: job floor " + (jobTracker.mapDest?.ToString() ?? "none") + ", floor field " + describeFloor(lookedAfterPrefix) + ", " + filedBelowPrefix.Count + " cells filed under the lower floor");
                bool reachAfterPrefix = colonist.CanReach(cell, PathEndMode.OnCell, Danger.Deadly);
                Record("through the prefix, the colonist still reaches the cell on its own floor after a failed search", reachAfterPrefix ? "PASS" : "FAIL: the colonist's reach to " + cell + " is refused; it is filed under " + DescribeFiled(jobTracker, cell));

                // Part 4. A normal search that finds nothing, through the Z-Levels prefix, with nothing thrown (decision D13, 2026-11-10). It looks into
                // the lower floor last, and must leave the floor field on the own floor too. The tracker stays the one being filled after this search.
                int otherFloorRunsBefore = FloorPolicy.OtherFloorSearchRuns;
                Job emptyJob = SearchWorkJob(colonist);
                int otherFloorRuns = FloorPolicy.OtherFloorSearchRuns - otherFloorRunsBefore;
                bool emptyBelow = emptyJob == null && otherFloorRuns >= 1;
                Record("a search that finds nothing looks into the lower floor", emptyBelow ? "PASS: " + otherFloorRuns + " searches on other floors" : "FAIL: job " + (emptyJob == null ? "none" : emptyJob.def.defName) + ", " + otherFloorRuns + " searches on other floors");
                Map lookedAfterEmpty = lookedInto();
                bool lookedOwnAfterEmpty = lookedAfterEmpty == surface;
                Record("the floor field is the colonist's own floor after a search that finds nothing", lookedOwnAfterEmpty ? "PASS" : "FAIL: the floor field is " + describeFloor(lookedAfterEmpty));
                bool reachAfterEmpty = colonist.CanReach(cell, PathEndMode.OnCell, Danger.Deadly);
                Record("the colonist still reaches the cell on its own floor after a search that finds nothing", reachAfterEmpty ? "PASS" : "FAIL: the colonist's reach to " + cell + " is refused; it is filed under " + DescribeFiled(jobTracker, cell));
                Record("tracker after a search that finds nothing", "INFO: the tracker being filled is " + (LocalTargetInfo_Constructor_Patch.curPawnJobTracker == jobTracker ? "this colonist's" : LocalTargetInfo_Constructor_Patch.curPawnJobTracker == null ? "none" : "another colonist's"));

                // Part 5. The paused search through the Z-Levels prefix (review finding R1). The empty search above started the idle pause, so the
                // prefix takes the paused path. The probe throws on the own floor, and moves the floor field to the lower floor first. A failed paused
                // search must give no job and must not let the vanilla body of the outer call run. That run has none of the paused search's limits.
                // The probe's postfix counts it.
                bool paused = !tracker.floorPolicy.IdleSearchAllowed(colonist, Find.TickManager.TicksGame);
                Record("the idle pause is on after the search that finds nothing", paused ? "PASS" : "FAIL: the idle pause is off, so the prefix does not take the paused path");
                jobTracker.mapDest = null;
                PrefixCall prefixPaused = CallThroughPrefix(colonist, surface, IntVec3.Invalid, lower);
                bool prefixPausedOk = prefixPaused.Escaped == null && prefixPaused.Job == null && SearchThrowProbe.Throws == 1 && SearchThrowProbe.OuterOriginalRuns == 0;
                Record("through the prefix, a failed paused search gives no job and runs no second search", prefixPausedOk ? "PASS" : "FAIL: " + prefixPaused.Describe());
                bool pausedLoggedOnce = SearchThrowLogProbe.Kept.Count == 1;
                Record("through the prefix, a failed paused search logs one error", pausedLoggedOnce ? "PASS" : "FAIL: " + SearchThrowLogProbe.Kept.Count + " errors with the probe's message");
                Record("error logged for the paused search", "INFO: " + FirstLine(SearchThrowLogProbe.Kept));
                Map lookedAfterPaused = lookedInto();
                bool pausedStateOk = jobTracker.mapDest == null && lookedAfterPaused == surface;
                Record("through the prefix, a failed paused search leaves no floor state", pausedStateOk ? "PASS" : "FAIL: job floor " + (jobTracker.mapDest?.ToString() ?? "none") + ", floor field " + describeFloor(lookedAfterPaused));

                bool all = baseline && failedBelow && back && mapDestCleared && lookedOwn && noneFiledBelow && reachAfter
                    && failedOwn && ownMapDestCleared && lookedOwnAfterOwn && marksCleared
                    && idleAllowed && prefixFullOk && prefixStateOk && reachAfterPrefix
                    && emptyBelow && lookedOwnAfterEmpty && reachAfterEmpty
                    && paused && prefixPausedOk && pausedLoggedOnce && pausedStateOk;
                Record(label, all ? "PASS" : "FAIL: a row above failed");
            }
            catch (Exception exception)
            {
                Record(label, "FAIL: " + exception);
                Log.Error("Z-Levels search throw state validation failed: " + exception);
            }
            finally
            {
                SearchThrowProbe.Disarm();
                SearchThrowLogProbe.Armed = false;
                if (patched)
                {
                    harmony.Unpatch(vanillaSearch, HarmonyPatchType.All, harmony.Id);
                    harmony.Unpatch(logError, HarmonyPatchType.All, harmony.Id);
                }
            }
            Root.Shutdown();
        }

        private struct PrefixCall
        {
            public Job Job;
            public Exception Escaped;

            public string Describe()
            {
                return "job " + (Job == null ? "none" : Job.def.defName) + ", " + (Escaped == null ? "nothing escaped the search" : "the search let " + Escaped.GetType().Name + " escape: " + Escaped.Message)
                    + ", " + SearchThrowProbe.Throws + " throws, " + SearchThrowProbe.OuterOriginalRuns + " runs of the vanilla body after the Z-Levels prefix, "
                    + SearchThrowLogProbe.Kept.Count + " errors with the probe's message";
            }
        }

        // One work search through the Z-Levels prefix, with the search probe armed for the colonist and floor, and the log probe armed. Both are
        // disarmed before it returns. An exception that escapes the search is returned, not thrown.
        private static PrefixCall CallThroughPrefix(Pawn colonist, Map throwOn, IntVec3 filedCell, Map moveFieldTo)
        {
            PrefixCall call = new PrefixCall();
            SearchThrowLogProbe.Kept.Clear();
            SearchThrowProbe.Arm(colonist, throwOn, filedCell, moveFieldTo);
            SearchThrowLogProbe.Armed = true;
            try
            {
                call.Job = SearchWorkJob(colonist);
            }
            catch (Exception escaped)
            {
                call.Escaped = escaped;
            }
            finally
            {
                SearchThrowLogProbe.Armed = false;
                SearchThrowProbe.Disarm();
            }
            return call;
        }

        // Calls the search and returns what it threw, or null. Reflection wraps the exception, so the inner one is returned.
        private static Exception InvokeSearch(MethodInfo method, params object[] arguments)
        {
            try
            {
                method.Invoke(null, arguments);
                return null;
            }
            catch (TargetInvocationException wrapped)
            {
                return wrapped.InnerException ?? wrapped;
            }
        }

        private static bool IsSearchThrow(Exception thrown)
        {
            return thrown is InvalidOperationException && thrown.Message == SearchThrowMessage;
        }

        private static string DescribeThrow(Exception thrown)
        {
            return thrown == null ? "the search did not throw" : "the search threw " + thrown.GetType().Name + ": " + thrown.Message;
        }

        private static List<IntVec3> FiledUnder(JobTracker jobTracker, Map map)
        {
            return jobTracker.lookedAtLocalCellMap == null ? new List<IntVec3>() : jobTracker.lookedAtLocalCellMap.Where(entry => entry.Value == map).Select(entry => entry.Key).ToList();
        }

        private static string DescribeFiled(JobTracker jobTracker, IntVec3 cell)
        {
            if (jobTracker.lookedAtLocalCellMap == null || !jobTracker.lookedAtLocalCellMap.TryGetValue(cell, out Map map))
            {
                return "no floor";
            }
            return map?.ToString() ?? "null";
        }

        private static string FirstLine(List<string> texts)
        {
            if (texts.Count == 0)
            {
                return "none";
            }
            string first = texts[0];
            int end = first.IndexOf('\n');
            return end < 0 ? first : first.Substring(0, end);
        }
    }
}
