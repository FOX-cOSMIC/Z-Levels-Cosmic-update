using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using ZLevels;

namespace ZLevelsValidation
{
    // Task 24 (idea "leave the busiest questions alone"): the per-patch timing that decides the gate. Each patch of the reach, reservation and attack
    // groups is removed from the running game in turn with Harmony.Unpatch, timed against a baseline taken before and after the removal, and
    // applied again with Harmony.Patch. The removal is for this measurement only. The mod's code does not change.
    public partial class RuntimeValidation
    {
        // Calls per colonist for one work-search measurement. Ten colonists make 500 calls, as in the Task 19 timing.
        private const int PatchTimingCallsEach = 50;
        // Calls for one attack-probe measurement.
        private const int PatchTimingProbeCalls = 500;
        // Each toggle is measured this many times. Every removal has a baseline before and after it.
        private const int PatchTimingRounds = 3;

        // One method that a mod patch added to a game method.
        private struct PatchPiece
        {
            public MethodBase Original;
            public MethodInfo Method;
            public bool Prefix;
        }

        // One mod patch as a unit. Its pieces are removed and applied together.
        private sealed class PatchToggle
        {
            public string Name;
            public bool Attack;
            public List<PatchPiece> Pieces = new List<PatchPiece>();
        }

        // One metric of one toggle over the rounds: the baselines around each removal, and the time without the patch.
        private sealed class PatchTimingLine
        {
            public string Name;
            public string Metric;
            public List<double> Baselines = new List<double>();
            public List<double> Without = new List<double>();
            public int Calls;
            public int Found;
            public int CrossFloor;
            public bool Integrity = true;
            public string Problem = "";

            public string Text()
            {
                double baselineMean = Baselines.Average();
                double withoutMean = Without.Average();
                double change = (withoutMean - baselineMean) / baselineMean * 100.0;
                return Metric + ": baseline mean " + OneDecimal(baselineMean) + " us per call (range " + OneDecimal(Baselines.Min()) + " to "
                    + OneDecimal(Baselines.Max()) + "), without the patch " + OneDecimal(withoutMean) + " us (range " + OneDecimal(Without.Min()) + " to "
                    + OneDecimal(Without.Max()) + "), change " + OneDecimal(change) + " percent; " + Calls + " calls per measure, " + Found
                    + " calls found a job or target, " + CrossFloor + " cross-floor searches in the baselines";
            }
        }

        // The counts of one attack-probe measurement: the time per call and the calls that found a target, for each of the two probes.
        private struct ProbeResult
        {
            public double FindMicroseconds;
            public int FindFound;
            public double ShootMicroseconds;
            public int ShootFound;
        }

        // Runs the per-patch phase on the three-floor colony. The probe is a colonist outside the work-search group, and it holds a revolver for
        // the attack metrics. Returns one line per toggle and metric. Every patch is applied again before this returns, also after a fault.
        private static List<PatchTimingLine> TimePatchRemovals(ZLevelsManager tracker, List<Pawn> colonists, Pawn probe)
        {
            Harmony harmony = new Harmony("zlevels.validation.task24");
            List<PatchTimingLine> lines = new List<PatchTimingLine>();

            probe.equipment.DestroyAllEquipment();
            probe.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_Revolver")));
            // The base class is abstract. The concrete subclass does not override FindAttackTarget, so the mod's patch on the base method covers it.
            JobGiver_AIFightEnemy jobGiver = new JobGiver_AIFightEnemies();
            MethodInfo findAttack = typeof(JobGiver_AIFightEnemy).GetMethod("FindAttackTarget",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Pawn) }, null);
            if (findAttack == null) throw new InvalidOperationException("JobGiver_AIFightEnemy.FindAttackTarget(Pawn) was not found");

