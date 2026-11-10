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
    // cell on that floor. The scenario makes the search throw on the lower floor, then checks what the search left behind. Last, a normal
    // search that finds nothing must leave the floor field on the own floor as well (decision D13).
    //
    // The harness adds its own Harmony prefix to JobGiver_Work.TryIssueJobPackage, the vanilla search the own-floor search calls once per
    // floor. While it is armed for the scenario's colonist and floor, it files one cell, as the vanilla scan does when it builds a target from
    // a cell, and throws. The scenario calls OwnFloorFirstSearch.Search and SearchOwnFloorOnly itself, after the same setup the Z-Levels
    // prefix does, and catches the exception. Through the prefix, the exception would be logged as an error, as it is in play, and the log
    // gate fails a run on any error line.
    public partial class RuntimeValidation
    {
        private const string SearchThrowMessage = "Z-Levels search throw test: the work search failed on its floor";

        // The harness prefix and what it saw. Armed only between the calls of the scenario.
        internal static class SearchThrowProbe
        {
            internal static Pawn ArmedPawn;
            internal static Map ArmedMap;
            internal static IntVec3 FiledCell = IntVec3.Invalid;
            internal static Map ThrewOn;
            internal static int Throws;

            public static void Prefix(Pawn pawn)
            {
                if (ArmedPawn == null || pawn != ArmedPawn || pawn.Map != ArmedMap)
                {
                    return;
                }
                if (FiledCell.IsValid)
                {
                    // The vanilla scan builds a target from each cell it checks. The Z-Levels constructor patch files the cell under the floor the
                    // search looks into.
                    _ = new LocalTargetInfo(FiledCell);
                }
                ThrewOn = pawn.Map;
                Throws++;
                throw new InvalidOperationException(SearchThrowMessage);
            }

            internal static void Disarm()
            {
                ArmedPawn = null;
                ArmedMap = null;
                FiledCell = IntVec3.Invalid;
            }
        }

        private void RunSearchThrowStateCheck()
        {
            const string label = "search throw state";
            Harmony harmony = new Harmony("zlevels.validation.searchthrow");
            MethodInfo vanillaSearch = AccessTools.Method(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage));
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
                Func<Map> lookedInto = () => (Map)lookedIntoField.GetValue(null);

                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                Building_StairsUp stairs = SetUpLowerStairs(surface, tracker, out Map lower);
                lower.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 2);

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

                harmony.Patch(vanillaSearch, prefix: new HarmonyMethod(typeof(SearchThrowProbe), nameof(SearchThrowProbe.Prefix)) { priority = Priority.First });
                patched = true;

                // The full search, set up as the Z-Levels prefix sets it up (JobPatches.TryIssueJobPackagePatch): the tracker is reset, which makes
                // it the tracker that cells are filed into, and marked as searching.
                IntVec3 colonistCell = colonist.Position;
                tracker.ResetJobTrackerFor(colonist);
                JobTracker jobTracker = tracker.jobTracker[colonist];
                jobTracker.searchingJobsNow = true;
                jobTracker.oldMap = surface;
                jobTracker.pawn = colonist;
                SearchThrowProbe.Throws = 0;
                SearchThrowProbe.ThrewOn = null;
                SearchThrowProbe.ArmedPawn = colonist;
                SearchThrowProbe.ArmedMap = lower;
                SearchThrowProbe.FiledCell = cell;
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
                Record("the floor field is the colonist's own floor after a failed search", lookedOwn ? "PASS" : "FAIL: the floor field is " + (lookedAfter?.ToString() ?? "null"));
                List<IntVec3> filedBelow = jobTracker.lookedAtLocalCellMap == null ? new List<IntVec3>() : jobTracker.lookedAtLocalCellMap.Where(entry => entry.Value == lower).Select(entry => entry.Key).ToList();
                bool noneFiledBelow = filedBelow.Count == 0;
                Record("no cell stays filed under the floor where the search failed", noneFiledBelow ? "PASS" : "FAIL: " + filedBelow.Count + " cells are filed under the lower floor, among them " + string.Join(", ", filedBelow.Take(3)));
                // The reach check builds a target from the cell, which files the cell again under the floor field, and then reads it.
                bool reachAfter = colonist.CanReach(cell, PathEndMode.OnCell, Danger.Deadly);
                Record("the colonist still reaches the cell on its own floor after a failed search", reachAfter ? "PASS" : "FAIL: the colonist's reach to " + cell + " is refused; it is filed under " + DescribeFiled(jobTracker, cell));

                // The paused search, which looks into the own floor only. The job's floor starts empty.
                jobTracker.mapDest = null;
                SearchThrowProbe.Throws = 0;
                SearchThrowProbe.ThrewOn = null;
                SearchThrowProbe.ArmedPawn = colonist;
                SearchThrowProbe.ArmedMap = surface;
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
                Record("the floor field is the colonist's own floor after a failed own-floor search", lookedOwnAfterOwn ? "PASS" : "FAIL: the floor field is " + (lookedAfterOwn?.ToString() ?? "null"));
                bool marksCleared = !(bool)vanillaCallRunning.GetValue(null) && giverFilter.GetValue(null) == null && ownFloorOnlyPawn.GetValue(null) == null;
                Record("the search marks are cleared after both failed searches", marksCleared ? "PASS" : "FAIL: vanilla call running " + vanillaCallRunning.GetValue(null) + ", giver filter " + (giverFilter.GetValue(null) == null ? "none" : "set") + ", own-floor colonist " + (ownFloorOnlyPawn.GetValue(null) ?? "none"));

                harmony.Unpatch(vanillaSearch, HarmonyPatchType.Prefix, harmony.Id);
                patched = false;

                // A normal search that finds nothing, through the Z-Levels prefix, with nothing thrown (decision D13, 2026-11-10). It looks into the
                // lower floor last, and must leave the floor field on the own floor too. The tracker stays the one being filled after this search.
                int otherFloorRunsBefore = FloorPolicy.OtherFloorSearchRuns;
                Job emptyJob = SearchWorkJob(colonist);
                int otherFloorRuns = FloorPolicy.OtherFloorSearchRuns - otherFloorRunsBefore;
                bool emptyBelow = emptyJob == null && otherFloorRuns >= 1;
                Record("a search that finds nothing looks into the lower floor", emptyBelow ? "PASS: " + otherFloorRuns + " searches on other floors" : "FAIL: job " + (emptyJob == null ? "none" : emptyJob.def.defName) + ", " + otherFloorRuns + " searches on other floors");
                Map lookedAfterEmpty = lookedInto();
                bool lookedOwnAfterEmpty = lookedAfterEmpty == surface;
                Record("the floor field is the colonist's own floor after a search that finds nothing", lookedOwnAfterEmpty ? "PASS" : "FAIL: the floor field is " + (lookedAfterEmpty?.ToString() ?? "null"));
                bool reachAfterEmpty = colonist.CanReach(cell, PathEndMode.OnCell, Danger.Deadly);
                Record("the colonist still reaches the cell on its own floor after a search that finds nothing", reachAfterEmpty ? "PASS" : "FAIL: the colonist's reach to " + cell + " is refused; it is filed under " + DescribeFiled(jobTracker, cell));
                Record("tracker after a search that finds nothing", "INFO: the tracker being filled is " + (LocalTargetInfo_Constructor_Patch.curPawnJobTracker == jobTracker ? "this colonist's" : LocalTargetInfo_Constructor_Patch.curPawnJobTracker == null ? "none" : "another colonist's"));

                bool all = baseline && failedBelow && back && mapDestCleared && lookedOwn && noneFiledBelow && reachAfter && failedOwn && ownMapDestCleared && lookedOwnAfterOwn && marksCleared
                    && emptyBelow && lookedOwnAfterEmpty && reachAfterEmpty;
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
                if (patched)
                {
                    harmony.Unpatch(vanillaSearch, HarmonyPatchType.Prefix, harmony.Id);
                }
            }
            Root.Shutdown();
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

        private static string DescribeFiled(JobTracker jobTracker, IntVec3 cell)
        {
            if (jobTracker.lookedAtLocalCellMap == null || !jobTracker.lookedAtLocalCellMap.TryGetValue(cell, out Map map))
            {
                return "no floor";
            }
            return map?.ToString() ?? "null";
        }
    }
}
