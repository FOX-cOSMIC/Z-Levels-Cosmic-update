using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    // Decision D9, answered 2026-11-08 as (a): profile the work search's own code, with no mod change. For one, two and three floors the phase
    // times the colonists' job search twice. The first pass has no timers and is the baseline. The second pass has timers on the search and on the
    // steps inside it. The timers are added and removed here, in the harness. Nothing in ZLevels/ changes. The plan is
    // docs/superpowers/plans/2026-11-08-work-search-profile.md.
    public partial class RuntimeValidation
    {
        private const string profileLabel = "work search profile";
        private const string profileOwner = "zlevels.validation.worksearchprofile";
        private const int profileRounds = 3;
        private const int profileCallsPerColonist = 20;

        private void RunWorkSearchProfile()
        {
            const int colonistCount = 10;
            bool leftPatches = false;
            List<ProfileFloors> floors = new List<ProfileFloors>();
            Rand.PushState(20261106);
            try
            {
                Map surface = Find.CurrentMap;
                ZLevelsManager tracker = Current.Game.GetComponent<ZLevelsManager>();
                RequireFloors(tracker, surface, 1);
                IntVec3 block = FindCentralOpenBlock(surface, 7, 7);
                if (!block.IsValid)
                {
                    throw new InvalidOperationException("no open 7 by 7 block on the surface away from the edges");
                }
                IntVec3 near = block + new IntVec3(3, 0, 3);
                List<IntVec3> avoid = new List<IntVec3>();
                List<Pawn> colonists = new List<Pawn>();
                for (int i = 0; i < colonistCount; i++)
                {
                    colonists.Add(SpawnColonistNear(surface, near, avoid));
                }
                Harmony harmony = new Harmony(profileOwner);
                floors.Add(MeasureProfile(harmony, colonists, 1));
                Map lower = CreateLevelBelow(tracker, surface);
                lower.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 2);
                floors.Add(MeasureProfile(harmony, colonists, 2));
                Map upper = tracker.CreateUpperLevel(surface, surface.Center);
                upper.fogGrid.ClearAllFog();
                RequireFloors(tracker, surface, 3);
                floors.Add(MeasureProfile(harmony, colonists, 3));

                foreach (ProfileFloors floor in floors)
                {
                    leftPatches |= floor.LeftPatches;
                    Record("work search profile, " + floor.Floors + (floor.Floors == 1 ? " floor" : " floors"), "INFO: " + floor.Text());
                }

                // Every call of the search is one outermost activation of the search timer, and a second floor or more runs the own-floor search.
                List<string> recordProblems = new List<string>();
                if (leftPatches) recordProblems.Add("a timer patch is left after removal");
                foreach (ProfileFloors floor in floors)
                {
                    foreach (ProfileRound round in floor.Rounds)
                    {
                        if (round.Count("search") != round.Calls)
                        {
                            recordProblems.Add(floor.Floors + " floors: the search timer counted " + round.Count("search") + " of " + round.Calls + " calls");
                        }
                        if (floor.Floors > 1 && round.Count("own floor search") == 0)
                        {
                            recordProblems.Add(floor.Floors + " floors: the own-floor search never ran");
                        }
                    }
                }
                // A probe nested inside another probe cannot take more time than its parent, so these hold for every round.
                List<string> containmentProblems = new List<string>();
                foreach (ProfileFloors floor in floors)
                {
                    foreach (ProfileRound round in floor.Rounds)
                    {
                        if (round.Ticks("own floor search") > round.Ticks("search")) containmentProblems.Add(floor.Floors + " floors: own floor search exceeds search");
                        if (round.Ticks("vanilla maps") > round.Ticks("own floor search")) containmentProblems.Add(floor.Floors + " floors: vanilla maps exceed own floor search");
                        if (round.Ticks("other floors") > round.Ticks("own floor search")) containmentProblems.Add(floor.Floors + " floors: other floors exceed own floor search");
                    }
                }
                Record("work search profile recorded", recordProblems.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", recordProblems));
                Record("work search profile containment", containmentProblems.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", containmentProblems));
                Record(profileLabel, recordProblems.Count == 0 && containmentProblems.Count == 0
                    ? "PASS"
                    : "FAIL: " + string.Join("; ", recordProblems.Concat(containmentProblems)));
            }
            catch (Exception exception)
            {
                Record(profileLabel, "FAIL: " + exception);
                Log.Error("Z-Levels work search profile validation failed: " + exception);
            }
            finally
            {
                Rand.PopState();
            }
            Root.Shutdown();
        }

        // One floor count: an untimed pass, then the baseline rounds with no timers, then the timed rounds. The timers come off in a finally block.
        private ProfileFloors MeasureProfile(Harmony harmony, List<Pawn> colonists, int floors)
        {
            ProfileFloors result = new ProfileFloors { Floors = floors };
            RunProfileCalls(colonists, profileCallsPerColonist, out int warmCalls, out int warmJobs);
            for (int round = 0; round < profileRounds; round++)
            {
                double ms = RunProfileCalls(colonists, profileCallsPerColonist, out int calls, out int jobs);
                result.Rounds.Add(new ProfileRound { Calls = calls, Jobs = jobs, BaselineMicros = ms * 1000.0 / calls });
            }
            WorkSearchTimer.Install(harmony, ProfileSpecs());
            try
            {
                for (int round = 0; round < profileRounds; round++)
                {
                    WorkSearchTimer.Reset();
                    double ms = RunProfileCalls(colonists, profileCallsPerColonist, out int calls, out int jobs);
                    ProfileRound timed = result.Rounds[round];
                    timed.TimedMicros = ms * 1000.0 / calls;
                    timed.Readings = WorkSearchTimer.Snapshot();
                    if (timed.Calls != calls) throw new InvalidOperationException("the timed round made " + calls + " calls, the baseline " + timed.Calls);
                }
            }
            finally
            {
                result.LeftPatches = WorkSearchTimer.Remove(harmony);
            }
            return result;
        }

        // The probes. Each row name is the probe's name. The first is the whole search, and the rest are the steps inside it. The mod's search
        // and utility classes are internal, so they are found by name. Count-only probes time lazy sequences, which do their work when read.
        private static List<WorkSearchTimer.Spec> ProfileSpecs()
        {
            Type search = AccessTools.TypeByName("ZLevels.OwnFloorFirstSearch");
            Type utils = AccessTools.TypeByName("ZLevels.ZUtils");
            if (search == null || utils == null)
            {
                throw new InvalidOperationException("the mod's search or utility class is not loaded");
            }
            return new List<WorkSearchTimer.Spec>
            {
                new WorkSearchTimer.Spec(AccessTools.Method(typeof(JobGiver_Work), "TryIssueJobPackage"), "search", false),
                new WorkSearchTimer.Spec(AccessTools.Method(search, "Search"), "own floor search", false),
                new WorkSearchTimer.Spec(AccessTools.Method(search, "RunVanillaSearch"), "vanilla maps", false),
                new WorkSearchTimer.Spec(AccessTools.Method(search, "SearchOtherFloors"), "other floors", false),
                new WorkSearchTimer.Spec(AccessTools.Method(utils, "TeleportThing"), "teleport", false),
                new WorkSearchTimer.Spec(AccessTools.Method(typeof(ZLevelsManager), "ResetJobTrackerFor"), "reset tracker", false),
                new WorkSearchTimer.Spec(AccessTools.Method(typeof(FloorPolicy), "Allowed"), "floor policy", true),
                new WorkSearchTimer.Spec(AccessTools.Method(utils, "GetAllMapsInClosestOrder"), "maps in order", true),
                new WorkSearchTimer.Spec(AccessTools.Method(typeof(FloorRestore), "Dispose"), "floor restore", false),
            };
        }

        // One pass: each colonist searches callsPerColonist times, in turn. The clock moves past the idle pause before each call, and only the
        // calls are timed. Returns the milliseconds the calls took, and how many of them found a job.
        private static double RunProfileCalls(List<Pawn> colonists, int callsPerColonist, out int calls, out int jobs)
        {
            calls = 0;
            jobs = 0;
            Stopwatch watch = new Stopwatch();
            for (int call = 0; call < callsPerColonist; call++)
            {
                foreach (Pawn colonist in colonists)
                {
                    AdvanceClockPastIdlePause();
                    watch.Start();
                    Job job = SearchWorkJob(colonist);
                    watch.Stop();
                    calls++;
                    if (job != null) jobs++;
                }
            }
            return watch.Elapsed.TotalMilliseconds;
        }

        private static string Fixed(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private sealed class ProfileRound
        {
            public int Calls;
            public int Jobs;
            public double BaselineMicros;
            public double TimedMicros;
            public List<WorkSearchTimer.Reading> Readings = new List<WorkSearchTimer.Reading>();

            public long Ticks(string name)
            {
                return Readings.First(reading => reading.Name == name).Ticks;
            }

            public int Count(string name)
            {
                return Readings.First(reading => reading.Name == name).Count;
            }
        }

        private sealed class ProfileFloors
        {
            public int Floors;
            public bool LeftPatches;
            public List<ProfileRound> Rounds = new List<ProfileRound>();

            public string Text()
            {
                double baseline = Rounds.Average(round => round.BaselineMicros);
                double timed = Rounds.Average(round => round.TimedMicros);
                double low = Rounds.Min(round => round.BaselineMicros);
                double high = Rounds.Max(round => round.BaselineMicros);
                long searchTicks = Rounds.Sum(round => round.Ticks("search"));
                int calls = Rounds.Sum(round => round.Calls);
                int jobs = Rounds.Sum(round => round.Jobs);
                double perCall = searchTicks == 0 ? 0 : (double)searchTicks * 1000000.0 / Stopwatch.Frequency / calls;
                string text = "no timers " + Fixed(baseline, "0.0") + " us per call (rounds " + Fixed(low, "0.0") + " to " + Fixed(high, "0.0")
                    + "); timers on " + Fixed(timed, "0.0") + " us per call, an overhead of " + Fixed(timed - baseline, "0.0")
                    + " us; the search took " + Fixed(perCall, "0.0") + " us of timed time per call; " + jobs + " of " + calls + " calls found a job";
                if (searchTicks == 0) return text;
                text += "; share of the search: own floor search " + Share("own floor search", searchTicks)
                    + ", of which vanilla maps " + Share("vanilla maps", Rounds.Sum(round => round.Ticks("own floor search")))
                    + " and other floors " + Share("other floors", Rounds.Sum(round => round.Ticks("own floor search")))
                    + "; teleport " + Share("teleport", searchTicks) + " (" + PerCall("teleport", calls) + " per call); reset tracker "
                    + Share("reset tracker", searchTicks) + " (" + PerCall("reset tracker", calls) + " per call); floor restore "
                    + Share("floor restore", searchTicks) + " (" + PerCall("floor restore", calls) + " per call)";
                text += "; count only: floor policy " + PerCall("floor policy", calls) + " per call, maps in order " + PerCall("maps in order", calls)
                    + " per call, vanilla maps " + PerCall("vanilla maps", calls) + " per call";
                return text;
            }

            private string Share(string name, long denominator)
            {
                long ticks = Rounds.Sum(round => round.Ticks(name));
                return denominator == 0 ? "0%" : Fixed(100.0 * ticks / denominator, "0.0") + "%";
            }

            private string PerCall(string name, int calls)
            {
                int count = Rounds.Sum(round => round.Count(name));
                return Fixed((double)count / calls, "0.00");
            }
        }
    }

    // The timers the work search profile adds at run time. Harmony runs Enter before a probed method and Exit after it. Exit is a finalizer, so
    // it runs when the method throws as well as when it returns. Only the outermost activation of a probe adds time and a count, so a call that
    // is nested inside its own method is inside its parent's time and is counted once.
    public static class WorkSearchTimer
    {
        public sealed class Spec
        {
            public readonly MethodBase Method;
            public readonly string Name;
            public readonly bool CountOnly;

            public Spec(MethodBase method, string name, bool countOnly)
            {
                Method = method ?? throw new InvalidOperationException("no method for the probe " + name);
                Name = name;
                CountOnly = countOnly;
            }
        }

        public sealed class Reading
        {
            public string Name;
            public bool CountOnly;
            public long Ticks;
            public int Count;
        }

        private sealed class Probe
        {
            public MethodBase Method;
            public string Name;
            public bool CountOnly;
            public int Depth;
            public long Started;
            public long Ticks;
            public int Count;
        }

        private static readonly MethodInfo enter = AccessTools.Method(typeof(WorkSearchTimer), "Enter");
        private static readonly MethodInfo exit = AccessTools.Method(typeof(WorkSearchTimer), "Exit");
        private static List<Probe> active = new List<Probe>();

        // Adds a prefix and a finalizer to each probed method, and starts the timers at zero.
        public static void Install(Harmony harmony, List<Spec> specs)
        {
            active = new List<Probe>();
            foreach (Spec spec in specs)
            {
                active.Add(new Probe { Method = spec.Method, Name = spec.Name, CountOnly = spec.CountOnly });
                // Harmony runs a higher priority first. The mod's prefix has the default, 400, so Priority.First (800) runs the timer before it.
                // Priority.Last (0) would run it after the mod's prefix, and the nested vanilla call would then be counted as a second call.
                harmony.Patch(spec.Method, prefix: new HarmonyMethod(enter) { priority = HarmonyLib.Priority.First }, finalizer: new HarmonyMethod(exit));
            }
        }

        public static void Reset()
        {
            foreach (Probe probe in active)
            {
                probe.Depth = 0;
                probe.Ticks = 0;
                probe.Count = 0;
            }
        }

        public static List<Reading> Snapshot()
        {
            return active.Select(probe => new Reading { Name = probe.Name, CountOnly = probe.CountOnly, Ticks = probe.Ticks, Count = probe.Count }).ToList();
        }

        // Removes the two patches of each probe, then checks that no patch of ours is left on any probed method. Returns true when one is left.
        public static bool Remove(Harmony harmony)
        {
            bool left = false;
            foreach (Probe probe in active)
            {
                harmony.Unpatch(probe.Method, enter);
                harmony.Unpatch(probe.Method, exit);
                var info = Harmony.GetPatchInfo(probe.Method);
                if (info != null && (info.Prefixes.Any(patch => patch.PatchMethod == enter) || info.Finalizers.Any(patch => patch.PatchMethod == exit)))
                {
                    left = true;
                }
            }
            active = new List<Probe>();
            return left;
        }

        public static void Enter(MethodBase __originalMethod)
        {
            Probe probe = Find(__originalMethod);
            if (probe == null) return;
            if (probe.Depth++ == 0) probe.Started = Stopwatch.GetTimestamp();
        }

        public static void Exit(MethodBase __originalMethod)
        {
            Probe probe = Find(__originalMethod);
            if (probe == null) return;
            if (--probe.Depth == 0)
            {
                probe.Ticks += Stopwatch.GetTimestamp() - probe.Started;
                probe.Count++;
            }
        }

        private static Probe Find(MethodBase method)
        {
            RuntimeMethodHandle handle = method.MethodHandle;
            foreach (Probe probe in active)
            {
                if (probe.Method.MethodHandle.Equals(handle)) return probe;
            }
            return null;
        }
    }
}