            foreach (PatchToggle toggle in PatchToggles())
            {
                if (!toggle.Attack)
                {
                    PatchTimingLine line = new PatchTimingLine { Name = toggle.Name, Metric = "work search, three floors" };
                    for (int round = 0; round < PatchTimingRounds; round++)
                    {
                        WorkSearchTiming before = default(WorkSearchTiming);
                        WorkSearchTiming without = default(WorkSearchTiming);
                        WorkSearchTiming after = default(WorkSearchTiming);
                        RemoveAndRestore(harmony, toggle, new[] { line },
                            () => before = TimeWorkSearches(tracker, colonists, PatchTimingCallsEach),
                            () => without = TimeWorkSearches(tracker, colonists, PatchTimingCallsEach),
                            () => after = TimeWorkSearches(tracker, colonists, PatchTimingCallsEach));
                        line.Baselines.Add(PerCall(before));
                        line.Baselines.Add(PerCall(after));
                        line.Without.Add(PerCall(without));
                        line.Calls += without.Calls;
                        line.Found += without.Jobs;
                        line.CrossFloor += before.CrossFloor;
                    }
                    lines.Add(line);
                    continue;
                }

                PatchTimingLine findLine = new PatchTimingLine { Name = toggle.Name, Metric = "FindAttackTarget, probe colonist" };
                PatchTimingLine shootLine = new PatchTimingLine { Name = toggle.Name, Metric = "BestShootTargetFromCurrentPosition, probe colonist" };
                for (int round = 0; round < PatchTimingRounds; round++)
                {
                    ProbeResult before = default(ProbeResult);
                    ProbeResult without = default(ProbeResult);
                    ProbeResult after = default(ProbeResult);
                    RemoveAndRestore(harmony, toggle, new[] { findLine, shootLine },
                        () => before = ProbeBoth(jobGiver, findAttack, probe),
                        () => without = ProbeBoth(jobGiver, findAttack, probe),
                        () => after = ProbeBoth(jobGiver, findAttack, probe));
                    findLine.Baselines.Add(before.FindMicroseconds);
                    findLine.Baselines.Add(after.FindMicroseconds);
                    findLine.Without.Add(without.FindMicroseconds);
                    findLine.Calls += PatchTimingProbeCalls;
                    findLine.Found += without.FindFound;
                    shootLine.Baselines.Add(before.ShootMicroseconds);
                    shootLine.Baselines.Add(after.ShootMicroseconds);
                    shootLine.Without.Add(without.ShootMicroseconds);
                    shootLine.Calls += PatchTimingProbeCalls;
                    shootLine.Found += without.ShootFound;
                }
                lines.Add(findLine);
                lines.Add(shootLine);
            }
            return lines;
        }

        // Removes the toggle, runs the measurement without it, and applies the toggle again. Each step is checked, and a step that did not take
        // effect is written to the lines. The apply is in a finally block, so the game keeps its patches after a fault.
        private static void RemoveAndRestore(Harmony harmony, PatchToggle toggle, PatchTimingLine[] lines, Action before, Action without, Action after)
        {
            if (!AllApplied(toggle))
            {
                foreach (PatchTimingLine line in lines) Fail(line, "the patch was not applied before its removal");
                return;
            }
            try
            {
                before();
                Remove(harmony, toggle);
                if (AnyApplied(toggle))
                {
                    foreach (PatchTimingLine line in lines) Fail(line, "the removal did not take effect");
                }
                without();
            }
            finally
            {
                Apply(harmony, toggle);
                if (!AllApplied(toggle))
                {
                    foreach (PatchTimingLine line in lines) Fail(line, "the patch was not applied again");
                }
            }
            after();
        }

        private static void Fail(PatchTimingLine line, string problem)
        {
            line.Integrity = false;
            if (line.Problem.Length > 0) line.Problem += "; ";
            line.Problem += line.Name + " (" + line.Metric + "): " + problem;
        }

        private static double PerCall(WorkSearchTiming timing)
        {
            return timing.Calls == 0 ? double.NaN : timing.Milliseconds * 1000.0 / timing.Calls;
        }

        // Both attack probes, timed over PatchTimingProbeCalls calls each. FindAttackTarget is protected, so it runs through reflection; the
        // reflection cost is the same before and after each removal.
        private static ProbeResult ProbeBoth(JobGiver_AIFightEnemy jobGiver, MethodInfo findAttack, Pawn probe)
        {
            ProbeResult result = new ProbeResult();
            Stopwatch watch = Stopwatch.StartNew();
            for (int call = 0; call < PatchTimingProbeCalls; call++)
            {
                if (findAttack.Invoke(jobGiver, new object[] { probe }) != null) result.FindFound++;
            }
            watch.Stop();
            result.FindMicroseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / PatchTimingProbeCalls;

            watch.Restart();
            for (int call = 0; call < PatchTimingProbeCalls; call++)
            {
                if (AttackTargetFinder.BestShootTargetFromCurrentPosition(probe, TargetScanFlags.NeedThreat) != null) result.ShootFound++;
            }
            watch.Stop();
            result.ShootMicroseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / PatchTimingProbeCalls;
            return result;
        }

        // The mod's patches as units. Each toggle names the game method and the patch methods of one mod patch class.
        private static List<PatchToggle> PatchToggles()
        {
            return new List<PatchToggle>
            {
                Toggle("CanReach", false,
                    GameMethod(typeof(Reachability), "CanReach", new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) }),
                    typeof(CanReach_Patch), true),
                Toggle("CanReserve", false,
                    GameMethod(typeof(ReservationManager), "CanReserve", new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(int), typeof(int), typeof(ReservationLayerDef), typeof(bool) }),
                    typeof(ReservationManager_CanReserve_Patch), true),
                Toggle("CanReserveStack", false,
                    GameMethod(typeof(ReservationManager), "CanReserveStack", new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(int), typeof(ReservationLayerDef), typeof(bool) }),
                    typeof(ReservationManager_CanReserveStack_Patch), true),
                Toggle("BestShootTargetFromCurrentPosition", true,
                    GameMethod(typeof(AttackTargetFinder), "BestShootTargetFromCurrentPosition", new[] { typeof(IAttackTargetSearcher), typeof(TargetScanFlags), typeof(Predicate<Thing>), typeof(float), typeof(float) }),
                    typeof(BestShootTargetFromCurrentPosition_Patch), true),
                Toggle("FindAttackTarget", true,
                    GameMethod(typeof(JobGiver_AIFightEnemy), "FindAttackTarget", new[] { typeof(Pawn) }),
                    typeof(FindAttackTarget_Patch), true),
                Toggle("BestAttackTarget", true,
                    GameMethod(typeof(AttackTargetFinder), "BestAttackTarget", new[] { typeof(IAttackTargetSearcher), typeof(TargetScanFlags), typeof(Predicate<Thing>), typeof(float), typeof(float), typeof(IntVec3), typeof(float), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }),
                    typeof(CombatPatches_BestAttackTarget_Patch), false),
            };
        }

        private static MethodInfo GameMethod(Type declaring, string name, Type[] parameters)
        {
            MethodInfo method = declaring.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, parameters, null);
            if (method == null) throw new InvalidOperationException(declaring.Name + "." + name + " with " + parameters.Length + " parameters was not found");
            return method;
        }

        private static PatchToggle Toggle(string name, bool attack, MethodInfo original, Type patchClass, bool hasPostfix)
        {
            PatchToggle toggle = new PatchToggle { Name = name, Attack = attack };
            toggle.Pieces.Add(Piece(original, patchClass, "Prefix", true));
            if (hasPostfix) toggle.Pieces.Add(Piece(original, patchClass, "Postfix", false));
            return toggle;
        }

        private static PatchPiece Piece(MethodInfo original, Type patchClass, string methodName, bool prefix)
        {
            MethodInfo patch = patchClass.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (patch == null) throw new InvalidOperationException(patchClass.Name + "." + methodName + " was not found");
            return new PatchPiece { Original = original, Method = patch, Prefix = prefix };
        }

        private static void Remove(Harmony harmony, PatchToggle toggle)
        {
            foreach (PatchPiece piece in toggle.Pieces) harmony.Unpatch(piece.Original, piece.Method);
        }

        private static void Apply(Harmony harmony, PatchToggle toggle)
        {
            foreach (PatchPiece piece in toggle.Pieces)
            {
                if (IsApplied(piece)) continue;
                if (piece.Prefix) harmony.Patch(piece.Original, prefix: new HarmonyMethod(piece.Method));
                else harmony.Patch(piece.Original, postfix: new HarmonyMethod(piece.Method));
            }
        }

        private static bool AllApplied(PatchToggle toggle)
        {
            return toggle.Pieces.All(IsApplied);
        }

        private static bool AnyApplied(PatchToggle toggle)
        {
            return toggle.Pieces.Any(IsApplied);
        }

        private static bool IsApplied(PatchPiece piece)
        {
            var info = Harmony.GetPatchInfo(piece.Original);
            if (info == null) return false;
            var patches = piece.Prefix ? info.Prefixes : info.Postfixes;
            return patches.Any(patch => patch.PatchMethod == piece.Method);
        }
    }
}
